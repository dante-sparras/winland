namespace Winland.Application;

/// <summary>
/// A Client the system resizes by its border. Winland treats the resize request of a client like a
/// <c>resizewindow</c> drag, so a tiled Client changes its split, live, while the border is held. The system
/// resizes the window itself, so the core never places that window in between: its neighbours follow the
/// border, and the window gets the Layout's rectangle when the border is let go. A floating Client keeps the
/// frame the system gives it.
/// </summary>
public sealed partial class WindowManager
{
    private Outcome BeginNativeSize(WindowSizeStarted started)
    {
        if (_interaction is not null)
            return View([]);

        var window = started.Window;
        if (WorkspaceContaining(window) is not LiveWorkspace desk
            || desk.ClientOf(window) is not { Minimized: false } client)
            return View([]);

        _interaction = new NativeSize(window, client.Floating);
        if (client.Floating)
            return BeginNativeFloating(desk, client);
        _ = desk.Layout.Drag(window, new PointerDrag(true, PointerDragPhase.Begin, started.Pointer));
        return View([]);
    }

    private Outcome ContinueNativeSize(WindowSizing sizing)
    {
        if (_interaction is not NativeSize size || size.Window != sizing.Window)
            return View([]);

        if (size.Floating)
            return FollowNativeFloating(sizing.Window, sizing.Bounds) with { Live = true };

        var resized = LayoutWorkspace(sizing.Window).Layout.Drag(sizing.Window, new PointerDrag(true, PointerDragPhase.Move, sizing.Pointer));
        var self = new ClientId(sizing.Window.Value);
        var neighbours = resized.Where(placement => placement.Client != self).ToList();
        return View(neighbours) with { Live = true };
    }

    private Outcome EndNativeSize(WindowSizeEnded ended)
    {
        if (_interaction is not NativeSize size || size.Window != ended.Window)
            return View([]);

        _interaction = null;
        if (size.Floating)
            return FollowNativeFloating(ended.Window, ended.Bounds);

        var layout = LayoutWorkspace(ended.Window).Layout;
        var covering = layout.Visible();
        var resized = layout.Drag(ended.Window, new PointerDrag(true, PointerDragPhase.Move, ended.Pointer));
        var released = layout.Drag(ended.Window, new PointerDrag(true, PointerDragPhase.End, ended.Pointer));

        // Whatever the border did to the window, it goes back to the rectangle the Layout holds for it. The end
        // can move the windows again (a scrolling tape recenters), so its placements win over the last step's.
        return View(released.Count > 0 ? released : resized.Count > 0 ? resized : covering);
    }

    /// <summary>The system resizes this window by its border. A floating one is only followed, a tiled one changes its split.</summary>
    private sealed class NativeSize(WindowId window, bool floating) : PointerInteraction(window)
    {
        public bool Floating { get; } = floating;
    }
}
