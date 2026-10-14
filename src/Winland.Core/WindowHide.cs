namespace Winland.Core;

/// <summary>How Winoma took a window off the screen.</summary>
public enum HideWay
{
    /// <summary>Explorer's application-view cloak. Windows still calls the window visible, and the window keeps the keyboard it had.</summary>
    Cloak,

    /// <summary><c>ShowWindow</c> with <c>SW_HIDE</c>. The only fallback, for a window the cloak is not used on.</summary>
    Hide,
}

/// <summary>
/// Why the cloak was not used on a window. Every reason but <see cref="None"/> hides the window with <see cref="HideWay.Hide"/>.
/// A Host test that finds another window the cloak must not be used on adds its reason here, and the Host answers it from <see cref="IHideHost.Cloak"/>.
/// </summary>
public enum CloakRefusal
{
    /// <summary>The cloak was used.</summary>
    None,

    /// <summary>Explorer has no view of the window, as for a tool window or one that opened a moment ago.</summary>
    NoView,

    /// <summary>Explorer has a view of the window and answered the cloak with an error.</summary>
    Refused,

    /// <summary>Explorer could not be reached, as while it restarts.</summary>
    Unreachable,
}

/// <summary>The Host's ways to take a window off the screen and put it back, as <see cref="WindowHide"/> asks for them.</summary>
public interface IHideHost
{
    /// <summary>
    /// Makes the record of hidden windows (<see cref="HiddenWindows"/>) name <paramref name="id"/> with <paramref name="way"/>, before the window is hidden so.
    /// A desktop killed after the hide leaves the window to whoever reads the record, and a rescue undoes only the way the record names.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <param name="way">The way the window is about to be hidden.</param>
    /// <returns>False when the record does not name the window with that way: it could not be written. The window is then not hidden.</returns>
    bool Record(long id, HideWay way);

    /// <summary>True while the shell's cloak is on <paramref name="id"/>, whoever put it there.</summary>
    /// <param name="id">Window identity.</param>
    bool UnderShellCloak(long id);

    /// <summary>
    /// True when Windows keeps <paramref name="id"/> on a virtual desktop of its own that is not in front. Such a window reads the shell's cloak too.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <returns>False for a window on the Windows desktop in front, and when Windows cannot say.</returns>
    bool OnAnotherDesktop(long id);

    /// <summary>Asks Explorer to cloak <paramref name="id"/>.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns><see cref="CloakRefusal.None"/> when the window is cloaked. Any other answer leaves it as it was.</returns>
    CloakRefusal Cloak(long id);

    /// <summary>Asks Explorer to take Winoma's cloak off <paramref name="id"/>.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns><see cref="CloakRefusal.None"/> when Winoma's cloak is off the window. Any other answer leaves it cloaked.</returns>
    CloakRefusal Uncloak(long id);

    /// <summary>Hides <paramref name="id"/> with <c>SW_HIDE</c>.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>
    /// False when the window is still on the screen: Windows refused, as it does for an administrator window asked by a desktop that is not one.
    /// Explorer cloaks such a window, so this is asked of one only when the cloak was not used on it.
    /// True for a window that has stopped answering and was asked without a wait: it hides when it wakes.
    /// </returns>
    bool Hide(long id);

    /// <summary>Shows a window hidden with <see cref="Hide"/>, without giving it the keyboard.</summary>
    /// <param name="id">Window identity.</param>
    void Unhide(long id);
}

/// <summary>
/// Decides how a window is hidden, and keeps how each hidden window was (ADR 0059).
/// The cloak comes first. A window the cloak is not used on, for any <see cref="CloakRefusal"/>, is hidden with <c>SW_HIDE</c>.
/// A window is put back the way it was taken off. A window neither way takes off the screen is not on the ledger,
/// and neither is one whose record could not be written, which is not hidden at all:
/// the caller is told, and keeps it with the Workspace in front (ADR 0003).
/// A cloaked window always has a way back: one under Winoma's kind of cloak that is not on the ledger is uncloaked when it is shown (<see cref="Cloaked"/>).
/// Safe from any thread: the Host is asked outside the lock.
/// </summary>
public sealed class WindowHide
{
    private readonly IHideHost _host;
    private readonly object _gate = new();
    private readonly Dictionary<long, HideWay> _ways = [];

