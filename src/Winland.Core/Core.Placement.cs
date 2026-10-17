namespace Winland;

public sealed partial class Core
{
    // A window that resizes itself this soon after it opens is put back where it was placed, once.
    // Electron windows restore their saved bounds just after they are shown.
    internal const long ReapplyMilliseconds = 2000;

    private const string NotCloseable = "error: can't close window, it's not closeable yet (noclosefor)";
    private const int DefaultFloatWidth = 640;
    private const int DefaultFloatHeight = 400;

    private readonly Dictionary<WindowId, MapPlaced> _mapPlaced = [];
    private readonly Dictionary<WindowId, long> _closeableAt = [];

    /// <summary>The clock for <c>no_close_for</c> and the reapply after map, in milliseconds.</summary>
    internal Func<long>? RuleClock { get; set; }

    /// <summary>True while a Client's first resize of itself would be undone. Lets the adapter skip other moves.</summary>
    public bool AwaitsFirstResize(WindowId window)
    {
        lock (_gate)
            return _mapPlaced.TryGetValue(window, out var placed) && placed.Until >= RuleNow();
    }

    private long RuleNow() => RuleClock?.Invoke() ?? Environment.TickCount64;

    // The monitor rule picks the Monitor before the workspace rule picks the Workspace.
    // "unset" keeps the Monitor the window opened on. Without "silent" the chosen Monitor is focused.
    private LiveMonitor? RuleMonitor(string? text, LiveMonitor? opened)
    {
        if (text is null || _monitors.Count == 0)
            return opened;

        var selector = text.Trim();
        if (selector.Length == 0 || selector == "unset")
            return opened;

        var space = selector.LastIndexOf(' ');
        var silent = space >= 0 && selector[space..].Contains("silent", StringComparison.Ordinal);
        if (silent)
            selector = selector[..space].Trim();

        if (MonitorOf(MonitorSelector.Resolve(selector, Targets(), TargetOf(FocusedMonitor()))) is not LiveMonitor chosen)
            return opened;

        if (!silent && chosen.Id != FocusedId())
            Focus(chosen.Id);

        return chosen;
    }

    /// <summary>
    /// The rectangle a new Client opens at. A floating Client is centered in the floating work area
    /// unless a <c>move</c> rule places it. <c>size</c> sets its size, which a tiled Client keeps as its pseudo size.
    /// Expressions are in pixels and monitor-local.
    /// </summary>
    private Rectangle PlaceOnMap(WindowAppeared appeared, WindowDecision decision, LiveWorkspace desk, Rectangle area)
    {
        var placement = decision.Placement;
        var monitor = MonitorById(desk.Monitor) is { Effective.Enabled: true } screen ? screen.Bounds : area;
        var desired = appeared.Bounds;
        var sized = desired.Width > 2 && desired.Height > 2;
        var width = sized || !decision.Floating ? desired.Width : DefaultFloatWidth;
        var height = sized || !decision.Floating ? desired.Height : DefaultFloatHeight;
        var pointer = appeared.Pointer ?? Cursor?.Read() ?? new Point(monitor.X + (monitor.Width / 2), monitor.Y + (monitor.Height / 2));
        if (placement.Size is { } size
            && size.TryEvaluate(Variables(monitor, desired with { Width = width, Height = height }, pointer), out var sizedWidth, out var sizedHeight)
            && sizedWidth >= 1
            && sizedHeight >= 1)
        {
            width = Pixels(sizedWidth);
            height = Pixels(sizedHeight);
        }

        (width, height) = Clamp(width, height, decision.MinSize, decision.MaxSize);
        Rectangle placed;
        if (placement.Move is { } move
            && move.TryEvaluate(Variables(monitor, desired with { Width = width, Height = height }, pointer), out var x, out var y))
        {
            placed = new Rectangle(monitor.X + Pixels(x), monitor.Y + Pixels(y), width, height);
        }
        else
        {
            var floating = desk.Options.FloatingWorkArea(area);
            placed = new Rectangle(
                floating.X + ((floating.Width - width) / 2),
                floating.Y + ((floating.Height - height) / 2),
                width,
                height);
        }

        // misc:new_float_force_onscreen keeps a Client that opens floating on screen, a moved one too.
        return decision.Floating ? desk.Options.NewFloatingOnScreen(area).Keep(placed) : placed;
    }

    private void NotePlaced(WindowId window, LiveWorkspace desk, WindowDecision decision, bool restored)
    {
        var now = RuleNow();
        if (decision.Placement.NoCloseFor > 0)
            _closeableAt[window] = now + decision.Placement.NoCloseFor;

        if (!restored && desk.ClientOf(window) is { Floating: true, Fullscreen: FullscreenMode.None } && desk.Layout.TryBounds(window, out var bounds))
            _mapPlaced[window] = new MapPlaced(bounds, now + ReapplyMilliseconds);
    }

    private bool Closeable(WindowId window) =>
        !_closeableAt.TryGetValue(window, out var at) || RuleNow() >= at;

    // Puts a floating Client back at its rule placement the first time it changes its own size.
    private Outcome Reapply(WindowResized resized)
    {
        if (!_mapPlaced.TryGetValue(resized.Window, out var placed))
            return View([]);

        if (RuleNow() > placed.Until)
        {
            _mapPlaced.Remove(resized.Window);
            return View([]);
        }

        if (resized.Bounds.Width == placed.Bounds.Width && resized.Bounds.Height == placed.Bounds.Height)
            return View([]);

        _mapPlaced.Remove(resized.Window);
        if (WorkspaceContaining(resized.Window) is not LiveWorkspace desk || desk.ClientOf(resized.Window) is not { Floating: true })
            return View([]);

        var covered = desk.Covered();
        var placements = desk.Layout.PlaceFloating(resized.Window, placed.Bounds);
        return IsShown(desk.Id) ? Present(desk, covered, placements) : View([]);
    }

    private static Dictionary<string, double> Variables(Rectangle monitor, Rectangle window, Point cursor) => new(StringComparer.Ordinal)
    {
        ["monitor_w"] = monitor.Width,
        ["monitor_h"] = monitor.Height,
        ["window_x"] = window.X - monitor.X,
        ["window_y"] = window.Y - monitor.Y,
        ["window_w"] = window.Width,
        ["window_h"] = window.Height,
        ["cursor_x"] = cursor.X - monitor.X,
        ["cursor_y"] = cursor.Y - monitor.Y,
    };

    private static int Pixels(double value) =>
        (int)Math.Round(Math.Clamp(value, int.MinValue / 2, int.MaxValue / 2), MidpointRounding.AwayFromZero);

    private static (int Width, int Height) Clamp(int width, int height, Size? min, Size? max)
    {
        if (max is { } hi)
        {
            if (hi.Width < int.MaxValue && width > hi.Width)
                width = hi.Width;
            if (hi.Height < int.MaxValue && height > hi.Height)
                height = hi.Height;
        }

        if (min is { } lo)
        {
            if (lo.Width > 0 && width < lo.Width)
                width = lo.Width;
            if (lo.Height > 0 && height < lo.Height)
                height = lo.Height;
        }

        return (width, height);
    }

    private readonly record struct MapPlaced(Rectangle Bounds, long Until);
}
