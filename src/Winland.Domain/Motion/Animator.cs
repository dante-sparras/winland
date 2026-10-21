namespace Winland.Domain;

/// <summary>
/// Steps the motions the core plans, on a clock the caller passes in milliseconds. It holds no window
/// handles and does no Win32, so the adapter applies the frames it returns.
/// </summary>
/// <remarks>
/// A channel always ends exactly on its goal. A motion that replaces a running one starts from the value
/// shown at that moment. Each change to a window's motion raises its generation, so a frame computed
/// before the change can be recognised and dropped.
/// </remarks>
public sealed class Animator
{
    private readonly Dictionary<WindowId, Entry> _entries = [];
    private long _generation;

    public int Count => _entries.Count;

    public bool Running(WindowId window) => _entries.ContainsKey(window);

    /// <summary>The current generation of the window's motion, or 0 when it has none.</summary>
    public long Generation(WindowId window) => _entries.TryGetValue(window, out var entry) ? entry.Generation : 0;

    public void Start(Motion motion, double now)
    {
        ArgumentNullException.ThrowIfNull(motion);
        if (!_entries.TryGetValue(motion.Window, out var entry))
        {
            entry = new Entry { Goal = motion.To, AlphaGoal = motion.AlphaTo };
            _entries.Add(motion.Window, entry);
        }

        entry.Generation = ++_generation;
        if (motion.Move is Timing move)
        {
            var running = motion.Warp ? null : entry.Geometry?.Value(now);
            var begun = running ?? Vector(motion.From ?? motion.To);
            entry.Geometry = new Track(begun, Vector(motion.To), now, move);
        }
        else if (entry.Geometry is not null && entry.Goal != motion.To)
        {
            entry.Geometry = null;
        }

        entry.Goal = motion.To;
        if (motion.Fade is Timing fade)
        {
            var running = motion.Warp ? null : entry.Fade?.Value(now)[0];
            var begun = running ?? motion.FadeFrom ?? entry.FadeGoal;
            entry.Fade = new Track([begun], [motion.FadeTo], now, fade);
        }
        else if (entry.Fade is not null && entry.FadeGoal != motion.FadeTo)
        {
            entry.Fade = null;
        }

        entry.FadeGoal = motion.FadeTo;
        if (motion.Alpha is Timing alpha)
        {
            var begun = entry.Alpha?.Value(now)[0] ?? motion.AlphaFrom ?? entry.AlphaGoal;
            entry.Alpha = new Track([begun], [motion.AlphaTo], now, alpha);
        }
        else if (entry.Alpha is not null && entry.AlphaGoal != motion.AlphaTo)
        {
            entry.Alpha = null;
        }

        entry.AlphaGoal = motion.AlphaTo;
        if (motion.BorderTo is BorderColor border)
        {
            if (motion.Border is Timing timing)
            {
                var begun = entry.Border?.Value(now) ?? OkLab.From(motion.BorderFrom ?? entry.BorderGoal ?? border);
                entry.Border = new Track(begun, OkLab.From(border), now, timing);
            }
            else if (entry.Border is not null && entry.BorderGoal != border)
            {
                entry.Border = null;
            }

            entry.BorderGoal = border;
        }

        entry.End = motion.End;
        entry.Rest = motion.Rest;
    }

    /// <summary>
    /// Takes a Layout rectangle that arrived without a motion. Returns true when the running motion
    /// already lands there, or hides or closes the window, so the window is left to it. Otherwise the
    /// window's movement stops and the caller places it.
    /// </summary>
    public bool Settle(WindowId window, Rectangle bounds)
    {
        if (!_entries.TryGetValue(window, out var entry))
            return false;

        if (entry.End != MotionEnd.None)
        {
            entry.Rest = bounds;
            return true;
        }

        if (entry.Geometry is null)
        {
            entry.Goal = bounds;
            entry.Rest = bounds;
            return false;
        }

        if (entry.Goal == bounds)
            return true;

        entry.Geometry = null;
        entry.Goal = bounds;
        entry.Rest = bounds;
        entry.Generation = ++_generation;
        return false;
    }

    /// <summary>
    /// Takes a window's decoration. A running fade or alpha change moves toward the new alpha instead of
    /// jumping to it, and a running border change toward the new color.
    /// </summary>
    public PaintHold Paint(WindowId window, byte alpha, BorderColor border, double now)
    {
        if (!_entries.TryGetValue(window, out var entry))
            return default;

        if (entry.Alpha is Track running && entry.AlphaGoal != alpha)
            entry.Alpha = new Track(running.Value(now), [alpha], now, running.Timing);

        entry.AlphaGoal = alpha;
        var holdsBorder = entry.Border is not null;
        if (entry.Border is Track changing && entry.BorderGoal != border)
            entry.Border = new Track(changing.Value(now), OkLab.From(border), now, changing.Timing);

        if (holdsBorder)
            entry.BorderGoal = border;

        return new PaintHold(entry.Fade is not null || entry.Alpha is not null, holdsBorder);
    }

