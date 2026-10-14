namespace Winland.Core.Chords;

/// <summary>One Chord command that carries no payload.</summary>
/// <param name="Id">Its chord id in <c>bindings.json</c>.</param>
/// <param name="Summary">One sentence saying what it does. The README prints the same sentence.</param>
/// <param name="Command">What a Chord bound to <paramref name="Id"/> runs.</param>
public sealed record ChordCommandRow(string Id, string Summary, ShellCommand Command);

/// <summary>
/// The Chord commands that carry no payload, one row each.
/// A new one is a <see cref="WindowAction"/> or <see cref="HostAction"/> value, its row here, and its arm where that action runs.
/// The last row is <see cref="ShellCommand.Nothing"/>, which is neither.
/// A command with a number, a direction, or a plugin id is read by its shape in <see cref="ChordCommands"/>.
/// </summary>
public static class ChordCommandTable
{
    /// <summary>Every row, in the order the README lists them.</summary>
    public static IReadOnlyList<ChordCommandRow> Rows { get; } =
    [
        Window(WindowAction.Close, "close", "Close the focused window."),
        Window(WindowAction.ToggleFloat, "float", "Float the focused window, or tile it again."),
        Window(WindowAction.ToggleFullscreen, "fullscreen", "Fill the screen with the focused window, or tile it again."),
        Window(WindowAction.ToggleTiledFullscreen, "tiled-fullscreen", "Make the focused window fullscreen inside its tile, or take it out of fullscreen."),
        Window(WindowAction.ToggleFullWidth, "full-width", "Give the focused window the workspace's full width and height, with the bar still up, or restore it."),
        Window(WindowAction.ToggleLayout, "layout", "Toggle dwindle and scrolling on the current workspace."),
        Window(WindowAction.ToggleSplit, "split", "Flip the focused window's split between side by side and stacked."),
        Window(WindowAction.TogglePseudo, "pseudo", "Draw the focused window at its pseudotile size, or fill its tile."),
        Window(WindowAction.ToggleSticky, "sticky", "Float the focused window on every workspace, or tile it again."),
        Window(WindowAction.ToggleSpacing, "spacing", "Toggle the space between tiles."),
        Window(WindowAction.ToggleSquareTile, "square-tile", "Draw the one tiled window as a square, or fill the tile again."),
        Window(WindowAction.ToggleFullscreenDesktop, "fullscreen-desktop", "Hide the bar and turn off the space between tiles, or restore both."),
        Window(WindowAction.ToggleGroup, "group", "Arm a group, or disassemble the focused group."),
        Window(WindowAction.LeaveGroup, "leave-group", "Pull the focused window out of its group."),
        Window(WindowAction.ToggleScratchpad, "scratchpad", "Show or hide the scratchpad."),
        Window(WindowAction.SendScratchpad, "scratchpad-send", "Move the focused window into the scratchpad."),
        Window(WindowAction.SaveWidth, "save-width", "Remember the focused window's width."),
        Window(WindowAction.RestoreWidth, "restore-width", "Resize the focused window back to the width that was saved for it."),
        Window(WindowAction.NextMonitor, "next-monitor", "Give the keyboard to the workspace in front on the next monitor."),
        Window(WindowAction.PreviousMonitor, "previous-monitor", "Give the keyboard to the workspace in front on the previous monitor."),
        Host(HostAction.Terminal, "terminal", "Open a terminal."),
        Host(HostAction.Browser, "browser", "Open the browser."),
        Host(HostAction.PrivateBrowser, "private-browser", "Open a private window of the browser."),
        Host(HostAction.NextBackground, "background", "Set the wallpaper of every monitor to the Theme's next background."),
        Host(HostAction.Files, "files", "Open the file manager."),
        Host(HostAction.FilesHere, "files-here", "Open File Explorer in the directory of the terminal in front, or in your home folder."),
        Host(HostAction.Activity, "activity", "Open Activity."),
        Host(HostAction.Lock, "lock", "Lock the session."),
        Host(HostAction.ToggleBar, "bar", "Hide or show the bar."),
        Host(HostAction.ToggleIdleLock, "idle-lock", "Toggle idle lock."),
        Host(HostAction.Copy, "copy", "Copy."),
        Host(HostAction.Cut, "cut", "Cut."),
        Host(HostAction.Paste, "paste", "Paste."),
        Host(HostAction.Screenshot, "screenshot", "Take a picture of the monitor in front."),
        Host(HostAction.Capture, "capture", "Open the picker to take a picture, or dismiss it."),
        Host(HostAction.ScreenRecord, "record", "Ask which sound to record, then open the picker, or stop the recording that is running."),
        Host(HostAction.WebcamSmaller, "webcam-smaller", "Make the webcam smaller while a recording shows it."),
        Host(HostAction.WebcamLarger, "webcam-larger", "Make the webcam larger while a recording shows it."),
        Host(HostAction.ColorPick, "color", "Pick a color."),
        Host(HostAction.ExtractText, "extract", "Extract text from a region."),
        Host(HostAction.CaptureQr, "qr", "Read a QR code from a region onto the clipboard, out of clipboard history."),
        Host(HostAction.Calculator, "calculator", "Open Windows Calculator."),
        Host(HostAction.DisplayMirror, "display-mirror", "Show the same picture on every monitor, through Windows' Display Switch."),
        Host(HostAction.DisplayInternal, "display-internal", "Use the PC's own screen alone, through Windows' Display Switch."),
        Host(HostAction.DisplayExtend, "display-extend", "Spread the desktop across every monitor, through Windows' Display Switch."),
        Host(HostAction.Herdr, "herdr", "Open Herdr in Windows Terminal. On a PC without it, ask once to install it first."),
        Host(HostAction.HerdrKeys, "herdr-keys", "Open the Menu at Herdr's keybindings."),
        Host(HostAction.Transcode, "transcode", "Ask in the Menu for a capture, a format, and a size, then transcode it with ffmpeg."),
        Host(HostAction.Agent, "agent", "Start the Default agent in Windows Terminal, or pick one when none is chosen."),
        Host(HostAction.CopyPageAddress, "copy-page-address", "Copy the address of the page in front of Chrome, Edge, or Brave. Only those three take the key."),
        Host(HostAction.DownloadPageVideo, "download-page-video", "Download the video on the page in front of Chrome, Edge, or Brave into Videos. Only those three take the key."),
        new("nothing", "Do nothing, so the key does not reach Windows.", new ShellCommand.Nothing()),
    ];

    private static readonly Dictionary<string, ChordCommandRow> ById =
        Rows.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<ShellCommand, ChordCommandRow> ByCommand =
        Rows.ToDictionary(row => row.Command);

    /// <summary>The row whose chord id is <paramref name="id"/>, or null. Case is ignored.</summary>
    /// <param name="id">A chord id.</param>
    /// <returns>The row, or null when no payload-free command has that id.</returns>
    public static ChordCommandRow? Find(string id) =>
        id is not null && ById.TryGetValue(id, out var row) ? row : null;

    /// <summary>The row for <paramref name="command"/>, or null when it carries a payload.</summary>
    /// <param name="command">A command.</param>
    /// <returns>The row, or null.</returns>
    public static ChordCommandRow? Of(ShellCommand command) =>
        command is not null && ByCommand.TryGetValue(command, out var row) ? row : null;

    private static ChordCommandRow Window(WindowAction action, string id, string summary) =>
        new(id, summary, new ShellCommand.Window(action));

    private static ChordCommandRow Host(HostAction action, string id, string summary) =>
        new(id, summary, new ShellCommand.Host(action));
}
