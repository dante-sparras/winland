namespace Winland.Platform;

/// <summary>
/// A key the shell claims while it is open, as the capture picker does.
/// The hook calls <see cref="Take"/> on the hook thread. The call must only read.
/// </summary>
public interface IKeyClaim
{
    /// <summary>True while the shell is open and wants keys kept from the program in front.</summary>
    bool Open { get; set; }

    /// <summary>Decides one key event.</summary>
    /// <param name="vk">The virtual-key code.</param>
    /// <param name="down">True for key-down.</param>
    /// <param name="super">True while Super is held, not counting this key.</param>
    /// <param name="ctrl">True while Control is held.</param>
    /// <param name="alt">True while Alt is held.</param>
    /// <param name="claimed">The key code to report when this event presses one, or null when it presses nothing.</param>
    /// <returns>True when the hook keeps the event from the chords and from the program in front.</returns>
    bool Take(int vk, bool down, bool super, bool ctrl, bool alt, out int? claimed);
}

/// <summary>What the shell knows about a window that the window manager asks.</summary>
public interface IWindowProbe
{
    /// <summary>True when <paramref name="hwnd"/> belongs to an elevated process.</summary>
    /// <param name="hwnd">A top-level window.</param>
    /// <returns>True when the process is elevated.</returns>
    bool IsElevated(nint hwnd);

    /// <summary>Brings <paramref name="hwnd"/> forward from the elevated helper.</summary>
    /// <param name="hwnd">A top-level window.</param>
    void FocusFromHelper(nint hwnd);
}

/// <summary>Whether a window is still on screen, and how the shell takes it off or puts it back.</summary>
public interface IWindowHiding
{
    /// <summary>True when <paramref name="hwnd"/> is still to be hidden.</summary>
    /// <param name="hwnd">A top-level window.</param>
    /// <returns>True when the window is visible and this desktop has not hidden it.</returns>
    bool Showing(nint hwnd);

    /// <summary>Puts <paramref name="hwnd"/> back the way it was hidden.</summary>
    /// <param name="hwnd">A top-level window.</param>
    void Show(nint hwnd);

    /// <summary>Takes <paramref name="hwnd"/> off the screen.</summary>
    /// <param name="hwnd">A top-level window.</param>
    void Hide(nint hwnd);
}

/// <summary>The session lock tells the hook to drop every finger it still holds.</summary>
public interface ISessionArm
{
    /// <summary>Runs <paramref name="release"/> before the session locks.</summary>
    /// <param name="release">Drops the fingers. It must return at once.</param>
    void Arm(Action release);

    /// <summary>Stops running <paramref name="release"/> when it is the one that was armed.</summary>
    /// <param name="release">The action <see cref="Arm"/> was given.</param>
    void Disarm(Action release);
}

/// <summary>
/// The chords the elevated helper takes while an administrator window is in front.
/// The helper closes that window itself and hands every other chord to the desktop.
/// </summary>
public interface IElevatedChords
{
    /// <summary>Decides one physical key while the helper's hook is the one that saw it.</summary>
    /// <param name="key">The physical key.</param>
    /// <param name="down">True for key-down.</param>
    /// <param name="now">Tick count.</param>
    /// <param name="desktopSawThis">True when the desktop hook already published this same event.</param>
    /// <param name="own">True when an administrator window is the foreground.</param>
    /// <param name="desktopSuper">The Super the desktop hook holds, or null.</param>
    /// <returns>What the hook does with the event.</returns>
    GateEffect OnKey(PhysicalKey key, bool down, long now, bool desktopSawThis, bool own, DesktopSuper? desktopSuper = null);

    /// <summary>True when <paramref name="effect"/> closes the administrator window.</summary>
    /// <param name="effect">The effect <see cref="OnKey"/> returned.</param>
    /// <returns>True when the helper closes the foreground window.</returns>
    bool Closes(GateEffect effect);

    /// <summary>The chord to hand to the desktop, or null when this effect stays here.</summary>
    /// <param name="effect">The effect <see cref="OnKey"/> returned.</param>
    /// <returns>The chord, or null.</returns>
    QueuedChord? Forwards(GateEffect effect);

