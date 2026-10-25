namespace Winland.Application;

/// <summary>The <c>centerwindow</c> dispatcher, and the helper that places a floating Client.</summary>
public sealed partial class WindowManager
{
    // centerwindow centers the active floating Client in its Monitor's work area.
    private Outcome CenterWindow()
    {
        const string name = "centerwindow";
        if (ActiveWindow() is not WindowId window
            || Find(window) is not { Floating: true, Fullscreen: FullscreenMode.None }
            || WorkspaceContaining(window) is not LiveWorkspace desk
            || !desk.Layout.TryBounds(window, out var box))
            return Rejected(name, "error: No floating window found");

        var area = WorkArea(desk, _area);
        var centered = box with
        {
            X = area.X + ((area.Width - box.Width) / 2),
            Y = area.Y + ((area.Height - box.Height) / 2),
        };
        return PlaceWindow(desk, window, centered, name);
    }

    private Outcome PlaceWindow(LiveWorkspace desk, WindowId window, Rectangle bounds, string name)
    {
        _mapPlaced.Remove(window);
        var covered = desk.Covered();
        var placements = desk.Layout.PlaceFloating(window, bounds);
        return _workspaces.Shown.IsShown(desk.Id)
            ? Present(desk, covered, placements, dispatcher: name)
            : View([], dispatcher: name);
    }
}
