namespace Winland.Domain;

/// <summary>
/// The algorithm that places the Clients of one Workspace.
/// A Workspace holds one Layout. WindowManager adopts, drops, minimizes and restores Clients through it,
/// moves them between Workspaces, and reads the placements of the Clients shown on a Monitor.
/// </summary>
internal interface Layout
{
    /// <summary>The Clients this Layout holds, including minimized and floating Clients.</summary>
    IReadOnlyList<Client> Clients { get; }

    /// <summary>Applies options and returns the placements of the tiled Clients this Layout is showing.</summary>
    IReadOnlyList<Placement> Apply(LayoutOptions options);

    /// <summary>Fits this Layout to a Monitor's work area.</summary>
    void Fit(Rectangle area);

    /// <summary>Gives every Client the Workspace's new id, after a move to another Monitor.</summary>
    void Readdress(WorkspaceId workspace);

    /// <summary>Gives the Client its window's new title.</summary>
    void Retitle(WindowId window, string title);

    /// <summary>Gives the Client its window's new class. The class it had when it appeared stays.</summary>
    void Reclass(WindowId window, string windowClass);

    /// <summary>Adopts a Window as a Client of this Workspace.</summary>
    IReadOnlyList<Placement> Add(
        WindowId window,
        Rectangle area,
        Point? pointer,
        bool floating,
        Rectangle bounds,
        WorkspaceId workspace,
        string windowClass = "",
        string title = "",
        Rectangle monitor = default,
        long processId = 0,
        string initialClass = "",
        string initialTitle = "",
        Size? minSize = null,
        Size? maxSize = null,
        TiledLimits? tiledLimits = null);

    /// <summary>
    /// Stores a Client's minimum and maximum and lays the Workspace out again. <paramref name="tiledLimits"/> are the
    /// limits it keeps while tiled, when they differ.
    /// </summary>
    IReadOnlyList<Placement> UpdateLimits(WindowId window, Size? minSize, Size? maxSize, TiledLimits? tiledLimits = null);

    /// <summary>Drops a Client from this Workspace.</summary>
    IReadOnlyList<Placement> Remove(WindowId window);

    /// <summary>Minimizes a Client and returns the placements that remain.</summary>
    IReadOnlyList<Placement> Minimize(WindowId window);

    /// <summary>Restores a minimized Client.</summary>
    IReadOnlyList<Placement> Restore(WindowId window);

    /// <summary>Removes a Client so another Workspace's Layout can adopt it.</summary>
    bool TryTake(WindowId window, out Taken taken);

    /// <summary>
    /// Hands the slot of <paramref name="current"/> to <paramref name="next"/>, a Window this Layout
    /// does not hold. The slot keeps its place, size, floating and fullscreen state. A Group shows one
    /// member in its tile this way. False when <paramref name="current"/> is not here or
    /// <paramref name="next"/> already is.
    /// </summary>
    bool Substitute(WindowId current, WindowId next, Taken incoming, out Taken outgoing);

    /// <summary>The rectangle a Client is shown at, when the Layout still has one.</summary>
    bool TryBounds(WindowId window, out Rectangle bounds);

    /// <summary>Placements of the Clients shown on this Workspace, including floating Clients.</summary>
    IReadOnlyList<Placement> Visible();

    /// <summary>Placements of the tiled Clients this Layout is showing.</summary>
    IReadOnlyList<Placement> Placed();

    /// <summary>
    /// Puts the tiles back on the rectangles a restart found in the state file, where this Layout can tell its
    /// arrangement from them. Changes nothing otherwise.
    /// </summary>
    IReadOnlyList<Placement> Arrange(IReadOnlyDictionary<WindowId, Rectangle> tiles) => [];

    /// <summary>Sets the direction of the next split on this Workspace.</summary>
    IReadOnlyList<Placement> Preselect(string direction);

    /// <summary>Toggles pseudo for a Client.</summary>
    IReadOnlyList<Placement> TogglePseudo(WindowId window);

    /// <summary>The Client focus moves to, in the requested direction.</summary>
    WindowId? MoveFocus(WindowId active, string arguments);

    /// <summary>
    /// The Client in the requested direction, as <see cref="MoveFocus"/> finds it, without moving
    /// focus, cycling or scrolling the Layout.
    /// </summary>
    WindowId? Neighbor(WindowId active, string arguments) => MoveFocus(active, arguments);

