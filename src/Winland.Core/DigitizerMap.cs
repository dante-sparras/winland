namespace Winland;

/// <summary>One enabled monitor the pen or touch screen can map onto. Coordinates are physical pixels.</summary>
public readonly record struct DigitizerMonitor(string Name, string Description, int X, int Y, int Width, int Height);

/// <summary>
/// The pen and touch settings and the monitors, published for the mouse hook. The hook reads it with no lock.
/// </summary>
public sealed class DigitizerSpace
{
    public static DigitizerSpace Empty { get; } = new(DigitizerOptions.Documented, [], "");

    public DigitizerSpace(DigitizerOptions options, DigitizerMonitor[] monitors, string focused)
    {
        Options = options;
        Monitors = monitors;
        Focused = focused;
    }

    public DigitizerOptions Options { get; }

    public DigitizerMonitor[] Monitors { get; }

    /// <summary>The name of the focused enabled monitor, or empty when none is focused.</summary>
    public string Focused { get; }

    public bool Same(DigitizerSpace other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!Options.Equals(other.Options) || !string.Equals(Focused, other.Focused, StringComparison.Ordinal) || Monitors.Length != other.Monitors.Length)
            return false;

        for (var i = 0; i < Monitors.Length; i++)
        {
            if (Monitors[i] != other.Monitors[i])
                return false;
        }

        return true;
    }
}

/// <summary>Where Windows maps one pen or touch screen, and the tablet's size in millimetres. A zero size skips the active area.</summary>
public readonly record struct DigitizerSample(Rectangle Display, double WidthMm, double HeightMm);

public enum DigitizerKind
{
    Mouse,
    Pen,
    Touch,

    /// <summary>Input Winland sent, or anything that is not a pen, a touch screen or the mouse. It does not reset a stroke.</summary>
    Other,
}

/// <summary>
/// What to do with one pointer event. <see cref="Take"/> swallows it. <see cref="Report"/> tells the core, and a button
/// is sent again. A disabled pen or touch screen is taken and not reported, and the cursor goes back to the mouse.
/// </summary>
public readonly record struct DigitizerOutcome(bool Take, int X, int Y, bool Report)
{
    /// <summary>The event already has the right position. Leave it to Windows.</summary>
    public static DigitizerOutcome Leave { get; } = default;
}

/// <summary>
/// Maps a pen and a touch screen the way the pinned release does. The point it starts from is where Windows already
/// put the pen or the finger, not the device's own report. <c>input:tablet:relative_input</c> then moves by the change
/// in that mapped pixel, because a mouse hook has no device units.
/// </summary>
public static class DigitizerMap
{
    public static bool TouchUnbound(string output) =>
        output.Length == 0 || output == "[[Auto]]" || output == "[[EMPTY]]";

    public static bool TabletUnbound(string output) => output.Length == 0 || output == "[[EMPTY]]";

    /// <summary>The calibration, then <paramref name="leftHanded"/> as a half turn. 0 with it off is the position unchanged.</summary>
    public static (double X, double Y) Calibrate(double x, double y, int transform, bool leftHanded)
    {
        var turned = transform switch
        {
            1 => (1 - y, x),
            2 => (1 - x, 1 - y),
            3 => (y, 1 - x),
            4 => (1 - x, y),
            5 => (y, x),
            6 => (x, 1 - y),
            _ => (x, y),
        };
        return leftHanded ? (1 - turned.Item1, 1 - turned.Item2) : turned;
    }

    /// <summary>
    /// The active area, in the position <see cref="Calibrate"/> already turned. An odd transform swaps the tablet's
    /// axes, because that turn did. A size that is not both above 0, or a tablet Windows gave no size for, is skipped.
    /// </summary>
    public static (double X, double Y) Active(double x, double y, Vec2 position, Vec2 size, double widthMm, double heightMm, int transform)
    {
        if (size.X <= 0 || size.Y <= 0 || widthMm <= 0 || heightMm <= 0)
            return (x, y);

        var width = widthMm;
        var height = heightMm;
        if (transform % 2 != 0)
            (width, height) = (height, width);

        var left = position.X / width;
        var top = position.Y / height;
        var right = (position.X + size.X) / width;
        var bottom = (position.Y + size.Y) / height;
        var nx = right == left ? x : (x - left) / (right - left);
        var ny = bottom == top ? y : (y - top) / (bottom - top);
        return (nx, ny);
    }