    /// <summary>True after <see cref="OnKey"/> when the desktop's Super went up where the desktop hook was not called. The next <see cref="OnKey"/> clears it.</summary>
    bool DesktopSuperWentUp { get; }
}

/// <summary>
/// Opens the elevated helper's chord file. Called on the helper's hook thread.
/// The disposable stops the watch that reloads the file.
/// </summary>
/// <returns>The chords, and the watch.</returns>
public delegate (IElevatedChords Chords, IDisposable Watch) ElevatedChordSource();

/// <summary>
/// The binds the keyboard-hook process honors. The shell builds them, because it knows the plugins and the chord file.
/// </summary>
public sealed class HookProcessBinds : IDisposable
{
    /// <summary>The ledger's table. A page action is already gated.</summary>
    public required IBindTable Ledger { get; init; }

    /// <summary>Escape and the other conditional binds. The process asks this when the desktop sends the Escape mode.</summary>
    public required BindBook Book { get; init; }

    /// <summary>Reloads the chord file. Disposing the binds stops it.</summary>
    public IDisposable? Watch { get; init; }

    /// <summary>Stops the chord-file watch.</summary>
    public void Dispose() => Watch?.Dispose();
}

/// <summary>
/// The shell's answers, set once when the process starts.
/// Until <see cref="Use"/>, a window is not elevated, no key is claimed, and a hide does nothing.
/// </summary>
public static class ShellBridge
{
    private static IWindowProbe _windows = Nothing.Windows;
    private static IWindowHiding _hiding = Nothing.Hiding;
    private static IKeyClaim _keys = Nothing.Keys;
    private static ISessionArm _session = Nothing.Session;

    /// <summary>Whether a window is elevated, and how the helper focuses one.</summary>
    public static IWindowProbe Windows => Volatile.Read(ref _windows);

    /// <summary>How the shell hides a window and puts it back.</summary>
    public static IWindowHiding Hiding => Volatile.Read(ref _hiding);

    /// <summary>The keys the shell claims, such as the capture picker's.</summary>
    public static IKeyClaim Keys => Volatile.Read(ref _keys);

    /// <summary>The session lock.</summary>
    public static ISessionArm Session => Volatile.Read(ref _session);

    /// <summary>The elevated helper's chord file. Null until <see cref="Use"/>.</summary>
    public static ElevatedChordSource? ElevatedChords { get; private set; }

    /// <summary>Installs the shell's answers. Later calls replace them.</summary>
    /// <param name="windows">Elevation and helper focus.</param>
    /// <param name="hiding">Showing, hiding, and putting a window back.</param>
    /// <param name="keys">The key claim. One instance for this process.</param>
    /// <param name="session">The session lock.</param>
    /// <param name="elevatedChords">Opens the helper's chord file. Null when this process is not the helper.</param>
    public static void Use(IWindowProbe windows, IWindowHiding hiding, IKeyClaim keys, ISessionArm session, ElevatedChordSource? elevatedChords)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(hiding);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(session);
        Volatile.Write(ref _windows, windows);
        Volatile.Write(ref _hiding, hiding);
        Volatile.Write(ref _keys, keys);
        Volatile.Write(ref _session, session);
        ElevatedChords = elevatedChords;
    }

    private static class Nothing
    {
        public static readonly IWindowProbe Windows = new NoWindows();
        public static readonly IWindowHiding Hiding = new NoHiding();
        public static readonly IKeyClaim Keys = new NoKeys();
        public static readonly ISessionArm Session = new NoSession();

        private sealed class NoWindows : IWindowProbe
        {
            public bool IsElevated(nint hwnd) => false;

            public void FocusFromHelper(nint hwnd)
            {
            }
        }

        private sealed class NoHiding : IWindowHiding
        {
            public bool Showing(nint hwnd) => false;

            public void Show(nint hwnd)
            {
            }

            public void Hide(nint hwnd)
            {
            }
        }

        private sealed class NoKeys : IKeyClaim
        {
            public bool Open { get; set; }

            public bool Take(int vk, bool down, bool super, bool ctrl, bool alt, out int? claimed)
            {
                claimed = null;
                return false;
            }
        }

        private sealed class NoSession : ISessionArm
        {
            public void Arm(Action release)
            {
            }

            public void Disarm(Action release)
            {
            }
        }
    }
}
