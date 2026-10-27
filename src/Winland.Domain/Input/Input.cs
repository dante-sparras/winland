namespace Winland.Domain;

public abstract record Input;

public abstract record Event : Input;

public abstract record Command : Input;

public sealed record KeyPressed(string Key) : Event
{
    public int ScanCode { get; init; }
}

public sealed record KeyReleased(string Key) : Event
{
    public int ScanCode { get; init; }
}

public sealed record PointerMoved(Point Position) : Event
{
    public long? TimeMs { get; init; }

    /// <summary>
    /// The top-level window of another program actually under the pointer, when it was read. A menu, drop-down list
    /// or other popup over a tile is under the pointer in place of the tile.
    /// </summary>
    public WindowId? Under { get; init; }
}

public sealed record PointerButtonPressed(string Button) : Event
{
    public Point Position { get; init; }
}

public sealed record PointerButtonReleased(string Button) : Event
{
    public Point Position { get; init; }
}

/// <summary>One notch of the wheel. <paramref name="Key"/> is mouse_up, mouse_down, mouse_left, or mouse_right.</summary>
public sealed record WheelTurned(string Key, long TimeMs) : Event;

/// <summary>Fingers came down on a touchpad together. Movement follows as <see cref="SwipeUpdated"/>.</summary>
public sealed record SwipeBegan(int Fingers) : Event;

/// <summary>
/// The fingers of a touchpad swipe moved. The delta is in pixels since the last update, right and down positive.
/// </summary>
public sealed record SwipeUpdated(int Fingers, double DeltaX, double DeltaY) : Event
{
    /// <summary>When the update happened, in milliseconds of a steady clock, or 0 when it is not known.</summary>
    public long TimeMs { get; init; }

    /// <summary>How many of the touchpad's reports this update adds up, when several waited together.</summary>
    public int Steps { get; init; } = 1;
}

/// <summary>The swipe ended. A cancelled swipe ended because the finger count changed, not because the fingers lifted.</summary>
public sealed record SwipeEnded(bool Cancelled = false) : Event
{
    /// <summary>When the swipe ended, in milliseconds of a steady clock, or 0 when it is not known.</summary>
    public long TimeMs { get; init; }
}

public sealed record WindowClosed(WindowId Window) : Event;

public sealed record WindowFocused(WindowId Window, FocusCause Cause = FocusCause.User) : Event
{
    /// <summary>The top-level window the click before a <see cref="FocusCause.User"/> change landed on, if it was a click.</summary>
    public WindowId? Clicked { get; init; }
}

public enum FocusCause
{
    /// <summary>Winland moved the foreground itself.</summary>
    Requested,

    /// <summary>A click or a key preceded the change.</summary>
    User,

    /// <summary>The application took the foreground without a recent click or key.</summary>
    Activated,
}

public sealed record WindowMinimized(WindowId Window) : Event;

public sealed record WindowRestored(WindowId Window) : Event;

/// <summary>A Client moved or resized itself. <see cref="Bounds"/> is its visible frame.</summary>
public sealed record WindowResized(WindowId Window, Rectangle Bounds) : Event;

/// <summary>
/// A Client's frame is not where its Layout put it, soon after the core changed it. Electron windows restore their
/// own bounds just after they leave the system's maximized state. <see cref="Bounds"/> is the visible frame.
/// </summary>
public sealed record WindowDrifted(WindowId Window, Rectangle Bounds) : Event;

/// <summary>The system began moving a window by its title bar. <see cref="Pointer"/> is where the pointer is.</summary>
public sealed record WindowMoveStarted(WindowId Window, Point Pointer) : Event;

/// <summary>
/// The system finished moving a window by its title bar. <see cref="Bounds"/> is the visible frame the window was
/// dropped at, which is where its motion into the Layout starts. Without it the motion starts at the lifted tile.
/// </summary>
public sealed record WindowMoveEnded(WindowId Window, Point Pointer, Rectangle? Bounds = null) : Event;

/// <summary>
/// The system began resizing a window by its border. <see cref="Pointer"/> is where the pointer is, and
/// <see cref="Bounds"/> the visible frame then.
/// </summary>
public sealed record WindowSizeStarted(WindowId Window, Point Pointer, Rectangle? Bounds = null) : Event;

/// <summary>
/// The border of a window the system is resizing moved. <see cref="Pointer"/> is where the pointer is, and
/// <see cref="Bounds"/> the visible frame now.
/// </summary>
public sealed record WindowSizing(WindowId Window, Point Pointer, Rectangle? Bounds = null) : Event;

