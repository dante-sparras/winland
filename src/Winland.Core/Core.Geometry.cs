using System.Globalization;

namespace Winland;

public sealed partial class Core
{
    private WarpTo? _requestedWarp;

    // movewindowpixel moves a floating Client. A tiled Client is left where it is.
    private Outcome MoveWindowPixel(string arguments, bool allowsElevation)
    {
        const string name = "movewindowpixel";
        var (vector, selector) = SplitSelector(arguments);
        if (Select(selector, emptyMeansActive: false) is not WindowId window
            || Find(window) is not Client client
            || WorkspaceContaining(window) is not LiveWorkspace desk)
            return Rejected(name, "error: moveWindow: no window", allowsElevation);

        if (client.Fullscreen != FullscreenMode.None)
            return Rejected(name, "error: Window is fullscreen", allowsElevation);

        if (!client.Floating || !desk.Layout.TryBounds(window, out var box))
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        var to = WindowVector.Resolve(vector, new Point(box.X, box.Y), FocusedMonitorSize());
        return PlaceWindow(desk, window, box with { X = to.X, Y = to.Y }, name, allowsElevation);
    }

    // resizewindowpixel resizes through the Layout, so a tiled Client resizes its tile.
    private Outcome ResizeWindowPixel(string arguments, bool allowsElevation)
    {
        const string name = "resizewindowpixel";
        var (vector, selector) = SplitSelector(arguments);
        if (Select(selector, emptyMeansActive: false) is not WindowId window
            || Find(window) is not Client client
            || WorkspaceContaining(window) is not LiveWorkspace desk
            || !desk.Layout.TryBounds(window, out var box))
            return Rejected(name, "error: resizeWindow: no window", allowsElevation);

        var size = WindowVector.Resolve(vector, new Point(box.Width, box.Height), FocusedMonitorSize());
        if (size.X < 1 || size.Y < 1)
            return Rejected(name, "error: Invalid size", allowsElevation);

        if (client.Fullscreen != FullscreenMode.None)
            return Rejected(name, "error: Window is fullscreen", allowsElevation);

        _mapPlaced.Remove(window);
        var covered = desk.Covered();
        var delta = string.Create(CultureInfo.InvariantCulture, $"{size.X - box.Width} {size.Y - box.Height}");
        var placements = desk.Layout.ResizeActive(window, delta);
        return IsShown(desk.Id)
            ? Present(desk, covered, placements, dispatcher: name, allowsElevation: allowsElevation)
            : View([], dispatcher: name, allowsElevation: allowsElevation);
    }

    // centerwindow centers the active floating Client in its Monitor's work area.
    private Outcome CenterWindow(bool allowsElevation)
    {
        const string name = "centerwindow";
        if (ActiveWindow() is not WindowId window
            || Find(window) is not { Floating: true, Fullscreen: FullscreenMode.None }
            || WorkspaceContaining(window) is not LiveWorkspace desk
            || !desk.Layout.TryBounds(window, out var box))
            return Rejected(name, "error: No floating window found", allowsElevation);

        var area = WorkArea(desk, _area);
        var centered = box with
        {
            X = area.X + ((area.Width - box.Width) / 2),
            Y = area.Y + ((area.Height - box.Height) / 2),
        };
        return PlaceWindow(desk, window, centered, name, allowsElevation);
    }

    // alterzorder takes top or bottom and a window. Without a window it uses the active Client when that floats.
    private Outcome AlterZOrder(string arguments, bool bringToTop, bool allowsElevation)
    {
        var name = bringToTop ? "bringactivetotop" : "alterzorder";
        var (mode, selector) = SplitSelector(arguments);
        WindowId? window;
        if (bringToTop)
        {
            mode = "top";
            window = ActiveWindow();
        }
        else
        {
            window = selector.Trim().Length == 0 ? null : WindowSelector.Find(selector.Trim(), ActiveWindow(), EveryClient());
            if (window is null && ActiveWindow() is WindowId active && Find(active) is { Floating: true })
                window = active;
        }

        if (window is not WindowId chosen || Find(chosen) is null)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        mode = mode.Trim();
        if (mode is not ("top" or "bottom"))
            return Rejected(name, "error: Bad z-order position: " + mode, allowsElevation);

        return View([], dispatcher: name, allowsElevation: allowsElevation) with
        {
            ZOrder = [new ZOrderRequest(chosen, mode == "top")],
        };
    }

    // Corners count from the bottom left, counterclockwise: 0 bottom left, 1 bottom right, 2 top right, 3 top left.
    private Outcome MoveCursorToCorner(string arguments, bool allowsElevation)
    {
        const string name = "movecursortocorner";
        if (!int.TryParse(arguments.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var corner))
            return Rejected(name, "error: moveCursorToCorner, arg has to be a number", allowsElevation);

        if (ActiveWindow() is not WindowId window
            || WorkspaceContaining(window) is not LiveWorkspace desk
            || !desk.Layout.TryBounds(window, out var box))
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        if (corner is < 0 or > 3)
            return Rejected(name, "error: Corner must be 0 - 3", allowsElevation);

        var point = corner switch
        {
            0 => new Point(box.X, box.Y + box.Height),
            1 => new Point(box.X + box.Width, box.Y + box.Height),
            2 => new Point(box.X + box.Width, box.Y),
            _ => new Point(box.X, box.Y),
        };
        _requestedWarp = new WarpTo(point);
        return View([], dispatcher: name, allowsElevation: allowsElevation);
    }

    private WarpTo? TakeRequestedWarp()
    {
        var warp = _requestedWarp;
        _requestedWarp = null;
        return warp;
    }

    private Outcome PlaceWindow(LiveWorkspace desk, WindowId window, Rectangle bounds, string name, bool allowsElevation)
    {
        _mapPlaced.Remove(window);
        var covered = desk.Covered();
        var placements = desk.Layout.PlaceFloating(window, bounds);
        return IsShown(desk.Id)
            ? Present(desk, covered, placements, dispatcher: name, allowsElevation: allowsElevation)
            : View([], dispatcher: name, allowsElevation: allowsElevation);
    }

    private Size FocusedMonitorSize()
    {
        var bounds = FocusedMonitorBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
            bounds = _area;

        return new Size(bounds.Width, bounds.Height);
    }

    // The text before the first comma is the argument and the text after it the window.
    // Without a comma both are the whole text.
    private static (string Argument, string Selector) SplitSelector(string arguments)
    {
        var comma = arguments.IndexOf(',', StringComparison.Ordinal);
        return comma < 0 ? (arguments, arguments) : (arguments[..comma], arguments[(comma + 1)..]);
    }
}
