using System.Text.Json.Serialization;

namespace Winland.Core.Cli;

/// <summary>The tiler's picture of the desktop, before the shell adds panels, plugins, capture, and the theme.</summary>
public sealed record TilerFacts(
    IReadOnlyList<WorkspaceFact> Workspaces,
    IReadOnlyList<WindowFact> Windows,
    long? Focused,
    bool Spacing,
    bool IdleLock,
    bool Scratchpad,
    bool Switching,
    IReadOnlyList<BarFact> Bars,
    int Monitor,
    bool SquareTile,
    string Font);

/// <summary>One workspace in the desktop snapshot.</summary>
/// <param name="Number">Workspace number, 1-based.</param>
/// <param name="Occupancy">How many windows it holds: its tiles, and each Free window that no window owns. A dialog is its owner's, and is not counted.</param>
/// <param name="Layout"><c>dwindle</c> or <c>scrolling</c>.</param>
/// <param name="Monitor">The 0-based index of the monitor the workspace is on. One that does not exist yet would be made on the monitor with the keyboard.</param>
/// <param name="Grouping">True while a group is armed on this workspace.</param>
public sealed record WorkspaceFact(int Number, int Occupancy, string Layout, int Monitor, bool Grouping)
{
    /// <summary>True when the workspace is in front on its monitor: its windows are on the screen. Each monitor has one.</summary>
    public bool Front { get; init; }

    /// <summary>Each Group on the workspace, its members in the order its strip shows them. Absent while no window shares a tile.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<GroupFact>? Groups { get; init; }
}

/// <summary>One Group on a workspace in the desktop snapshot.</summary>
/// <param name="Members">The windows that share the tile, in the order the Group's strip lists them.</param>
/// <param name="Shown">The member the tile shows. The others are hidden behind it.</param>
public sealed record GroupFact(IReadOnlyList<long> Members, long Shown);

/// <summary>One window in the desktop snapshot.</summary>
/// <param name="Hwnd">Window identity.</param>
/// <param name="Title">Caption text. Empty when the window has none.</param>
/// <param name="Place"><c>workspace</c>, <c>scratchpad</c>, <c>sticky</c>, or <c>picture-in-picture</c>.</param>
/// <param name="Workspace">The workspace number when <paramref name="Place"/> is <c>workspace</c>.</param>
/// <param name="Floated">True when the window is floated out of its tile. A sticky window is floated.</param>
/// <param name="Fullscreen">True when the window covers its screen, from the fullscreen chord or because its own app made it fullscreen.</param>
/// <param name="Grouped">True when the window shares its tile.</param>
/// <param name="Free">True when the window is a dialog, an owned window, or a fixed popup.</param>
/// <param name="FullWidth">True when the window takes the full width and height of its workspace, below the bar.</param>
/// <param name="Pseudo">True when the window is drawn at its pseudotile size.</param>
public sealed record WindowFact(
    long Hwnd,
    string Title,
    string Place,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Workspace,
    bool Floated,
    bool Fullscreen,
    bool Grouped,
    bool Free,
    bool FullWidth,
    bool Pseudo);

/// <summary>One App chord role in the desktop snapshot.</summary>
/// <param name="Role">The role, such as <c>music</c>. Its Chord command is <c>app.</c> and this name.</param>
/// <param name="Program">What the Host's <c>apps</c> setting names for it. Empty when it names nothing.</param>
/// <param name="Opens">
/// <c>program</c> for a program Start lists, <c>path</c> for a file, <c>not-found</c> when Start lists no program by that name,
/// <c>unread</c> when the desktop has not read Start's programs yet, or <c>nothing</c> when the Host names no program.
/// </param>
/// <param name="Target">The program's id under <c>shell:AppsFolder</c>, or the path. Empty when the role opens nothing.</param>
/// <param name="Offers">
/// What the first press of the role would offer to install while it is empty: the program's name, or <c>no-windows-build</c> when Winoma knows none.
/// Empty when the role would launch something or the catalog does not name it.
/// </param>
public sealed record AppFact(string Role, string Program, string Opens, string Target, string Offers = "");

/// <summary>One monitor's Bar, as the desktop snapshot prints it. Every monitor has one (ADR 0080).</summary>
/// <param name="Monitor">The 0-based index of the monitor the Bar is on.</param>
/// <param name="Visible">True when the Bar is not hidden. One setting hides or shows every Bar, so it reads the same on each.</param>
/// <param name="Yields">True when a full-screen window covers this Bar's monitor.</param>
public sealed record BarFact(int Monitor, bool Visible, bool Yields);