    /// <summary>The monitor <paramref name="output"/> names, or null when it names none. A direction is not a name.</summary>
    public static DigitizerMonitor? Find(string output, DigitizerMonitor[] monitors, string focused)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        var name = output.Trim();
        if (name.Equals("current", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var monitor in monitors)
            {
                if (monitor.Name.Equals(focused, StringComparison.Ordinal))
                    return monitor;
            }

            return null;
        }

        if (name.StartsWith("desc:", StringComparison.OrdinalIgnoreCase))
        {
            var description = name[5..].Trim();
            foreach (var monitor in monitors)
            {
                if (monitor.Description.StartsWith(description, StringComparison.Ordinal))
                    return monitor;
            }

            return null;
        }

        foreach (var monitor in monitors)
        {
            if (monitor.Name.Equals(name, StringComparison.Ordinal))
                return monitor;
        }

        foreach (var monitor in monitors)
        {
            if (monitor.Description.Length > 0 && monitor.Description.Equals(name, StringComparison.Ordinal))
                return monitor;
        }

        return null;
    }

    public static Rectangle? Union(DigitizerMonitor[] monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Length == 0)
            return null;

        var left = monitors[0].X;
        var top = monitors[0].Y;
        var right = monitors[0].X + monitors[0].Width;
        var bottom = monitors[0].Y + monitors[0].Height;
        for (var i = 1; i < monitors.Length; i++)
        {
            var monitor = monitors[i];
            left = Math.Min(left, monitor.X);
            top = Math.Min(top, monitor.Y);
            right = Math.Max(right, monitor.X + monitor.Width);
            bottom = Math.Max(bottom, monitor.Y + monitor.Height);
        }

        var width = right - left;
        var height = bottom - top;
        return width > 0 && height > 0 ? new Rectangle(left, top, width, height) : null;
    }

    /// <summary>
    /// The monitor a touch screen is bound to, or null when the output keeps the display Windows chose. An unknown
    /// name is every monitor.
    /// </summary>
    public static Rectangle? TouchTarget(TouchSettings touch, DigitizerMonitor[] monitors, string focused)
    {
        ArgumentNullException.ThrowIfNull(touch);
        if (TouchUnbound(touch.Output))
            return null;

        if (Find(touch.Output, monitors, focused) is DigitizerMonitor monitor)
            return Box(monitor);

        return Union(monitors);
    }

    /// <summary>The rectangle a pen maps onto, after its region. Empty when there is no monitor to map onto.</summary>
    public static Rectangle TabletArea(TabletSettings tablet, DigitizerMonitor[] monitors, string focused)
    {
        ArgumentNullException.ThrowIfNull(tablet);
        var named = !TabletUnbound(tablet.Output);
        Rectangle layout;
        if (named && Find(tablet.Output, monitors, focused) is DigitizerMonitor monitor)
            layout = Box(monitor);
        else if (Union(monitors) is Rectangle union)
            layout = union;
        else
            return default;

        var originX = layout.X;
        var originY = layout.Y;
        if (!named && tablet.AbsoluteRegion)
        {
            originX = Pixel(tablet.RegionPosition.X);
            originY = Pixel(tablet.RegionPosition.Y);
        }
        else
        {
            originX += Pixel(tablet.RegionPosition.X);
            originY += Pixel(tablet.RegionPosition.Y);
        }

        var width = layout.Width;
        var height = layout.Height;
        if (tablet.RegionSize.X > 0 && tablet.RegionSize.Y > 0)
        {
            width = Pixel(tablet.RegionSize.X);
            height = Pixel(tablet.RegionSize.Y);
        }

        return new Rectangle(originX, originY, width, height);
    }

    /// <summary>The fraction of <paramref name="sample"/>'s display, or of the monitor under the point when Windows did not say.</summary>
    public static bool TryUnit(DigitizerSample? sample, DigitizerMonitor[] monitors, int x, int y, out double nx, out double ny)
    {
        Rectangle? area = null;
        if (sample is DigitizerSample known && known.Display.Width > 0 && known.Display.Height > 0)
            area = known.Display;
        else if (MonitorAt(monitors, x, y) is DigitizerMonitor monitor)
            area = Box(monitor);

        if (area is not Rectangle box || box.Width <= 0 || box.Height <= 0)
        {
            nx = 0;
            ny = 0;
            return false;
        }

        nx = (x - box.X) / (double)box.Width;
        ny = (y - box.Y) / (double)box.Height;
        return true;
    }

    public static (int X, int Y) Place(double x, double y, Rectangle area) =>
        (area.X + Pixel(x * area.Width), area.Y + Pixel(y * area.Height));

    private static DigitizerMonitor? MonitorAt(DigitizerMonitor[] monitors, int x, int y)
    {
        foreach (var monitor in monitors)
        {
            if (monitor.Width > 0 && monitor.Height > 0 && x >= monitor.X && y >= monitor.Y && x < monitor.X + monitor.Width && y < monitor.Y + monitor.Height)
                return monitor;
        }

        return null;
    }

    private static Rectangle Box(DigitizerMonitor monitor) => new(monitor.X, monitor.Y, monitor.Width, monitor.Height);

    private static int Pixel(double value)
    {
        if (!double.IsFinite(value))
            return 0;

        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded >= int.MaxValue)
            return int.MaxValue;
        if (rounded <= int.MinValue)
            return int.MinValue;
        return (int)rounded;
    }
}

