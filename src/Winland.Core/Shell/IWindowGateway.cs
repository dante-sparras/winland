namespace Winland.Core.Shell;

/// <summary>A window Winoma is allowed to tile.</summary>
/// <param name="Id">Native handle.</param>
/// <param name="Title">Caption text.</param>
public readonly record struct TrackedWindow(long Id, string Title);

/// <summary>Lists and moves real windows.</summary>
public interface IWindowGateway : ITilePlacement
{
    /// <summary>
    /// Tiles, picture-in-picture players, and free windows on the glass, from one census.
    /// One call is one read, so the three lists cannot disagree. A window Winoma hid is on none of them.
    /// </summary>
    AdmittedWindows World();

    /// <summary>Windows on the glass that should be tiled.</summary>
    IReadOnlyList<TrackedWindow> Snapshot();

    /// <summary>
    /// Takes every window in <paramref name="ids"/> off the screen, the way <see cref="WindowHide"/> decides: Explorer's cloak,
    /// or <c>SW_HIDE</c> for a window the cloak is not used on. No call waits for a frame or for the window's program, so a Workspace's
    /// windows go in one frame. A cloaked window keeps the keyboard, so the caller moves the keyboard first. A window that is already
    /// hidden is left alone, and costs one lookup: every arrange asks again for every Scratchpad window.
    /// </summary>
    /// <param name="ids">Window identities. Empty does nothing.</param>
    /// <returns>
    /// The windows that are still on the screen: neither way took them off. An administrator window is not one of them as a rule, since Explorer cloaks it too.
    /// Empty when every window is hidden. The caller keeps such a window with the Workspace in front, and never counts it as hidden.
    /// </returns>
    IReadOnlyList<long> HideAll(IReadOnlyList<long> ids);

    /// <summary>
    /// Shows every window in <paramref name="ids"/> that Winoma hid, each the way it was hidden, and waits for no frame.
    /// A Workspace switch asks for this and then hides the windows it leaves, and both reach the screen together.
    /// A window under Winoma's kind of cloak that this desktop did not hide itself is uncloaked too (<see cref="HiddenByCloak"/>).
    /// A window that is not hidden is left alone. One Explorer did not uncloak stays hidden, and a later call asks again.
    /// </summary>
    /// <param name="ids">Window identities. Empty does nothing.</param>
    void ShowAll(IReadOnlyList<long> ids);

    /// <summary>
    /// Shows every window in <paramref name="ids"/> as <see cref="ShowAll"/> does, and waits for the next frame once, after the last.
    /// The Scratchpad's windows are placed right after they are shown, and a desktop that is asked to Stop shows what it hid.
    /// </summary>
    /// <param name="ids">Window identities. Empty does nothing.</param>
    void ShowAllAndWait(IReadOnlyList<long> ids);

    /// <summary>
    /// True when Windows keeps <paramref name="id"/> on a virtual desktop of its own that is not in front
    /// (<c>IVirtualDesktopManager</c>, which is documented). Such a window reads the same cloak as one Winoma hid, and it is not Winoma's (ADR 0003).
    /// The shell asks about a window it showed that did not come onto the glass.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <returns>False for a window on the Windows desktop in front, and when Windows cannot say.</returns>
    bool OnAnotherDesktop(long id);

    /// <summary>
    /// True when Winoma's kind of cloak is on <paramref name="id"/> (<see cref="WindowHide.Cloaked"/>): the shell's cloak, on a window
    /// Windows does not keep on a virtual desktop of its own. This desktop put it there, or one before it did.
    /// A desktop that starts asks about each marked window that is not on the glass, and a desktop that stops about each window it showed.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <returns>False for a window on the glass, a closed one, and one Windows cloaked.</returns>
    bool HiddenByCloak(long id);

    /// <summary>
    /// Drops the keyboard when <paramref name="leaving"/> has it, before that window is hidden: a cloaked window would keep it.
    /// The shell asks when no window of the workspace can take the keyboard.
    /// </summary>
    /// <param name="leaving">The window about to be hidden.</param>
    void ReleaseFocus(long leaving);

    /// <summary>
    /// The window the keyboard left for the one that has it now, when that window was already off the glass as the keyboard moved:
    /// it had closed, or was hidden or minimized. Windows then handed the keyboard on by itself, to the next window in its own order,
    /// and that can be one Winoma keeps hidden. A window that takes the keyboard while the one before is still on the glass asked for it.
    /// </summary>
    /// <returns>Null when the window before was still on the glass, and when the Host did not see the keyboard move.</returns>
    long? ForegroundLeft();