/// <summary>The system finished resizing a window by its border. <see cref="Bounds"/> is the visible frame it was left at.</summary>
public sealed record WindowSizeEnded(WindowId Window, Point Pointer, Rectangle? Bounds = null) : Event;

/// <summary>An application asked to be maximized, or to leave it. That is maximize mode 1, toggled.</summary>
public sealed record WindowMaximizeRequested(WindowId Window) : Event;

/// <summary>
/// A window came to cover its monitor by itself, as a browser does for a video, or stopped covering it. That is
/// fullscreen mode 2.
/// </summary>
public sealed record WindowFullscreenRequested(WindowId Window, bool Fullscreen) : Event;

/// <summary>A window's title changed.</summary>
public sealed record WindowTitleChanged(WindowId Window, string Title) : Event;

/// <summary>A window's class changed.</summary>
public sealed record WindowClassChanged(WindowId Window, string Class) : Event;

/// <summary>Windows disabled a Client, as it does while a modal dialog it owns is open, or enabled it again.</summary>
public sealed record WindowEnabledChanged(WindowId Window, bool Enabled) : Event;

/// <summary>The window flashed for attention without taking the foreground.</summary>
public sealed record WindowFlashed(WindowId Window) : Event;

/// <summary>The window's minimum or maximum track size changed. A null size is no limit.</summary>
public sealed record WindowLimitsChanged(WindowId Window, Size? MinSize, Size? MaxSize) : Event;

/// <summary>
/// The full set of attached monitors, in adapter order. A monitor absent from the next event was unplugged.
/// Each monitor's rectangle and <see cref="Monitor.WorkArea"/> are physical pixels, and <see cref="Monitor.Scale"/>
/// is its DPI divided by 96.
/// </summary>
public sealed record MonitorsChanged(IReadOnlyList<Monitor> Monitors) : Event;

/// <summary>The Windows accent. The active border follows it unless the config set <c>general:col.active_border</c>.</summary>
public sealed record AccentChanged(BorderColor Color) : Event;

/// <summary>The Clients a restart found in the state file have been adopted again, so their tiles can be arranged as they were.</summary>
public sealed record RestoredAdopted : Event;

public sealed record ConfigLoaded(ConfigDocument Config) : Command;

public sealed record Configure(ConfigDocument Document) : Command;

public sealed record Dispatch(string Name, string Arguments = "") : Command
{
    /// <summary>The key edge that ran this dispatcher from a bind. Null for a pipe command.</summary>
    public BindTrigger? Trigger { get; init; }
}

public sealed record Preselect(string Direction) : Command;

public sealed record Pseudo(WindowId Window) : Command;

/// <summary>The wheel turned over the tab bar of the Group <paramref name="Shown"/> shows, towards its next member or back.</summary>
public sealed record GroupBarScrolled(WindowId Shown, bool Forward) : Command;

/// <summary>A middle click on the tab of <paramref name="Window"/> asked to close it.</summary>
public sealed record GroupTabClosed(WindowId Window) : Command;

/// <summary>
/// A mouse bind already matched on the hook thread. The worker applies the drag under the core lock.
/// </summary>
public sealed record PointerGesture(string Dispatcher, Point Position, PointerDragPhase Phase, string Button) : Command
{
    /// <summary>The bind's argument, such as the aspect ratio mode of <c>resizewindow</c>.</summary>
    public string Arguments { get; init; } = "";
}

public sealed record WindowAppeared(WindowId Window, Rectangle WorkArea) : Event
{
    public Point? Pointer { get; init; }

    public WindowTraits Traits { get; init; } = WindowTraits.Normal;

    public Rectangle Bounds { get; init; }

    public long ProcessId { get; init; }

    public IReadOnlyList<int> AncestorProcessIds { get; init; } = [];

    /// <summary>The window that owns this one, as a dialog's owner, or null.</summary>
    public WindowId? Owner { get; init; }

    /// <summary>
    /// The window opened while Winland runs, so it opens on the focused Monitor, or on its owner's. False for a window
    /// that was already open when Winland started, which stays on the Monitor it is on.
    /// </summary>
    public bool Opened { get; init; }

    /// <summary>Windows has disabled the window, as it does the owner of an open modal dialog.</summary>
    public bool Disabled { get; init; }

    /// <summary>
    /// The window was maximized when it first showed, as a client that asks to be maximized before it maps. The core
    /// ignores the maximize request that follows.
    /// </summary>
    public bool ShownMaximized { get; init; }

    /// <summary>The full path of the window's program, or empty when it could not be read. The traits' exe is only the file name.</summary>
    public string Executable { get; init; } = "";
}
