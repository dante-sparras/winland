namespace Winland.Core.Chords;

/// <summary>What Winoma does when a chord matches.</summary>
public abstract record ShellCommand
{
    /// <summary>Jump to a workspace, creating it when needed.</summary>
    public sealed record JumpWorkspace(int Number) : ShellCommand;

    /// <summary>Send the focused window to a workspace.</summary>
    public sealed record MoveWindow(int Number, bool Follow) : ShellCommand;

    /// <summary>Next, previous, or former workspace.</summary>
    public sealed record CycleWorkspace(WorkspaceCycle Cycle) : ShellCommand;

    /// <summary>Move focus.</summary>
    public sealed record Focus(Direction Direction) : ShellCommand;

    /// <summary>Swap with the neighbor.</summary>
    public sealed record Swap(Direction Direction) : ShellCommand;

    /// <summary>Resize the focused window by a pixel delta. Negative X expands the left edge. Negative Y shrinks from the top.</summary>
    public sealed record Resize(int X, int Y) : ShellCommand;

    /// <summary>Cycle the focused group by <paramref name="Delta"/> members.</summary>
    public sealed record GroupStep(int Delta) : ShellCommand;

    /// <summary>Show the 1-based member of the focused group.</summary>
    public sealed record GroupIndex(int Index) : ShellCommand;

    /// <summary>Pull the focused window into the group in this direction.</summary>
    public sealed record JoinGroup(Direction Direction) : ShellCommand;

    /// <summary>Move the current workspace onto the monitor in this direction.</summary>
    public sealed record MoveMonitor(Direction Direction) : ShellCommand;

    /// <summary>A command with no payload that Winoma runs on the windows of the current workspace. <see cref="ChordCommandTable"/> names it.</summary>
    public sealed record Window(WindowAction Action) : ShellCommand;

    /// <summary>A command with no payload that the Host runs: launch, lock, the bar, clipboard, and capture. <see cref="ChordCommandTable"/> names it.</summary>
    public sealed record Host(HostAction Action) : ShellCommand;

    /// <summary>An App chord: start the program the Host names for a role. A role with no program starts nothing. Stored as <c>app.&lt;role&gt;</c>.</summary>
    /// <param name="Role">The role's name in <see cref="AppRoles"/>, such as <c>music</c>.</param>
    public sealed record AppRole(string Role) : ShellCommand;

    /// <summary>Open the panel for a plugin, or close it when that panel is already open.</summary>
    /// <param name="Id">Plugin id.</param>
    public sealed record OpenPanel(string Id) : ShellCommand;

    /// <summary>Open the numbered panel widget on the right of the bar. The tray is not counted.</summary>
    /// <param name="Number">1-based index among the visible panel widgets on the right.</param>
    public sealed record OpenRightPanel(int Number) : ShellCommand;

    /// <summary>Do nothing. A Chord bound to it keeps its key from Windows, which would act on it otherwise.</summary>
    public sealed record Nothing : ShellCommand;

    /// <summary>A command a plugin declares in its manifest. Stored as <c>plugin:&lt;id&gt;:&lt;name&gt;</c>.</summary>
    /// <param name="Plugin">Plugin id.</param>
    /// <param name="Name">Command name.</param>
    /// <param name="Phase">A one-shot, or the edge of a held chord.</param>
    public sealed record PluginCommand(string Plugin, string Name, BindPhase Phase) : ShellCommand;
}

/// <summary>Commands with no payload that the Host runs. Each has an arm where the desktop implements <c>IShellActions</c>.</summary>
public enum HostAction
{
    /// <summary>Open a terminal.</summary>
    Terminal,

    /// <summary>Open the browser.</summary>
    Browser,

    /// <summary>Open a private window of the browser.</summary>
    PrivateBrowser,

    /// <summary>Set every monitor's wallpaper to the Theme's next background.</summary>
    NextBackground,

    /// <summary>Open the file manager.</summary>
    Files,

    /// <summary>Open File Explorer in the directory of the terminal in front, or in the home folder.</summary>
    FilesHere,

    /// <summary>Open Activity.</summary>
    Activity,

    /// <summary>Lock the session.</summary>
    Lock,

    /// <summary>Hide or show the bar.</summary>
    ToggleBar,

    /// <summary>Toggle idle lock.</summary>
    ToggleIdleLock,

    /// <summary>Copy.</summary>
    Copy,

    /// <summary>Cut.</summary>
    Cut,

    /// <summary>Paste.</summary>
    Paste,

    /// <summary>Take a picture of the monitor in front, to the clipboard and to a file under Pictures\Screenshots.</summary>
    Screenshot,

    /// <summary>Open Winoma's picker over a picture of the monitor in front, or dismiss the one that is open. Print Screen.</summary>
    Capture,

    /// <summary>Ask which sound to record, then open the picker, or stop the recording that is running (ADR 0084).</summary>
    ScreenRecord,

    /// <summary>Make the webcam one size smaller while a recording shows it. At the smallest, or with none, nothing is asked.</summary>
    WebcamSmaller,

    /// <summary>Make the webcam one size larger while a recording shows it. At the largest, or with none, nothing is asked.</summary>
    WebcamLarger,

    /// <summary>Pick a color.</summary>
    ColorPick,

    /// <summary>Extract text from a region.</summary>
    ExtractText,

    /// <summary>Read a QR code from a region and put its value on the clipboard, out of clipboard history.</summary>
    CaptureQr,

    /// <summary>Open Windows Calculator.</summary>
    Calculator,

    /// <summary>Ask Windows' Display Switch to show the same picture on every monitor.</summary>
    DisplayMirror,

    /// <summary>Ask Windows' Display Switch to use the PC's own screen alone.</summary>
    DisplayInternal,

    /// <summary>Ask Windows' Display Switch to spread the desktop across every monitor.</summary>
    DisplayExtend,

    /// <summary>Open Herdr in Windows Terminal, or ask once to install it first.</summary>
    Herdr,

    /// <summary>Open the Menu at Herdr's keybindings.</summary>
    HerdrKeys,

    /// <summary>Open the Menu to transcode a capture: a file, then a format, then a size.</summary>
    Transcode,

    /// <summary>Start the Default agent unattended, or open the list of agents when none is chosen (ADR 0090).</summary>
    Agent,
    /// <summary>Copy the address of the page in front of Chrome, Edge, or Brave. Winoma's only while one of them has the keyboard.</summary>
    CopyPageAddress,

    /// <summary>Download the video on the page in front of Chrome, Edge, or Brave into Videos. Winoma's only while one of them has the keyboard.</summary>
    DownloadPageVideo,
}