    /// <summary>
    /// Writes down the windows Winoma has hidden, as <see cref="HiddenWindows"/>, so they can be shown after a desktop that was killed.
    /// The shell calls this before a window is hidden and after one is shown, so the record is never short of the glass.
    /// A window whose record could not be written is not hidden (<see cref="IHideHost.Record"/>): <see cref="HideAll"/> answers that it is still on the screen.
    /// </summary>
    /// <param name="ids">Every window that is hidden now or about to be. Empty when none is.</param>
    void RecordHidden(IReadOnlyCollection<long> ids);

    /// <summary>The window that currently has the keyboard, if any.</summary>
    long? Foreground();

    /// <summary>Closes a window.</summary>
    void Close(long id);

    /// <summary>Focuses a window. The pointer stays where it is.</summary>
    void Focus(long id);

    /// <summary>Makes a window the foreground one without moving the pointer.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>False when the window stayed back and the caller may move the keyboard off a hidden workspace.</returns>
    bool Activate(long id);

    /// <summary>Drops a refused focus and an open handoff. A workspace change calls this.</summary>
    void ResetFocusRefusal();

    /// <summary>
    /// True when the window that has the keyboard is under the shell's cloak: it is not drawn though Windows calls it visible,
    /// so typing would miss the screen. Winoma hid it, or Windows keeps it on a virtual desktop of its own that is not in front.
    /// </summary>
    bool ForegroundCloaked();

    /// <summary>Drops the keyboard when it is still on a window that is not drawn: one Winoma hid, or one under the shell's cloak.</summary>
    void ReleaseFocus();

    /// <summary>The tiled window whose tile contains the pointer, if any.</summary>
    long? WindowUnderPointer();

    /// <summary>
    /// Removes close, minimize, maximize, and the thick resize border so a tile can sit flush.
    /// A window that fills <paramref name="screen"/> keeps its frame: it is fullscreen in its own way.
    /// Asked for every tile on every arrange, so a window that is already stripped costs one read of its style.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <param name="screen">
    /// The monitor of the window's Workspace. Empty when that monitor has no bar and its tiles reach every edge:
    /// a tile that covers it is then a tile, and no window keeps its frame for covering it.
    /// </param>
    /// <returns>True when the window's frame changed, so it no longer sits on its tile the way it was placed.</returns>
    bool ApplyCaption(long id, TileRect screen);

    /// <summary>
    /// The work area of the monitor the first Bar is on: the pixel rectangle Winoma may tile into there, already clear of that Bar.
    /// It is the monitor a Workspace falls back to when its own is not listed, and the only one while <see cref="Monitors"/> is empty.
    /// </summary>
    TileRect FirstBarArea { get; }

    /// <summary>
    /// Work areas of the attached monitors, each clear of its own Bar (ADR 0080). Empty when the host has not listed them.
    /// </summary>
    IReadOnlyList<TileRect> Monitors { get; }

    /// <summary>Monitor rectangles, including the strip each monitor's Bar reserves. Same order as <see cref="Monitors"/>.</summary>
    IReadOnlyList<TileRect> Screens { get; }

    /// <summary>True when a visible window fills <paramref name="screen"/>.</summary>
    /// <param name="screen">A monitor rectangle.</param>
    /// <returns>True when some other process's window covers that monitor.</returns>
    bool WindowFills(TileRect screen);

    /// <summary>True when <paramref name="id"/> is drawn: visible, not minimized, and not cloaked.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>False for a closed, hidden, or minimized window, and for one under a cloak, Winoma's or Windows'.</returns>
    bool Shown(long id);

    /// <summary>True when <paramref name="id"/> still names a window.</summary>
    /// <param name="id">Window identity.</param>
    bool Alive(long id);

    /// <summary>The rectangle <paramref name="id"/> covers now, measured the way a tile is.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>Null for a closed or minimized window.</returns>
    TileRect? Frame(long id);

    /// <summary>The rectangle <paramref name="id"/> was made with, while it is a window a program made and has not shown yet.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>Null for a window that is shown, is gone, another window owns, or could never take a tile.</returns>
    TileRect? Unshown(long id);

    /// <summary>Moves <paramref name="id"/>, which is not shown yet, without giving it the keyboard or changing its place in the stack.</summary>
    /// <param name="id">Window identity.</param>
    /// <param name="rect">Where it goes, in physical pixels.</param>
    /// <param name="tile">True when <paramref name="rect"/> is a tile, measured as <see cref="Frame"/> measures. False when it is the window's own rectangle.</param>
    void Seat(long id, TileRect rect, bool tile);

    /// <summary>
    /// Asks <paramref name="id"/>'s app to enter or leave its own fullscreen, by pressing the key most apps answer for it, F11.
    /// The window must have the keyboard, since the key goes where the keyboard is.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <returns>False when the key was not sent, as when another window has the keyboard.</returns>
    bool AskFullscreen(long id);

