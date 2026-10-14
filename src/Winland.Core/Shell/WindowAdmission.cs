
namespace Winland.Core.Shell;

/// <summary>One top-level window, as the words the host read.</summary>
/// <param name="Id">Window identity.</param>
/// <param name="Style">Raw style word.</param>
/// <param name="ExtendedStyle">Raw extended style word.</param>
/// <param name="Visible">True when the window is visible.</param>
/// <param name="Cloak">Cloak flags. Zero when the window is not cloaked.</param>
/// <param name="Owned">True when another window owns this one.</param>
/// <param name="ClassName">Window class name.</param>
/// <param name="Title">Window title.</param>
/// <param name="OwnProcess">True when this process owns the window.</param>
/// <param name="Untiled">
/// True when Winoma may not tile the window: an administrator window on a Host that does not tile them (ADR 0069).
/// Winoma cannot move it. Explorer cloaks it all the same, so it stays Free and is hidden and shown with its Workspace.
/// </param>
/// <param name="ShellWindow">True when the window carries Winshell's mark. Winland does not tile it and does not hide it.</param>
public readonly record struct WindowReading(
    long Id,
    long Style,
    long ExtendedStyle,
    bool Visible,
    int Cloak,
    bool Owned,
    string ClassName,
    string Title,
    bool OwnProcess,
    bool Untiled = false,
    bool ShellWindow = false);

/// <summary>
/// The lists produced from one set of readings. Each holds windows that are on the glass. A window under a cloak is on none:
/// Winoma hid it and keeps it itself (<see cref="ShellController"/>), or Windows keeps it on another virtual desktop, and then it is not Winoma's (ADR 0003).
/// </summary>
/// <param name="Tiles">Windows on the glass that take a tile.</param>
/// <param name="PictureInPicture">Picture-in-picture players on the glass.</param>
/// <param name="Free">Dialogs, owned windows, and fixed popups on the glass.</param>
/// <param name="FreeAlone">
/// The <paramref name="Free"/> windows that no window owns. Winoma hides one with the Workspace it is on.
/// An owned window is left to its owner: Windows cloaks it when its owner is cloaked.
/// </param>
public readonly record struct AdmittedWindows(
    IReadOnlyList<TrackedWindow> Tiles,
    IReadOnlyList<long> PictureInPicture,
    IReadOnlyList<long> Free,
    IReadOnlyList<long> FreeAlone);

/// <summary>
/// Sorts top-level windows into tiles, free windows, and picture-in-picture players.
/// A picture-in-picture player wins over a window that also looks free.
/// </summary>
public static class WindowAdmission
{
    /// <summary>The owner hid the window. A cloaked window whose handle is still alive is closed.</summary>
    public const int CloakedByApp = 1;

    /// <summary>The shell's cloak. Winoma puts it on a window it hides (<see cref="HideWay.Cloak"/>), and Windows on a window of another virtual desktop.</summary>
    public const int CloakedByShell = 2;

    private const long Caption = 0x00C00000;
    private const long Popup = 0x80000000;
    private const long ThickFrame = 0x00040000;
    private const long ToolWindow = 0x00000080;
    private const long DialogFrame = 0x00000001;
    private const long Topmost = 0x00000008;
    private const long NoActivate = 0x08000000;
    private const string DialogClass = "#32770";
    private const string HostedFrame = "ApplicationFrameWindow";

    /// <summary>Sorts <paramref name="windows"/> into the lists.</summary>
    /// <param name="windows">Readings in enumeration order.</param>
    /// <returns>Tiles, picture-in-picture, and free windows.</returns>
    public static AdmittedWindows Admit(IReadOnlyList<WindowReading> windows)
    {
        var tiles = new List<TrackedWindow>();
        var pictureInPicture = new List<long>();
        var free = new List<long>();
        var alone = new List<long>();
        foreach (var window in windows)
        {
            if (!MayCount(window.Visible, window.Cloak))
            {
                continue;
            }

            switch (Classify(window))
            {
                case Kind.Tile:
                    tiles.Add(new TrackedWindow(window.Id, window.Title));
                    break;
                case Kind.PictureInPicture:
                    pictureInPicture.Add(window.Id);
                    break;
                case Kind.Free:
                    free.Add(window.Id);
                    if (!window.Owned)
                    {
                        alone.Add(window.Id);
                    }

                    break;
            }
        }

        return new AdmittedWindows(tiles, pictureInPicture, free, alone);
    }