    /// <summary>Hides through <paramref name="host"/>.</summary>
    /// <param name="host">The Host's ways to hide a window.</param>
    public WindowHide(IHideHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>
    /// Takes <paramref name="id"/> off the screen. The way is on the ledger before the Host is asked for it,
    /// so whoever writes the ledger down (<see cref="HiddenWindows"/>) is never short of the glass,
    /// and the record names the way before the window is hidden so (<see cref="IHideHost.Record"/>).
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <returns>
    /// How the window was hidden. Null when it is still on the screen, and not on the ledger: neither way took it off,
    /// or the record could not be written.
    /// </returns>
    public HideWay? Hide(long id)
    {
        Note(id, HideWay.Cloak);
        if (!_host.Record(id, HideWay.Cloak))
        {
            return Unrecorded(id);
        }

        var refusal = _host.Cloak(id);
        if (refusal == CloakRefusal.None)
        {
            return HideWay.Cloak;
        }

        Log.Information("Shell", "window " + id + " is hidden without the cloak: " + refusal);
        Note(id, HideWay.Hide);
        if (!_host.Record(id, HideWay.Hide))
        {
            return Unrecorded(id);
        }

        if (_host.Hide(id))
        {
            return HideWay.Hide;
        }

        Log.Warning("Shell", "window " + id + " could not be hidden");
        Forget(id);
        return null;
    }

    private HideWay? Unrecorded(long id)
    {
        Log.Warning("Shell", "window " + id + " is not hidden: the record of hidden windows could not be written");
        Forget(id);
        return null;
    }

    /// <summary>
    /// True when Winoma's kind of cloak is on <paramref name="id"/>: the shell's cloak, on a window Windows does not keep on a virtual desktop
    /// of its own (ADR 0003). This desktop put it there, or one before it did and left no record.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <returns>False for a window on the glass, and for one that Windows cloaked.</returns>
    public bool Cloaked(long id) => _host.UnderShellCloak(id) && !_host.OnAnotherDesktop(id);

    /// <summary>
    /// Takes over <paramref name="id"/> as a window that is already off the screen: the desktop before this one hid it,
    /// and was replaced (ADR 0059). The Host is not asked. <see cref="Show"/> puts it back the way the record says it was hidden.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <param name="way">How the record says the window was hidden.</param>
    public void Adopt(long id, HideWay way) => Note(id, way);

    /// <summary>
    /// Puts <paramref name="id"/> back on the screen, the way it was taken off. A window that is not on the ledger and is under
    /// Winoma's kind of cloak (<see cref="Cloaked"/>) is uncloaked too. Any other window that is not on the ledger is left alone.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <returns>
    /// True when Winoma no longer hides the window. False when Explorer did not take the cloak off:
    /// a window on the ledger stays there, and the next call asks again.
    /// </returns>
    public bool Show(long id)
    {
        switch (Way(id))
        {
            case HideWay.Cloak:
                var refusal = _host.Uncloak(id);
                if (refusal != CloakRefusal.None)
                {
                    Log.Warning("Shell", "window " + id + " is still cloaked: " + refusal);
                    return false;
                }

                break;
            case HideWay.Hide:
                _host.Unhide(id);
                break;
            default:
                if (!Cloaked(id))
                {
                    return true;
                }

                var answer = _host.Uncloak(id);
                Log.Information("Shell", "window " + id + " was cloaked and on no ledger; Explorer was asked to uncloak it: " + answer);
                return answer == CloakRefusal.None;
        }

        Forget(id);
        return true;
    }

    /// <summary>How <paramref name="id"/> is hidden.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>Null for a window this did not hide.</returns>
    public HideWay? Way(long id)
    {
        lock (_gate)
        {
            return _ways.TryGetValue(id, out var way) ? way : null;
        }
    }

    /// <summary>Drops <paramref name="id"/> without showing it: its window closed, and Windows hands the handle to a later one.</summary>
    /// <param name="id">Window identity.</param>
    public void Forget(long id)
    {
        lock (_gate)
        {
            _ways.Remove(id);
        }
    }

    private void Note(long id, HideWay way)
    {
        lock (_gate)
        {
            _ways[id] = way;
        }
    }
}