    /// <summary>
    /// True when <paramref name="id"/>'s app keeps a fullscreen window over the monitor: it puts the window back there each time the keyboard
    /// moves onto it or off it, and lays its page out for the monitor as it does. Chromium and the apps built on it do.
    /// Such a window cannot be held on a tile, so it is left where its app puts it (ADR 0076).
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <returns>False for an app whose fullscreen follows its window, such as Windows Terminal.</returns>
    bool FullscreenTakesMonitor(long id);

    /// <summary>Keeps <paramref name="id"/> above the other windows, or stops keeping it there. A picture-in-picture player is one or the other (ADR 0076).</summary>
    /// <param name="id">Window identity.</param>
    /// <param name="above">
    /// True to keep it above, where it floated before, with the frame Winoma took off it. False to let it take its place among the windows.
    /// </param>
    void KeepAbove(long id, bool above);

    /// <summary>
    /// True when every placed window still sits on its tile, or stayed where it was after a placement asked it to move.
    /// True as well when nothing has been placed, and while a placement is in progress. The bar loop asks on every turn.
    /// </summary>
    bool TilesHold();

    /// <summary>
    /// Names the windows a Carry may take though they have no tile: the floated windows of the workspace in front, and the Sticky ones.
    /// The shell names them again whenever the set changes. Until it does, there are none.
    /// </summary>
    /// <param name="ids">Every such window. Empty when there is none.</param>
    void NoteFloated(IReadOnlyCollection<long> ids);

    /// <summary>Picture-in-picture players. They stay off the tile layout, and no Workspace switch hides them.</summary>
    IReadOnlyList<long> PictureInPicture();

    /// <summary>Dialogs, owned windows, and fixed popups on the glass.</summary>
    IReadOnlyList<TrackedWindow> Free();

    /// <summary>
    /// The window that owns <paramref name="id"/>, as a dialog is owned by the window it was opened from. Windows draws an owned window
    /// above its owner and cloaks it with its owner, so it is on its owner's Workspace (ADR 0079).
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <returns>Zero when no window owns it, and when it is closed.</returns>
    long Owner(long id);

    /// <summary>The caption of <paramref name="id"/>, or empty when it has none.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>The window title.</returns>
    string Caption(long id);

    /// <summary>
    /// Writes the Workspace <paramref name="id"/> is on onto the window itself, as the property <see cref="WorkspaceMark.Property"/> (ADR 0078).
    /// Asked for every tile on every arrange, so a window that already carries <paramref name="mark"/> costs one read.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <param name="mark">A Workspace number, <see cref="WorkspaceMark.Scratchpad"/>, or <see cref="WorkspaceMark.None"/> to take the mark off.</param>
    void MarkWorkspace(long id, int mark);

    /// <summary>
    /// The mark of every open window that carries one, read off the windows themselves: a window Winoma hid is read too.
    /// A desktop that starts asks once, to find where the desktop before it left each window.
    /// </summary>
    /// <returns>Each marked window with its mark. A window with no mark is left out.</returns>
    IReadOnlyDictionary<long, int> WorkspaceMarks();

    /// <summary>
    /// Writes the Layout of the Workspace <paramref name="id"/> is on and its Group onto the window itself, as the property <see cref="TilingMark.Property"/> (ADR 0050).
    /// Asked for every window of a Workspace whose Layout or Groups changed, so a window that already carries <paramref name="mark"/> costs one read.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <param name="mark">What <see cref="TilingMark.Write"/> gives, or <see cref="TilingMark.None"/> to take the mark off.</param>
    void MarkTiling(long id, int mark);

    /// <summary>
    /// The tiling mark of every open window that carries one, read off the windows themselves: a window Winoma hid is read too.
    /// A desktop that starts asks once, with <see cref="WorkspaceMarks"/>, to find the Layout and the Groups the desktop before it left.
    /// </summary>
    /// <returns>Each marked window with its mark. A window with no mark is left out.</returns>
    IReadOnlyDictionary<long, int> TilingMarks();

    /// <summary>
    /// The windows the record named when this desktop took it over still hidden: the desktop before this one was replaced
    /// (<see cref="Lifetime.TakesOverHidden"/>, ADR 0059). <see cref="ShowAllAndWait"/> shows one, and the record names them all until the shell writes it again.
    /// A desktop that starts asks once. It keeps each one whose mark names a Workspace that is not in front, or the Scratchpad, and shows the rest.
    /// The record is not what makes a hidden window this desktop's own: the shell also takes every marked window it finds under
    /// Winoma's kind of cloak (<see cref="HiddenByCloak"/>, ADR 0078). These are the ones a mark would miss: a window hidden with <c>SW_HIDE</c>, and one with no mark.
    /// </summary>
    /// <returns>Empty after a crash or a Stop: the windows the record named were shown before this desktop looked.</returns>
    IReadOnlyList<long> LeftHidden();
}