    /// <summary>Records the focused Client, which the next split uses.</summary>
    void NoteFocus(WindowId window);

    /// <summary>
    /// Records that the pointer gave a Client the focus. The scrolling Layout follows it there only when enough of the
    /// Client shows. Other Layouts take no note of it.
    /// </summary>
    void NotePointerFocus(WindowId window)
    {
    }

    /// <summary>
    /// True once after the Layout moved its tiles by itself, as scrolling does when focus scrolls its strip, so the core
    /// sends its placements. Layouts whose tiles move only when asked return false.
    /// </summary>
    bool TakeMoved() => false;

    /// <summary>
    /// How a direction picks among the tiles next to a Client: by how recently each was focused, higher being more
    /// recent. Layouts that do not look by edge ignore it.
    /// </summary>
    void RankFocus(Func<WindowId, int> recency)
    {
    }

    /// <summary>Moves a Client in the requested direction.</summary>
    IReadOnlyList<Placement> MoveWindow(WindowId window, string arguments);

    /// <summary>
    /// When a directional move stays inside this Workspace, scrolling continues onto the Monitor in that
    /// direction. Dwindle continues only when the point
    /// just outside the Client already lies on another Monitor.
    /// </summary>
    bool FollowsMonitorWhenUnmoved => false;

    /// <summary>Shifts floating Clients by a Monitor move. Tiled Clients are refit separately.</summary>
    void NudgeFloating(int dx, int dy)
    {
    }

    /// <summary>Resizes the active Client.</summary>
    IReadOnlyList<Placement> ResizeActive(WindowId window, string arguments);

    /// <summary>Toggles whether a Client floats. A Client tiled again goes in at <paramref name="pointer"/>, as a new one does.</summary>
    IReadOnlyList<Placement> ToggleFloating(WindowId window, Point? pointer = null);

    /// <summary>Exchanges two Clients' places. Layouts that cannot swap them change nothing.</summary>
    IReadOnlyList<Placement> Swap(WindowId active, WindowId other) => [];

    /// <summary>A layout message for this Layout. An unrecognized message changes nothing.</summary>
    IReadOnlyList<Placement> LayoutMessage(WindowId? active, string arguments);

    /// <summary>
    /// Moves or resizes a Client with the pointer.
    /// Begin records the grab. Move follows the pointer. End drops a tiled Client back into the tree.
    /// </summary>
    IReadOnlyList<Placement> Drag(WindowId window, PointerDrag drag);

    /// <summary>Set when the last layout message was rejected. Null when it was accepted.</summary>
    string? LayoutMessageError { get; }

    /// <summary>The Client the last layout message asked to focus. Null when it did not move focus.</summary>
    WindowId? LayoutMessageFocus => null;

    /// <summary>Replaces the scrolling options. Layouts that are not scrolling ignore them.</summary>
    void Configure(ScrollingOptions options)
    {
    }

    /// <summary>Remembers a <c>scrolling_width</c> rule for the next time this Window is added.</summary>
    void NoteScrollingWidth(WindowId window, double width)
    {
    }

    /// <summary>The Client to focus after a close, when this Layout chooses one.</summary>
    WindowId? FocusAfterClose() => null;

    /// <summary>
    /// Records a Client's fullscreen modes. Mode 0 is its layout slot.
    /// A covering mode replaces any other covering Client on this Workspace.
    /// </summary>
    IReadOnlyList<Placement> SetFullscreen(WindowId window, FullscreenMode mode, FullscreenMode clientMode);

    /// <summary>
    /// Records a Client's fullscreen modes. With <paramref name="layoutAware"/> a Layout that lays out its own
    /// fullscreen Clients takes this one, as Winland's scrolling Layout does, and marks it
    /// <see cref="Client.LayoutFullscreen"/>. Other Layouts ignore it.
    /// </summary>
    IReadOnlyList<Placement> SetFullscreen(WindowId window, FullscreenMode mode, FullscreenMode clientMode, bool layoutAware) =>
        SetFullscreen(window, mode, clientMode);

    /// <summary>
    /// Whether this Client's fullscreen or maximized mode covers its Workspace now. A Client the Layout lays out
    /// fullscreen itself covers only while its column fills the view.
    /// </summary>
    bool FullscreenCovers(WindowId window) => true;

    /// <summary>Moves a floating Client to a rectangle. An empty list means that Client is not floating.</summary>
    IReadOnlyList<Placement> PlaceFloating(WindowId window, Rectangle bounds) => [];
}
