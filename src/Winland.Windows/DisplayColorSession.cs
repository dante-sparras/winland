namespace Winland.Windows;

/// <summary>Reads a display's color and turns its HDR and wide color switches.</summary>
public interface IDisplayColorControl
{
    /// <summary>What Windows reports for <paramref name="device"/>, or false when it cannot be read.</summary>
    bool TryRead(string device, out DisplayColorReading reading);

    /// <summary>Turns the switches <paramref name="change"/> names. A null switch is left as it is.</summary>
    bool TrySet(string device, DisplayColorChange change);
}

/// <summary>HDR and wide color to turn. Null leaves that switch as it is.</summary>
public readonly record struct DisplayColorChange(bool? Hdr, bool? Wide);

/// <summary>
/// Turns the color switches an outcome's monitor rules require, and remembers the values they had before. A display
/// the rules no longer change, and every display when <see cref="Restore"/> runs, is put back. A switch Windows
/// refuses is said once, and said again only after the rules stop naming that display.
/// </summary>
public sealed class DisplayColorSession
{
    private readonly IDisplayColorControl _control;
    private readonly Action<string> _log;
    private readonly Lock _gate = new();
    private readonly List<DisplayColorOriginal> _applied = [];
    private readonly Dictionary<string, HashSet<string>> _said = new(StringComparer.OrdinalIgnoreCase);

    public DisplayColorSession(IDisplayColorControl control, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        _control = control;
        _log = log ?? Console.Error.WriteLine;
    }

    /// <summary>The color switches Winland changed, oldest first.</summary>
    public IReadOnlyList<DisplayColorOriginal> Originals
    {
        get
        {
            lock (_gate)
                return [.. _applied];
        }
    }

    public void Apply(IReadOnlyList<DisplayColorRequest> wanted)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        lock (_gate)
        {
            var asked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var request in wanted)
            {
                if (request.Device.Length > 0)
                    asked.Add(request.Device);
            }

            for (var i = _applied.Count - 1; i >= 0; i--)
            {
                if (asked.Contains(_applied[i].Device))
                    continue;

                var device = _applied[i].Device;
                Release(i, null);
                _said.Remove(device);
            }

            foreach (var request in wanted)
            {
                if (request.Device.Length == 0)
                    continue;

                if (!_control.TryRead(request.Device, out var reading))
                {
                    Say(request.Device, $"The display {request.Device} could not be read.");
                    continue;
                }

                var goal = DisplayColors.Intend(request, reading);
                if (DisplayColors.Explain(request.Device, request, goal) is string why)
                    Say(request.Device, why);

                var index = IndexOf(request.Device);
                if (goal.Hdr is null && goal.Wide is null)
                {
                    if (index >= 0)
                        Release(index, reading);

                    continue;
                }

                if (index >= 0 && BackTo(_applied[index], goal, reading))
                {
                    Release(index, reading);
                    continue;
                }

                var hdr = goal.Hdr is bool wantHdr && wantHdr != reading.HdrOn ? wantHdr : (bool?)null;
                var wide = goal.Wide is bool wantWide && reading.Split && wantWide != reading.WideOn ? wantWide : (bool?)null;
                if (hdr is null && wide is null)
                    continue;

                if (index < 0)
                    _applied.Add(new DisplayColorOriginal(request.Device, reading.HdrOn, reading.WideOn, reading.Split));

                if (!_control.TrySet(request.Device, new DisplayColorChange(hdr, wide)))
                    Say(request.Device, $"The display {request.Device} could not be switched.");
            }
        }
    }

    /// <summary>Puts back every color switch Winland changed, newest first.</summary>
    public void Restore()
    {
        lock (_gate)
        {
            for (var i = _applied.Count - 1; i >= 0; i--)
                PutBack(_applied[i]);

            _applied.Clear();
            _said.Clear();
        }
    }

    /// <summary>Puts back the color switches a run that did not stop cleanly left, newest first.</summary>
    public void RestoreSaved(IReadOnlyList<DisplayColorOriginal> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        lock (_gate)
        {
            for (var i = saved.Count - 1; i >= 0; i--)
                PutBack(saved[i]);
        }
    }

    // Putting a display back drops the record either way, so a later rule can change it again.
    private void Release(int index, DisplayColorReading? reading)
    {
        var saved = _applied[index];
        if (reading is not DisplayColorReading current || current.HdrOn != saved.Hdr || (saved.Split && current.WideOn != saved.Wide))
            PutBack(saved);

        _applied.RemoveAt(index);
    }

    private void PutBack(DisplayColorOriginal saved)
    {
        var wide = saved.Split ? saved.Wide : (bool?)null;
        if (!_control.TrySet(saved.Device, new DisplayColorChange(saved.Hdr, wide)))
            _log($"The display {saved.Device} could not be given back the color it showed.");
    }

    private static bool BackTo(DisplayColorOriginal saved, DisplayColorGoal goal, DisplayColorReading reading)
    {
        var hdr = goal.Hdr ?? reading.HdrOn;
        var wide = goal.Wide ?? reading.WideOn;
        return hdr == saved.Hdr && (!saved.Split || wide == saved.Wide);
    }

    private int IndexOf(string device)
    {
        for (var i = 0; i < _applied.Count; i++)
        {
            if (string.Equals(_applied[i].Device, device, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void Say(string device, string message)
    {
        if (!_said.TryGetValue(device, out var said))
        {
            said = [];
            _said.Add(device, said);
        }

        if (said.Add(message))
            _log(message);
    }
}