/// <summary>
/// Where the cursor should go for one pen or touch event. A mouse event remembers where to put the cursor back.
/// Input Winland sent does not, so a cursor it just placed does not become that place.
/// </summary>
public sealed class DigitizerCursor
{
    private int _mouseX;
    private int _mouseY;
    private bool _haveMouse;
    private int _cursorX;
    private int _cursorY;
    private bool _haveCursor;
    private int _mappedX;
    private int _mappedY;
    private bool _haveMapped;
    private DigitizerKind _stroke = DigitizerKind.Mouse;

    /// <summary>The mouse moved or clicked. The next relative pen stroke starts over, and a disabled pen returns here.</summary>
    public void Note(int x, int y)
    {
        _mouseX = x;
        _mouseY = y;
        _haveMouse = true;
        Remember(x, y);
        _haveMapped = false;
        _stroke = DigitizerKind.Mouse;
    }

    public DigitizerOutcome Move(DigitizerKind kind, DigitizerSpace space, DigitizerSample? sample, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(space);
        if (kind == DigitizerKind.Mouse)
        {
            Note(x, y);
            return DigitizerOutcome.Leave;
        }

        if (kind is not (DigitizerKind.Pen or DigitizerKind.Touch))
            return DigitizerOutcome.Leave;

        if (_stroke != kind)
        {
            _haveMapped = false;
            _stroke = kind;
        }

        if (!Enabled(kind, space))
        {
            _haveMapped = false;
            return Restore(x, y);
        }

        if (!Affects(kind, space, sample))
            return DigitizerOutcome.Leave;

        if (!TryPlace(kind, space, sample, x, y, out var px, out var py))
            return DigitizerOutcome.Leave;

        if (kind == DigitizerKind.Pen && space.Options.Tablet.Relative)
            return Relative(px, py, x, y);

        if (px == x && py == y)
            return DigitizerOutcome.Leave;

        Remember(px, py);
        return new DigitizerOutcome(true, px, py, true);
    }

    /// <summary>A button. Relative mode keeps the cursor the moves left, and the click is sent again only when it is elsewhere.</summary>
    public DigitizerOutcome Button(DigitizerKind kind, DigitizerSpace space, DigitizerSample? sample, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(space);
        if (kind is not (DigitizerKind.Pen or DigitizerKind.Touch))
            return DigitizerOutcome.Leave;

        if (!Enabled(kind, space))
            return Restore(x, y);

        if (kind == DigitizerKind.Pen && space.Options.Tablet.Relative)
        {
            var cx = _haveCursor ? _cursorX : x;
            var cy = _haveCursor ? _cursorY : y;
            return cx == x && cy == y ? DigitizerOutcome.Leave : new DigitizerOutcome(true, cx, cy, true);
        }

        if (!Affects(kind, space, sample) || !TryPlace(kind, space, sample, x, y, out var px, out var py) || (px == x && py == y))
            return DigitizerOutcome.Leave;

        return new DigitizerOutcome(true, px, py, true);
    }

    private DigitizerOutcome Relative(int mappedX, int mappedY, int eventX, int eventY)
    {
        if (!_haveMapped)
        {
            _haveMapped = true;
            _mappedX = mappedX;
            _mappedY = mappedY;
            if (mappedX == eventX && mappedY == eventY)
            {
                Remember(eventX, eventY);
                return DigitizerOutcome.Leave;
            }

            if (!_haveCursor)
                Remember(_haveMouse ? _mouseX : eventX, _haveMouse ? _mouseY : eventY);
            return new DigitizerOutcome(true, _cursorX, _cursorY, true);
        }

        Remember(_cursorX + (mappedX - _mappedX), _cursorY + (mappedY - _mappedY));
        _mappedX = mappedX;
        _mappedY = mappedY;
        return new DigitizerOutcome(true, _cursorX, _cursorY, true);
    }

