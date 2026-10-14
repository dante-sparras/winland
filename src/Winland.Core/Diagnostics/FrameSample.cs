using System.Globalization;

namespace Winland.Core.Diagnostics;

/// <summary>
/// Times arranges and idle ticks for five seconds after <see cref="Start"/>.
/// Four tiled windows or fewer are ordinary. A heavier snapshot is reported only when one was seen.
/// After that, every <see cref="PaceMilliseconds"/> it reports the median, 95th percentile, and slowest
/// loop turn, arrange, and chord of that stretch, so a change can be compared on a Host while it runs.
/// Call from one thread.
/// </summary>
public sealed class FrameSample
{
    /// <summary>Tiled windows that still count as an ordinary scene.</summary>
    public const int OrdinaryWindows = 4;

    /// <summary>How long samples are kept, in milliseconds.</summary>
    public const long WindowMilliseconds = 5_000;

    /// <summary>How long one pace summary covers, in milliseconds.</summary>
    public const long PaceMilliseconds = 600_000;

    private readonly List<double> _ordinaryArrange = [];
    private readonly List<double> _ordinaryIdle = [];
    private readonly List<double> _heavyArrange = [];
    private readonly List<double> _heavyIdle = [];
    private readonly List<double> _paceTurn = [];
    private readonly List<double> _paceArrange = [];
    private readonly List<double> _paceChord = [];
    private long _started = long.MinValue;
    private long _paceStarted = long.MinValue;
    private bool _closed;

    /// <summary>Opens the five-second window and the first pace stretch at <paramref name="nowMilliseconds"/>.</summary>
    /// <param name="nowMilliseconds">Clock reading, in milliseconds.</param>
    public void Start(long nowMilliseconds)
    {
        _started = nowMilliseconds;
        _paceStarted = nowMilliseconds;
    }

    /// <summary>Records an arrange. The note that reaches the end of the window returns the summary once.</summary>
    /// <param name="nowMilliseconds">Clock reading, in milliseconds.</param>
    /// <param name="milliseconds">How long the arrange took.</param>
    /// <param name="tiledWindows">Tiled windows in that snapshot.</param>
    /// <returns>The summary when this note closes the window. Otherwise null.</returns>
    public string? NoteArrange(long nowMilliseconds, double milliseconds, int tiledWindows) =>
        Join(Note(nowMilliseconds, milliseconds, tiledWindows, arrange: true), Pace(nowMilliseconds, _paceArrange, milliseconds));

    /// <summary>Records an idle tick. The note that reaches the end of the window returns the summary once.</summary>
    /// <param name="nowMilliseconds">Clock reading, in milliseconds.</param>
    /// <param name="milliseconds">How long the tick took.</param>
    /// <param name="tiledWindows">Tiled windows in that snapshot.</param>
    /// <returns>The summary when this note closes the window. Otherwise null.</returns>
    public string? NoteIdle(long nowMilliseconds, double milliseconds, int tiledWindows) =>
        Join(Note(nowMilliseconds, milliseconds, tiledWindows, arrange: false), Pace(nowMilliseconds, _paceTurn, milliseconds));

    /// <summary>Records a chord, from the moment the hook queued it until the shell finished running it.</summary>
    /// <param name="nowMilliseconds">Clock reading, in milliseconds.</param>
    /// <param name="milliseconds">How long the chord waited and ran.</param>
    /// <returns>The pace summary when this note closes a stretch. Otherwise null.</returns>
    public string? NoteChord(long nowMilliseconds, double milliseconds) => Pace(nowMilliseconds, _paceChord, milliseconds);

    /// <summary>Counts one sample in the current pace stretch. The note that reaches the end of a stretch opens the next one and is counted there.</summary>
    private string? Pace(long nowMilliseconds, List<double> samples, double milliseconds)
    {
        if (_paceStarted == long.MinValue)
        {
            return null;
        }

        string? summary = null;
        if (nowMilliseconds >= _paceStarted + PaceMilliseconds)
        {
            summary = PaceSummary(nowMilliseconds - _paceStarted);
            _paceStarted = nowMilliseconds;
        }

        samples.Add(milliseconds);
        return summary;
    }

    private string? PaceSummary(long spanMilliseconds)
    {
        var text = "pace " + (spanMilliseconds / 1000).ToString(CultureInfo.InvariantCulture) + "s";
        var any = false;
        any |= AppendPace(ref text, "turn", _paceTurn);
        any |= AppendPace(ref text, "arrange", _paceArrange);
        any |= AppendPace(ref text, "chord", _paceChord);
        return any ? text : null;
    }

    /// <summary>Appends one kind's figures and clears its samples. Sorted in place, so a stretch allocates only as its lists grow.</summary>
    private static bool AppendPace(ref string text, string name, List<double> samples)
    {
        if (samples.Count == 0)
        {
            return false;
        }

        var sorted = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(samples);
        sorted.Sort();
        text += " " + name
            + " p50=" + Format(Rank(sorted, 0.50))
            + " p95=" + Format(Rank(sorted, 0.95))
            + " max=" + Format(sorted[^1])
            + " n=" + sorted.Length.ToString(CultureInfo.InvariantCulture);
        samples.Clear();
        return true;
    }

    /// <summary>The nearest-rank percentile of sorted samples.</summary>
    private static double Rank(ReadOnlySpan<double> sorted, double share)
    {
        var index = (int)Math.Ceiling(share * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static string? Join(string? first, string? second) =>
        first is null ? second : second is null ? first : first + " | " + second;

    private string? Note(long nowMilliseconds, double milliseconds, int tiledWindows, bool arrange)
    {
        if (_closed || _started == long.MinValue)
        {
            return null;
        }

        if (nowMilliseconds < _started + WindowMilliseconds)
        {
            Bucket(arrange, tiledWindows > OrdinaryWindows).Add(milliseconds);
            return null;
        }

        _closed = true;
        return Summary();
    }

    private List<double> Bucket(bool arrange, bool heavy)
    {
        if (arrange)
        {
            return heavy ? _heavyArrange : _ordinaryArrange;
        }

        return heavy ? _heavyIdle : _ordinaryIdle;
    }

    private string? Summary()
    {
        var parts = new List<string>();
        Append(parts, "ordinary", _ordinaryArrange, _ordinaryIdle);
        Append(parts, "heavy", _heavyArrange, _heavyIdle);
        return parts.Count == 0 ? null : "frame sample " + string.Join(' ', parts);
    }

    private static void Append(List<string> parts, string name, List<double> arrange, List<double> idle)
    {
        if (arrange.Count == 0 && idle.Count == 0)
        {
            return;
        }

        var text = name;
        if (arrange.Count > 0)
        {
            text += " arrange=" + Format(Median(arrange)) + "ms samples=" + arrange.Count.ToString(CultureInfo.InvariantCulture);
        }

        if (idle.Count > 0)
        {
            text += " idle=" + Format(Median(idle)) + "ms samples=" + idle.Count.ToString(CultureInfo.InvariantCulture);
        }

        parts.Add(text);
    }

    private static double Median(List<double> values)
    {
        var sorted = values.ToArray();
        Array.Sort(sorted);
        var mid = sorted.Length / 2;
        if (sorted.Length % 2 == 1)
        {
            return sorted[mid];
        }

        return (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static string Format(double milliseconds) =>
        milliseconds.ToString("0.###", CultureInfo.InvariantCulture);
}
