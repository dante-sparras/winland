namespace Winland;

/// <summary>
/// The algorithm that places the Clients of one Workspace.
/// A Workspace holds one Layout. Core adopts, drops, minimizes and restores Clients through it,
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
        bool activeWorkspace = true,
        Rectangle monitor = default,
        long processId = 0,
        string initialClass = "",
        string initialTitle = "",
        Size? minSize = null,
        Size? maxSize = null);

    /// <summary>Stores a Client's minimum and maximum and lays the Workspace out again.</summary>
    IReadOnlyList<Placement> UpdateLimits(WindowId window, Size? minSize, Size? maxSize);

    /// <summary>Drops a Client from this Workspace.</summary>
    IReadOnlyList<Placement> Remove(WindowId window);

    /// <summary>Minimizes a Client and returns the placements that remain.</summary>
    IReadOnlyList<Placement> Minimize(WindowId window);

    /// <summary>Restores a minimized Client.</summary>
    IReadOnlyList<Placement> Restore(WindowId window);

    /// <summary>Removes a Client so another Workspace's Layout can adopt it.</summary>
    bool TryTake(WindowId window, out Taken taken);

    /// <summary>The rectangle a Client is shown at, when the Layout still has one.</summary>
    bool TryBounds(WindowId window, out Rectangle bounds);

    /// <summary>Placements of the Clients shown on this Workspace, including floating Clients.</summary>
    IReadOnlyList<Placement> Visible();

    /// <summary>Placements of the tiled Clients this Layout is showing.</summary>
    IReadOnlyList<Placement> Placed();

    /// <summary>Sets the direction of the next split on this Workspace.</summary>
    IReadOnlyList<Placement> Preselect(string direction);

    /// <summary>Toggles pseudo for a Client.</summary>
    IReadOnlyList<Placement> TogglePseudo(WindowId window);

    /// <summary>The Client focus moves to, in the requested direction.</summary>
    WindowId? MoveFocus(WindowId active, string arguments);

    /// <summary>Records the focused Client, which the next split uses.</summary>
    void NoteFocus(WindowId window);

    /// <summary>Moves a Client in the requested direction.</summary>
    IReadOnlyList<Placement> MoveWindow(WindowId window, string arguments);

    /// <summary>
    /// When a directional move stays inside this Workspace, master, monocle and scrolling
    /// continue onto the Monitor in that direction. Dwindle continues only when the point
    /// just outside the Client already lies on another Monitor.
    /// </summary>
    bool FollowsMonitorWhenUnmoved => false;

    /// <summary>Shifts floating Clients by a Monitor move. Tiled Clients are refit separately.</summary>
    void NudgeFloating(int dx, int dy)
    {
    }

    /// <summary>Resizes the active Client.</summary>
    IReadOnlyList<Placement> ResizeActive(WindowId window, string arguments);

    /// <summary>Toggles whether a Client floats.</summary>
    IReadOnlyList<Placement> ToggleFloating(WindowId window);

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

    /// <summary>Applies master-layout options. Other layouts ignore them.</summary>
    void Apply(MasterOptions options)
    {
    }

    /// <summary>The Client to focus after a close, when this Layout chooses one.</summary>
    WindowId? FocusAfterClose() => null;

    /// <summary>
    /// Records a Client's fullscreen modes. Mode 0 is its layout slot.
    /// A covering mode replaces any other covering Client on this Workspace.
    /// </summary>
    IReadOnlyList<Placement> SetFullscreen(WindowId window, FullscreenMode mode, FullscreenMode clientMode);

    /// <summary>Moves a floating Client to a rectangle. An empty list means that Client is not floating.</summary>
    IReadOnlyList<Placement> PlaceFloating(WindowId window, Rectangle bounds) => [];
}

/// <summary>One step of a mouse move or resize. <see cref="KeepRatio"/> holds a floating Client's aspect ratio while it is resized.</summary>
internal readonly record struct PointerDrag(bool Resize, PointerDragPhase Phase, Point Pointer, bool KeepRatio = false);

internal static class FloatingShift
{
    public static void By(Dictionary<ClientId, Rectangle> floating, int dx, int dy)
    {
        ArgumentNullException.ThrowIfNull(floating);
        if ((dx == 0 && dy == 0) || floating.Count == 0)
            return;

        foreach (var id in floating.Keys.ToArray())
        {
            var box = floating[id];
            floating[id] = box with { X = box.X + dx, Y = box.Y + dy };
        }
    }
}

/// <summary>A Client handed from one Workspace's Layout to another.</summary>
internal readonly record struct Taken(
    bool Floating,
    bool Minimized,
    Rectangle Bounds,
    string Class = "",
    string Title = "",
    long ProcessId = 0,
    string InitialClass = "",
    string InitialTitle = "",
    Size? MinSize = null,
    Size? MaxSize = null,
    double? ScrollingWidth = null);
