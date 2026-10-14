namespace Winland.Core.Shell;

/// <summary>What the Host reads of a window a fade is waiting on.</summary>
public enum FadeLook
{
    /// <summary>The window is gone.</summary>
    Gone,

    /// <summary>The window exists and is not on the glass.</summary>
    Hidden,

    /// <summary>The window is on the glass.</summary>
    Shown,

    /// <summary>The window is on the glass and takes no input: a dialog of its own is in front of it.</summary>
    Blocked,
}

/// <summary>What a fade asks of the Host besides drawing the window at an opacity.</summary>
public enum FadeEnd
{
    /// <summary>Nothing: the fade goes on.</summary>
    None,

    /// <summary>The window is solid, and is an ordinary window again.</summary>
    Solid,

    /// <summary>The window is clear, and is asked to close now.</summary>
    AskToClose,
}

/// <summary>One window's opacity for this step.</summary>
/// <param name="Id">Window identity.</param>
/// <param name="Alpha">From 0 for clear to 255 for solid.</param>
/// <param name="End">What else the Host does for it.</param>
public readonly record struct FadeFrame(long Id, byte Alpha, FadeEnd End = FadeEnd.None);

/// <summary>
/// The windows that are fading in as they open and out as they are closed, on Omarchy Quattro's times (<see cref="MotionCurve"/>).
/// A window a program made is kept clear until the program shows it, and then fades in. A window that is closed fades out first
/// and is asked to close when it is clear. No window stays clear: one that is never shown, and one that does not close, is made solid again.
/// Call from one thread at a time.
/// </summary>
public sealed class WindowFades
{
    /// <summary>How long a window that was made is kept clear while its program has not shown it, in milliseconds.</summary>
    public const long ShowWaitMilliseconds = 5000;

    /// <summary>
    /// How long a window that was asked to close stays clear, in milliseconds. One that is still open after this refused to close,
    /// or is slow to, and is solid again.
    /// </summary>
    public const long CloseWaitMilliseconds = 1500;

    private readonly Dictionary<long, Entry> _entries = [];
    private readonly List<FadeFrame> _frames = [];
    private readonly List<long> _ids = [];

    /// <summary>True while any window is clear or fading.</summary>
    public bool Any => _entries.Count > 0;

    /// <summary>True while a window is fading, so the next step is due with the next frame.</summary>
    public bool Moving
    {
        get
        {
            foreach (var entry in _entries.Values)
            {
                if (entry.Phase is Phase.Opening or Phase.Closing)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>True when <paramref name="id"/> is clear or fading.</summary>
    /// <param name="id">Window identity.</param>
    public bool Holds(long id) => _entries.ContainsKey(id);

    /// <summary>A program made <paramref name="id"/>, and the Host made it clear. It waits to be shown.</summary>
    /// <param name="id">Window identity.</param>
    /// <param name="now">Milliseconds that only go up.</param>
    public void Made(long id, long now) => _entries[id] = new Entry(Phase.Waiting, now);

    /// <summary><paramref name="id"/> is closed: it fades out from solid, and is asked to close when it is clear.</summary>
    /// <param name="id">Window identity.</param>
    /// <param name="now">Milliseconds that only go up.</param>
    public void Close(long id, long now) => _entries[id] = new Entry(Phase.Closing, now);

    /// <summary>Forgets <paramref name="id"/>. The Host calls this when it could not make the window clear, or has made it solid itself.</summary>
    /// <param name="id">Window identity.</param>
    public void Forget(long id) => _entries.Remove(id);

    /// <summary>
    /// The opacity of every fading window at <paramref name="now"/>, and what has ended. A window that waits is asked about
    /// through <paramref name="look"/>, and a fading one is not: it has an end either way.
    /// </summary>
    /// <param name="now">Milliseconds that only go up.</param>
    /// <param name="look">Reads a window that waits to be shown or to close.</param>
    /// <returns>A frame for each window whose opacity or state changed. The list is reused by the next call.</returns>
    public IReadOnlyList<FadeFrame> Step(long now, Func<long, FadeLook> look)
    {
        ArgumentNullException.ThrowIfNull(look);
        _frames.Clear();
        _ids.Clear();
        _ids.AddRange(_entries.Keys);
        foreach (var id in _ids)
        {
            var entry = _entries[id];
            var elapsed = Math.Max(0, now - entry.Since);
            switch (entry.Phase)
            {
                case Phase.Waiting:
                    switch (look(id))
                    {
                        case FadeLook.Gone:
                            _entries.Remove(id);
                            break;
                        case FadeLook.Shown or FadeLook.Blocked:
                            _entries[id] = new Entry(Phase.Opening, now);
                            break;
                        default:
                            if (elapsed >= ShowWaitMilliseconds)
                            {
                                // Its program may show it hours from now, and nobody would be here to fade it in.
                                _entries.Remove(id);
                                _frames.Add(new FadeFrame(id, 255, FadeEnd.Solid));
                            }

                            break;
                    }

                    break;
                case Phase.Opening:
                    if (elapsed >= MotionCurve.OpenFadeMilliseconds)
                    {
                        _entries.Remove(id);
                        _frames.Add(new FadeFrame(id, 255, FadeEnd.Solid));
                    }
                    else
                    {
                        _frames.Add(new FadeFrame(id, MotionCurve.Alpha(elapsed, MotionCurve.OpenFadeMilliseconds, opening: true)));
                    }

                    break;
                case Phase.Closing:
                    if (elapsed >= MotionCurve.CloseFadeMilliseconds)
                    {
                        _entries[id] = new Entry(Phase.Asked, now);
                        _frames.Add(new FadeFrame(id, 0, FadeEnd.AskToClose));
                    }
                    else
                    {
                        _frames.Add(new FadeFrame(id, MotionCurve.Alpha(elapsed, MotionCurve.CloseFadeMilliseconds, opening: false)));
                    }

                    break;
                case Phase.Asked:
                    var seen = look(id);
                    if (seen == FadeLook.Gone)
                    {
                        _entries.Remove(id);
                    }
                    else if (seen == FadeLook.Blocked || elapsed >= CloseWaitMilliseconds)
                    {
                        // It asks whether to save, or it will not close. The person has to see it to answer.
                        _entries.Remove(id);
                        _frames.Add(new FadeFrame(id, 255, FadeEnd.Solid));
                    }

                    break;
            }
        }

        return _frames;
    }

    private enum Phase
    {
        Waiting,
        Opening,
        Closing,
        Asked,
    }

    private readonly record struct Entry(Phase Phase, long Since);
}
