namespace Winland;

/// <summary>
/// A Client the system moves by its title bar. Hyprland treats the move request of a client like a
/// <c>movewindow</c> drag: a tiled Client is lifted out of the Layout, follows the pointer, and is
/// dropped back in where the pointer ends. Here the system moves the window itself, so the core lifts
/// it and drops it but never places it in between.
/// </summary>
public sealed partial class Core
{
    private WindowId? _nativeMove;

    private Outcome BeginNativeMove(WindowMoveStarted started)
    {
        if (_nativeMove is not null || _gesture is not null)
            return View([]);

        var window = started.Window;
        if (WorkspaceContaining(window) is not LiveWorkspace desk
            || desk.ClientOf(window) is not { Floating: false, Minimized: false } client)
            return View([]);

        _nativeMove = window;

        // Taking a maximized or fullscreen Client by its title leaves that mode first, as a Hyprland drag does.
        if (client.Fullscreen != FullscreenMode.None)
            _ = desk.Layout.SetFullscreen(window, FullscreenMode.None, FullscreenMode.None);

        var grab = new PointerDrag(false, PointerDragPhase.Begin, started.Pointer);
        _ = desk.Layout.Drag(window, grab);
        var lifted = desk.Layout.Drag(window, grab with { Phase = PointerDragPhase.Move });
        return View([.. lifted.Where(placement => placement.Client != client.Id)]);
    }

    private Outcome EndNativeMove(WindowMoveEnded ended)
    {
        if (_nativeMove != ended.Window)
            return View([]);

        _nativeMove = null;
        var layout = LayoutWorkspace(ended.Window).Layout;
        var dropped = layout.Drag(ended.Window, new PointerDrag(false, PointerDragPhase.End, ended.Pointer));

        // A Layout that has no drop of its own still has to take the window back from where it was left.
        return View(dropped.Count > 0 ? dropped : layout.Placed());
    }
}
