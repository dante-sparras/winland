using System.Globalization;

namespace Winland.Application;

/// <summary>Dispatchers that move, resize and restack by pixels: <c>movewindowpixel</c>, <c>resizewindowpixel</c>, <c>centerwindow</c>, <c>alterzorder</c>, <c>movecursor</c> and <c>movecursortocorner</c>.</summary>
public sealed partial class WindowManager
{
    private WarpTo? _requestedWarp;

    // movewindowpixel moves a floating Client. A tiled Client is left where it is.
    private Outcome MoveWindowPixel(string arguments)
    {
        const string name = "movewindowpixel";
        var (vector, selector) = SplitSelector(arguments);
        if (Select(selector, emptyMeansActive: false) is not WindowId window
            || Find(window) is not Client client
            || WorkspaceContaining(window) is not LiveWorkspace desk)
            return Rejected(name, "error: moveWindow: no window");

        if (client.Fullscreen != FullscreenMode.None)
            return Rejected(name, "error: Window is fullscreen");

        if (!client.Floating || !desk.Layout.TryBounds(window, out var box))
            return View([], dispatcher: name);

        var to = WindowVector.Resolve(vector, new Point(box.X, box.Y), FocusedMonitorSize());
        return PlaceWindow(desk, window, box with { X = to.X, Y = to.Y }, name);
    }

    // resizewindowpixel resizes through the Layout, so a tiled Client resizes its tile.
    private Outcome ResizeWindowPixel(string arguments)
    {
        const string name = "resizewindowpixel";
        var (vector, selector) = SplitSelector(arguments);
        if (Select(selector, emptyMeansActive: false) is not WindowId window
            || Find(window) is not Client client
            || WorkspaceContaining(window) is not LiveWorkspace desk
            || !desk.Layout.TryBounds(window, out var box))
            return Rejected(name, "error: resizeWindow: no window");

        var size = WindowVector.Resolve(vector, new Point(box.Width, box.Height), FocusedMonitorSize());
        if (size.X < 1 || size.Y < 1)
            return Rejected(name, "error: Invalid size");

        if (client.Fullscreen != FullscreenMode.None)
            return Rejected(name, "error: Window is fullscreen");

        _mapPlaced.Remove(window);
        var covered = desk.Covered();
        var delta = string.Create(CultureInfo.InvariantCulture, $"{size.X - box.Width} {size.Y - box.Height}");
        var placements = desk.Layout.ResizeActive(window, delta);
        return IsShown(desk.Id)
            ? Present(desk, covered, placements, dispatcher: name)
            : View([], dispatcher: name);
    }

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

    // alterzorder takes top or bottom and a window. Without a window it uses the active Client when that floats.
    private Outcome AlterZOrder(string arguments, bool bringToTop)
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
            return View([], dispatcher: name);

        mode = mode.Trim();
        if (mode is not ("top" or "bottom"))
            return Rejected(name, "error: Bad z-order position: " + mode);

        Stack(chosen, top: mode == "top");
        SetOverFullscreen(chosen, mode == "top");
        var stacked = View([], dispatcher: name) with
        {
            ZOrder = [new ZOrderRequest(chosen, mode == "top")],
        };
        if (_pointer is not Point at)
            return stacked;

        // Winland simulates a pointer move after it raises or lowers a window, a pixel off so it counts as a
        // move, so with input:follow_mouse the window now under the pointer takes the focus.
        _pointer = at with { X = at.X - 1, Y = at.Y - 1 };
        var moved = MovePointer(new PointerMoved(at));
        return Merge(stacked, moved) with { Dispatcher = name };
    }

    // movecursor X Y: puts the cursor at a point on the desktop and handles it as a pointer move, so focus follows it as
    // it would follow the mouse, as Winland warps the pointer and simulates a mouse movement.
    private Outcome MoveCursor(string arguments)
    {
        const string name = "movecursor";
        var parts = arguments.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y))
            return Rejected(name, "error: movecursor takes two coordinates");

        var point = new Point(x, y);
        var moved = MovePointer(new PointerMoved(point));
        _pointer = point;
        _requestedWarp = new WarpTo(point);
        return moved with { Dispatcher = name };
    }

    // Corners count from the bottom left, counterclockwise: 0 bottom left, 1 bottom right, 2 top right, 3 top left.
    private Outcome MoveCursorToCorner(string arguments)
    {
        const string name = "movecursortocorner";
        if (!int.TryParse(arguments.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var corner))
            return Rejected(name, "error: moveCursorToCorner, arg has to be a number");

        if (ActiveWindow() is not WindowId window
            || WorkspaceContaining(window) is not LiveWorkspace desk
            || !desk.Layout.TryBounds(window, out var box))
            return View([], dispatcher: name);

        if (corner is < 0 or > 3)
            return Rejected(name, "error: Corner must be 0 - 3");

        var point = corner switch
        {
            0 => new Point(box.X, box.Y + box.Height),
            1 => new Point(box.X + box.Width, box.Y + box.Height),
            2 => new Point(box.X + box.Width, box.Y),
            _ => new Point(box.X, box.Y),
        };
        _requestedWarp = new WarpTo(point);
        return View([], dispatcher: name);
    }

    private WarpTo? TakeRequestedWarp()
    {
        var warp = _requestedWarp;
        _requestedWarp = null;
        return warp;
    }

    private Outcome PlaceWindow(LiveWorkspace desk, WindowId window, Rectangle bounds, string name)
    {
        _mapPlaced.Remove(window);
        var covered = desk.Covered();
        var placements = desk.Layout.PlaceFloating(window, bounds);
        return IsShown(desk.Id)
            ? Present(desk, covered, placements, dispatcher: name)
            : View([], dispatcher: name);
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
