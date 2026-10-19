namespace Winland.Windows;

/// <summary>Makes one display show another's picture, and puts it back.</summary>
public interface IDisplayTopology
{
    /// <summary>
    /// Makes <see cref="DisplayMirrorRequest.Device"/> show the picture of <see cref="DisplayMirrorRequest.Source"/>.
    /// Returns what it showed before, or null with why Windows cannot, when it leaves the displays alone.
    /// </summary>
    DisplayMirrorOriginal? Mirror(DisplayMirrorRequest request, out string failure);

    /// <summary>Gives the display back the picture it showed. True when it shows it, or is gone.</summary>
    bool Unmirror(DisplayMirrorOriginal original);
}

/// <summary>
/// Mirrors the displays an outcome lists and remembers what each showed before. A mirror the outcome no longer lists,
/// and every mirror when <see cref="Restore"/> runs, is put back. A mirror Windows cannot show is said once, and tried
/// again only after the rules stop asking for it.
/// </summary>
public sealed class DisplayMirrorSession
{
    private readonly IDisplayTopology _topology;
    private readonly Action<string> _log;
    private readonly Lock _gate = new();
    private readonly List<DisplayMirrorOriginal> _applied = [];
    private readonly HashSet<DisplayMirrorRequest> _failed = [];

    public DisplayMirrorSession(IDisplayTopology topology, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(topology);
        _topology = topology;
        _log = log ?? Console.Error.WriteLine;
    }

    /// <summary>The mirrors Winland made, oldest first.</summary>
    public IReadOnlyList<DisplayMirrorOriginal> Originals
    {
        get
        {
            lock (_gate)
                return [.. _applied];
        }
    }

    public void Apply(IReadOnlyList<DisplayMirrorRequest> wanted)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        lock (_gate)
        {
            for (var i = _applied.Count - 1; i >= 0; i--)
            {
                var mirror = _applied[i];
                if (Contains(wanted, new DisplayMirrorRequest(mirror.Device, mirror.Source)))
                    continue;

                PutBack(mirror);
                _applied.RemoveAt(i);
            }

            _failed.RemoveWhere(request => !Contains(wanted, request));
            foreach (var request in wanted)
            {
                if (_failed.Contains(request) || Applied(request.Device))
                    continue;

                if (_topology.Mirror(request, out var failure) is DisplayMirrorOriginal original)
                {
                    _applied.Add(original);
                    continue;
                }

                _failed.Add(request);
                _log($"The display {request.Device} cannot mirror {request.Source}: {failure}");
            }
        }
    }

    /// <summary>Puts back every mirror Winland made, newest first.</summary>
    public void Restore()
    {
        lock (_gate)
        {
            for (var i = _applied.Count - 1; i >= 0; i--)
                PutBack(_applied[i]);

            _applied.Clear();
            _failed.Clear();
        }
    }

    /// <summary>Puts back the mirrors a run that did not stop cleanly left, newest first.</summary>
    public void RestoreSaved(IReadOnlyList<DisplayMirrorOriginal> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        lock (_gate)
        {
            for (var i = saved.Count - 1; i >= 0; i--)
                PutBack(saved[i]);
        }
    }

    private void PutBack(DisplayMirrorOriginal mirror)
    {
        if (!_topology.Unmirror(mirror))
            _log($"The display {mirror.Device} could not be given back its own picture.");
    }

    private bool Applied(string device)
    {
        foreach (var mirror in _applied)
        {
            if (string.Equals(mirror.Device, device, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool Contains(IReadOnlyList<DisplayMirrorRequest> wanted, DisplayMirrorRequest request)
    {
        foreach (var candidate in wanted)
        {
            if (string.Equals(candidate.Device, request.Device, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Source, request.Source, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