    private DigitizerOutcome Restore(int x, int y)
    {
        var rx = _haveMouse ? _mouseX : x;
        var ry = _haveMouse ? _mouseY : y;
        return new DigitizerOutcome(true, rx, ry, false);
    }

    private void Remember(int x, int y)
    {
        _cursorX = x;
        _cursorY = y;
        _haveCursor = true;
    }

    private static bool Enabled(DigitizerKind kind, DigitizerSpace space) =>
        kind == DigitizerKind.Pen ? space.Options.Tablet.Enabled : space.Options.Touch.Enabled;

    // Anything but the documented mapping. An absolute origin of 0, 0 still counts: the layout may not start at 0, 0.
    private static bool Affects(DigitizerKind kind, DigitizerSpace space, DigitizerSample? sample)
    {
        if (kind == DigitizerKind.Touch)
        {
            var touch = space.Options.Touch;
            return touch.Transform != 0 || !DigitizerMap.TouchUnbound(touch.Output);
        }

        var tablet = space.Options.Tablet;
        if (tablet.Transform != 0 || tablet.LeftHanded || tablet.Relative || tablet.AbsoluteRegion || !DigitizerMap.TabletUnbound(tablet.Output))
            return true;
        if (tablet.RegionPosition.X != 0 || tablet.RegionPosition.Y != 0)
            return true;
        if (tablet.RegionSize.X > 0 && tablet.RegionSize.Y > 0)
            return true;
        return tablet.ActiveAreaSize.X > 0 && tablet.ActiveAreaSize.Y > 0 && sample is { WidthMm: > 0, HeightMm: > 0 };
    }

    private static bool TryPlace(DigitizerKind kind, DigitizerSpace space, DigitizerSample? sample, int x, int y, out int px, out int py)
    {
        px = x;
        py = y;
        if (!DigitizerMap.TryUnit(sample, space.Monitors, x, y, out var nx, out var ny))
            return false;

        if (kind == DigitizerKind.Pen)
        {
            var tablet = space.Options.Tablet;
            (nx, ny) = DigitizerMap.Calibrate(nx, ny, tablet.Transform, tablet.LeftHanded);
            var width = sample?.WidthMm ?? 0;
            var height = sample?.HeightMm ?? 0;
            (nx, ny) = DigitizerMap.Active(nx, ny, tablet.ActiveAreaPosition, tablet.ActiveAreaSize, width, height, tablet.Transform);
        }
        else
        {
            (nx, ny) = DigitizerMap.Calibrate(nx, ny, space.Options.Touch.Transform, false);
        }

        if (!double.IsFinite(nx) || !double.IsFinite(ny))
            return false;

        nx = Math.Clamp(nx, 0, 1);
        ny = Math.Clamp(ny, 0, 1);
        if (!Target(kind, space, sample, x, y, out var area) || area.Width <= 0 || area.Height <= 0)
            return false;

        (px, py) = DigitizerMap.Place(nx, ny, area);
        return true;
    }

    private static bool Target(DigitizerKind kind, DigitizerSpace space, DigitizerSample? sample, int x, int y, out Rectangle area)
    {
        if (kind == DigitizerKind.Pen)
        {
            area = DigitizerMap.TabletArea(space.Options.Tablet, space.Monitors, space.Focused);
            return area.Width > 0 && area.Height > 0;
        }

        if (DigitizerMap.TouchTarget(space.Options.Touch, space.Monitors, space.Focused) is Rectangle bound)
        {
            area = bound;
            return true;
        }

        // Unbound touch keeps Windows' display. A transform turns the finger inside it, or inside the monitor under it.
        if (sample is DigitizerSample known && known.Display.Width > 0 && known.Display.Height > 0)
        {
            area = known.Display;
            return true;
        }

        foreach (var monitor in space.Monitors)
        {
            if (monitor.Width > 0 && monitor.Height > 0 && x >= monitor.X && y >= monitor.Y && x < monitor.X + monitor.Width && y < monitor.Y + monitor.Height)
            {
                area = new Rectangle(monitor.X, monitor.Y, monitor.Width, monitor.Height);
                return true;
            }
        }

        area = default;
        return false;
    }
}