    private static Kind Classify(WindowReading window)
    {
        var style = window.Style;
        var extended = window.ExtendedStyle;
        if (window.OwnProcess
            || window.ShellWindow
            || (extended & ToolWindow) != 0
            || (extended & NoActivate) != 0
            || (style & Caption) == 0
            || IsShell(window.ClassName)
            || IsRecordingToolbar(window.ClassName, (extended & Topmost) != 0))
        {
            return Kind.Absent;
        }

        if (IsPictureInPicture(window.Title, (extended & Topmost) != 0))
        {
            return Kind.PictureInPicture;
        }

        if (window.Owned || (extended & DialogFrame) != 0 || window.ClassName == DialogClass)
        {
            return Kind.Free;
        }

        if ((style & Popup) != 0 && (style & ThickFrame) == 0 && window.ClassName != HostedFrame)
        {
            return Kind.Free;
        }

        return window.Untiled ? Kind.Free : Kind.Tile;
    }

    /// <summary>
    /// True when a window with this visibility and cloak can land in any list. A census reads the rest of a window,
    /// its styles, class, and title, only when this is true, since most top-level windows are hidden.
    /// </summary>
    /// <param name="visible">True when the window is visible. Windows calls a cloaked window visible too.</param>
    /// <param name="cloak">
    /// Cloak flags. Zero when the window is not cloaked. A window under any cloak is off the glass and lands in no list, as a window
    /// hidden with <c>SW_HIDE</c> does. The shell's cloak is Winoma's on a window of a Workspace that is not in front, on a Scratchpad window,
    /// and on a Group member behind the one shown: the shell keeps those itself. The same value is on a window Windows keeps on another
    /// virtual desktop, and that window is not Winoma's until it is on the glass. A window whose owner is cloaked reads a cloak of its own,
    /// inherited from the owner.
    /// </param>
    /// <returns>False when <see cref="Admit"/> would leave the window out whatever else it reads.</returns>
    public static bool MayCount(bool visible, int cloak) => visible && cloak == 0;

    /// <summary>
    /// True when a window with this visibility and cloak is an open window of its program, on the glass or not. A window Winoma hid
    /// with the shell's cloak is still one: its Workspace is only not in front. Activity asks, to tell an app from a background process.
    /// </summary>
    /// <param name="visible">True when the window is visible. Windows calls a cloaked window visible too.</param>
    /// <param name="cloak">Cloak flags. Zero when the window is not cloaked.</param>
    /// <returns>False for a window its program hid, and for one its app cloaked: that one is closed.</returns>
    public static bool IsOpen(bool visible, int cloak) => visible && (cloak & CloakedByApp) == 0;

    /// <summary>
    /// True when a window with this extended style can land in any list. A tool window and a window that never takes the keyboard,
    /// such as a tooltip, a menu, or a popup, cannot, so one that is shown or hidden changes nothing that was admitted.
    /// </summary>
    /// <param name="extendedStyle">Raw extended style word.</param>
    /// <returns>False when <see cref="Admit"/> would leave the window out whatever else it reads.</returns>
    public static bool MayAdmit(long extendedStyle) => (extendedStyle & (ToolWindow | NoActivate)) == 0;

    private static bool IsShell(string className) =>
        className is "Shell_TrayWnd" or "Progman" or "Windows.UI.Core.CoreWindow";

    /// <summary>
    /// Snipping Tool's recording toolbar: a topmost window of class <c>XamlWindow</c>.
    /// Its overlay is a tool window, and its own window is not kept on top and takes a tile. A toolbar on a tile would resize
    /// the other windows as a recording starts, inside the recording.
    /// </summary>
    private static bool IsRecordingToolbar(string className, bool topmost) =>
        topmost && className == "XamlWindow";

    private static bool IsPictureInPicture(string title, bool topmost) => topmost && IsPlayerTitle(title);

    /// <summary>
    /// True when <paramref name="title"/> is the one a browser gives its picture-in-picture player.
    /// A player that is kept above other windows is Picture-in-picture. One that is not is a window like any other, and takes a tile (ADR 0076).
    /// </summary>
    /// <param name="title">A window's caption text.</param>
    /// <returns>True for “picture in picture”, with spaces or hyphens, in any case.</returns>
    public static bool IsPlayerTitle(string title) =>
        !string.IsNullOrEmpty(title) && title.Replace('-', ' ').Equals("picture in picture", StringComparison.OrdinalIgnoreCase);

    private enum Kind
    {
        Tile,
        Free,
        PictureInPicture,
        Absent,
    }
}
