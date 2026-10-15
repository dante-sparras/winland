namespace Winland;

/// <summary>
/// A Client the system resizes by its border. Hyprland treats the resize request of a client like a
/// <c>resizewindow</c> drag, so a tiled Client changes its split. The system resizes the window itself,
/// so the core applies the whole drag when the border is let go and never places the window in between.
/// </summary>
public sealed partial class Core
{
    private WindowId? _nativeSize;

    private Outcome BeginNativeSize(WindowSizeStarted started)
    {
        if (_nativeSize is not null || _nativeMove is not null || _gesture is not null)
            return View([]);

        var window = started.Window;
        if (WorkspaceContaining(window) is not LiveWorkspace desk
            || desk.ClientOf(window) is not { Floating: false, Minimized: false })
            return View([]);

        _nativeSize = window;
        _ = desk.Layout.Drag(window, new PointerDrag(true, PointerDragPhase.Begin, started.Pointer));
        return View([]);
    }

    private Outcome EndNativeSize(WindowSizeEnded ended)
    {
        if (_nativeSize != ended.Window)
            return View([]);

        _nativeSize = null;
        var layout = LayoutWorkspace(ended.Window).Layout;
        var covering = layout.Visible();
        var resized = layout.Drag(ended.Window, new PointerDrag(true, PointerDragPhase.Move, ended.Pointer));
        _ = layout.Drag(ended.Window, new PointerDrag(true, PointerDragPhase.End, ended.Pointer));

        // Whatever the border did to the window, it goes back to the rectangle the Layout holds for it.
        return View(resized.Count > 0 ? resized : covering);
    }
}
