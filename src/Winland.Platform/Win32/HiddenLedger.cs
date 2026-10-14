
namespace Winland.Platform.Win32;

/// <summary>
/// The windows this process has hidden and how each was hidden (<see cref="WindowHide"/>, ADR 0059).
/// There is one ledger, because a window the placer hid is shown by the gateway when the desktop leaves,
/// and the window watcher asks it whether a window that took the keyboard is one Winoma hid.
/// </summary>
internal static class HiddenLedger
{
    /// <summary>Hides and shows windows, and answers how each one is hidden.</summary>
    internal static WindowHide Windows { get; } = new(new Host());

    /// <summary>
    /// True while this desktop has <paramref name="hwnd"/> off the screen: it cloaked the window and the shell's cloak is still on it,
    /// or it hid the window and the window is still not visible.
    /// </summary>
    internal static bool Hides(nint hwnd) => Windows.Way(hwnd) switch
    {
        HideWay.Cloak => Native.ShellCloaked(hwnd),
        HideWay.Hide => !Native.IsWindowVisible(hwnd),
        _ => false,
    };

    /// <summary>
    /// True when <paramref name="hwnd"/> is still to be hidden: Windows calls it visible, and this desktop has not hidden it.
    /// Windows calls a cloaked window visible too. One it cloaked itself, on another virtual desktop, is still to be hidden:
    /// it would be drawn again when that desktop comes in front.
    /// </summary>
    internal static bool Showing(nint hwnd) => Native.IsWindowVisible(hwnd) && !Hides(hwnd);

    /// <summary>
    /// Shows or hides a window. The call waits on the window's own thread, so one that has stopped answering is asked without waiting:
    /// the desktop's thread, or the desktop watcher, would stand still until that program answered.
    /// </summary>
    internal static void Show(nint hwnd, int command)
    {
        if (Native.IsHungAppWindow(hwnd))
        {
            Native.ShowWindowAsync(hwnd, command);
        }
        else
        {
            Native.ShowWindow(hwnd, command);
        }
    }

    private sealed class Host : IHideHost
    {
        // The ledger holds the way before it asks for it (WindowHide). The record says that way before the window is hidden so:
        // it names the cloak for every window at first, and SW_HIDE once the cloak was not used on one.
        public bool Record(long id, HideWay way) => HiddenStore.Record(id, way);

        public bool UnderShellCloak(long id) => Native.IsWindow((nint)id) && Native.ShellCloaked((nint)id);

        public bool OnAnotherDesktop(long id) => WindowsDesktops.KeepsElsewhere((nint)id);

        public CloakRefusal Cloak(long id) => ShellCloak.Set((nint)id, cloak: true);

        // A window that closed is under no cloak, and Explorer has no view of it to say so.
        public CloakRefusal Uncloak(long id) =>
            Native.IsWindow((nint)id) ? ShellCloak.Set((nint)id, cloak: false) : CloakRefusal.None;

        public bool Hide(long id)
        {
            var hwnd = (nint)id;
            var transitions = 1;
            _ = Native.DwmSetWindowAttribute(hwnd, Native.DwmwaTransitionsForcedDisabled, ref transitions, sizeof(int));

            // A window that has stopped answering is asked without a wait, and hides when it wakes. Any other has hidden by now,
            // or Windows refused. It refuses for a window of a program that runs as administrator, which Explorer cloaks all the same,
            // so one comes here only when the cloak was not used on it.
            var asked = Native.IsHungAppWindow(hwnd);
            Show(hwnd, Native.SwHide);
            return asked || !Native.IsWindowVisible(hwnd);
        }

        public void Unhide(long id) => Show((nint)id, Native.SwShowNa);
    }
}
