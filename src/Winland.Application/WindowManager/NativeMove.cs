namespace Winland.Application;

/// <summary>
/// A Client the system moves by its title bar. Winland treats the move request of a client like a
/// <c>movewindow</c> drag: a tiled Client is lifted out of the Layout, follows the pointer, and is
/// dropped back in where the pointer ends. Here the system moves the window itself, so the core lifts
/// it and drops it but never places it in between. A floating Client stays where the system leaves it.
/// </summary>
public sealed partial class WindowManager
{
    // Where the system left the window it just moved. The motion that drops it into the Layout starts there.
    private (WindowId Window, Rectangle Bounds)? _released;

    private Outcome BeginNativeMove(WindowMoveStarted started)
    {
        if (_interaction is not null)
            return View([]);

        var window = started.Window;
        if (WorkspaceContaining(window) is not LiveWorkspace desk
            || desk.ClientOf(window) is not { Minimized: false } client)
            return View([]);

        _interaction = new NativeMove(window, client.Floating);
        if (client.Floating)
            return BeginNativeFloating(desk, client);

        // Taking a maximized or fullscreen Client by its title leaves that mode first, as a drag in the pinned
        // release does. The Workspace's f[] rules are matched again, and the lift below places the windows.
        if (client.Fullscreen != FullscreenMode.None)
            _ = Restamp(desk, EndFullscreen(desk, window));

        var grab = new PointerDrag(false, PointerDragPhase.Begin, started.Pointer);
        _ = desk.Layout.Drag(window, grab);
        var lifted = desk.Layout.Drag(window, grab with { Phase = PointerDragPhase.Move });
        return View([.. lifted.Where(placement => placement.Client != client.Id)]);
    }

    private Outcome EndNativeMove(WindowMoveEnded ended)
    {
        if (_interaction is not NativeMove move || move.Window != ended.Window)
            return View([]);

        _interaction = null;
        var drop = GroupDrop(ended.Window, wasTiled: !move.Floating, ended.Pointer);
        if (move.Floating)
        {
            var left = FollowNativeFloating(ended.Window, ended.Bounds);
            return drop is { } onto ? JoinDragged(ended.Window, onto, consumed: false) : left;
        }

        _released = ended.Bounds is Rectangle frame ? (ended.Window, frame) : null;
        var layout = LayoutWorkspace(ended.Window).Layout;
        var dropped = layout.Drag(ended.Window, new PointerDrag(false, PointerDragPhase.End, ended.Pointer));
        if (drop is { } into)
            return JoinDragged(ended.Window, into, consumed: false);

        // A Layout that has no drop of its own still has to take the window back from where it was left.
        return View(dropped.Count > 0 ? dropped : layout.Placed());
    }

    // Taking a floating Client that is maximized or fullscreen leaves that mode, as a drag in Winland
    // does. The system already moves the window, so it is not placed, and the other windows are.
    private Outcome BeginNativeFloating(LiveWorkspace desk, Client client)
    {
        if (client.Fullscreen == FullscreenMode.None)
            return View([]);

        var covered = desk.Covered();
        var placements = Restamp(desk, EndFullscreen(desk, client.Window));
        return Present(desk, covered, [.. placements.Where(placement => placement.Client != client.Id)]);
    }

    // Records where the system has the floating Client now, as Winland's own drag puts it there.
    private Outcome FollowNativeFloating(WindowId window, Rectangle? bounds)
    {
        if (bounds is not Rectangle frame || WorkspaceContaining(window) is not LiveWorkspace desk || desk.ClientOf(window) is not { Floating: true })
            return View([]);

        var placements = desk.Layout.PlaceFloating(window, frame);
        var self = new ClientId(window.Value);
        return View([.. placements.Where(placement => placement.Client != self)]);
    }

    /// <summary>The system moves this window by its title bar. A floating one is only followed, a tiled one is lifted.</summary>
    private sealed class NativeMove(WindowId window, bool floating) : PointerInteraction(window)
    {
        public bool Floating { get; } = floating;
    }
}