    /// <summary>Stops the window's motion where it is. The caller puts the window on its Layout rectangle.</summary>
    public void Forget(WindowId window) => _entries.Remove(window);

    /// <summary>The frame the window shows at <paramref name="now"/>, without advancing anything.</summary>
    public Frame? Peek(WindowId window, double now) =>
        _entries.TryGetValue(window, out var entry) ? Show(window, entry, now, final: false) : null;

    /// <summary>The frame of every running motion at <paramref name="now"/>. Landed motions are removed.</summary>
    public IReadOnlyList<Frame> Step(double now)
    {
        if (_entries.Count == 0)
            return [];

        var frames = new List<Frame>(_entries.Count);
        foreach (var (window, entry) in _entries.ToArray())
        {
            var frame = Show(window, entry, now, final: false);
            entry.Geometry = Done(entry.Geometry, now);
            entry.Fade = Done(entry.Fade, now);
            entry.Alpha = Done(entry.Alpha, now);
            entry.Border = Done(entry.Border, now);
            if (entry.Geometry is null && entry.Fade is null && entry.Alpha is null && entry.Border is null)
            {
                frame = Rested(frame, entry);
                _entries.Remove(window);
            }

            frames.Add(frame);
        }

        return frames;
    }

    /// <summary>Lands every motion on its goal at once, and removes them.</summary>
    public IReadOnlyList<Frame> Land()
    {
        var frames = new List<Frame>(_entries.Count);
        foreach (var (window, entry) in _entries)
            frames.Add(Rested(Show(window, entry, double.PositiveInfinity, final: true), entry));

        _entries.Clear();
        return frames;
    }

    private static Frame Rested(Frame frame, Entry entry) => frame with
    {
        Done = true,
        End = entry.End,
        Rest = entry.Rest,
        RestAlpha = entry.AlphaGoal,
    };

    private static Frame Show(WindowId window, Entry entry, double now, bool final)
    {
        Rectangle? bounds = entry.Geometry is Track geometry ? Box(final ? geometry.Goal : geometry.Value(now)) : null;
        Rectangle? goal = entry.Geometry is Track heading ? Box(heading.Goal) : null;
        byte? alpha = null;
        if (entry.Fade is not null || entry.Alpha is not null)
        {
            var fade = entry.Fade is Track f ? (final ? f.Goal[0] : f.Value(now)[0]) : entry.FadeGoal;
            var level = !final && Running(entry.Alpha, now) ? entry.Alpha!.Value(now)[0] : entry.AlphaGoal;
            alpha = ToByte(Math.Clamp(fade, 0, 1) * level);
        }

        BorderColor? border = null;
        if (entry.Border is Track color)
            border = final || color.Finished(now) ? entry.BorderGoal : OkLab.To(color.Value(now));

        return new Frame(window, entry.Generation) { Bounds = bounds, Goal = goal, Content = goal is null ? null : entry.Rest, Alpha = alpha, Border = border };
    }

    private static bool Running(Track? track, double now) => track is not null && !track.Finished(now);

    private static Track? Done(Track? track, double now) => track is not null && track.Finished(now) ? null : track;

    private static double[] Vector(Rectangle box) => [box.X, box.Y, box.Width, box.Height];

    private static Rectangle Box(double[] value) =>
        new(Round(value[0]), Round(value[1]), Math.Max(0, Round(value[2])), Math.Max(0, Round(value[3])));

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);

    private sealed class Entry
    {
        public long Generation { get; set; }

        public Track? Geometry { get; set; }

        public Rectangle Goal { get; set; }

        public Track? Fade { get; set; }

        public double FadeGoal { get; set; } = 1;

        public Track? Alpha { get; set; }

        /// <summary>The alpha the window lands on.</summary>
        public byte AlphaGoal { get; set; } = 255;

        public Track? Border { get; set; }

        public BorderColor? BorderGoal { get; set; }

        public MotionEnd End { get; set; }

        public Rectangle Rest { get; set; }
    }

    private sealed class Track(double[] begun, double[] goal, double start, Timing timing)
    {
        public double[] Goal { get; } = goal;

        public Timing Timing { get; } = timing;

        public bool Finished(double now) => Progress(now) >= 1;

        public double[] Value(double now)
        {
            var progress = Progress(now);
            if (progress >= 1)
                return Goal;

            var eased = Timing.Curve.Ease(progress);
            var value = new double[Goal.Length];
            for (var i = 0; i < value.Length; i++)
                value[i] = begun[i] + ((Goal[i] - begun[i]) * eased);
            return value;
        }

        private double Progress(double now)
        {
            if (Timing.Milliseconds <= 0 || double.IsPositiveInfinity(now))
                return 1;

            return Math.Clamp((now - start) / Timing.Milliseconds, 0, 1);
        }
    }
}

/// <summary>Whether a running motion owns a window's alpha or border, so a decoration must not set them.</summary>
public readonly record struct PaintHold(bool Alpha, bool Border);
