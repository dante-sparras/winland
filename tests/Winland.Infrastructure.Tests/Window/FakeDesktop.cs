using System.Globalization;

namespace Winland.Infrastructure.Tests;

/// <summary>
/// The desktop the Windows adapter acts on, in memory. Every write lands in <see cref="Calls"/> in the order it was
/// made, whether it shows or hides a window, moves it, or changes its alpha, style or DWM attributes, so a test sees
/// what an outcome did and in what order. A window it has is visible, has an invisible frame of <see cref="Inset"/>
/// pixels on every side, takes a new size at once and has painted it <see cref="PaintTime"/> later.
/// </summary>
internal sealed class FakeDesktop(Func<double> clock) : IDesktop
{
    internal const int Inset = 8;

    private const uint LayeredStyle = 0x00080000;
    private const uint TopmostStyle = 0x00000008;
    private readonly Dictionary<WindowId, FakeWindow> _windows = [];

    internal List<string> Calls { get; } = [];

    /// <summary>Every stand-in a window was given, the one it has now last.</summary>
    internal List<FakeStandIn> StandIns { get; } = [];

    /// <summary>How long a window takes to paint a size it was given, in milliseconds.</summary>
    internal double PaintTime { get; set; } = 25;

    /// <summary>Whether <see cref="OpenStandIn"/> makes one. A desktop that cannot is the same as a window with no picture.</summary>
    internal bool CanStandIn { get; set; } = true;

    internal FakeWindow Add(long id, Rectangle visible)
    {
        var window = new FakeWindow { Visible = visible };
        _windows[new WindowId(id)] = window;
        return window;
    }

    internal FakeWindow this[long id] => _windows[new WindowId(id)];

    internal void Remove(long id) => _windows.Remove(new WindowId(id));

    internal FakeStandIn StandInOf(long id) => StandIns.Last(standIn => standIn.Window == new WindowId(id));

    internal static string Text(Rectangle rectangle) =>
        string.Create(CultureInfo.InvariantCulture, $"{rectangle.X},{rectangle.Y} {rectangle.Width}x{rectangle.Height}");

    internal void Log(string call) => Calls.Add(call);

    /// <summary>Where the first call that starts with <paramref name="prefix"/> is in <see cref="Calls"/>, or -1.</summary>
    internal int IndexOf(string prefix) => Calls.FindIndex(call => call.StartsWith(prefix, StringComparison.Ordinal));

    internal static string Id(WindowId window) => window.Value.ToString(CultureInfo.InvariantCulture);

    public bool TryRead(WindowId window, out WindowChrome chrome)
    {
        chrome = default;
        if (!_windows.TryGetValue(window, out var found))
            return false;

        chrome = new WindowChrome(found.ExStyle, false, found.Alpha);
        return true;
    }

    public bool TryWriteExStyle(WindowId window, uint exStyle)
    {
        if (!_windows.TryGetValue(window, out var found))
            return false;

        found.ExStyle = exStyle;
        Log($"exstyle {Id(window)} {exStyle:x}");
        return true;
    }

    // Like SetLayeredWindowAttributes, it fails for a window that is not layered.
    public bool TrySetAlpha(WindowId window, byte alpha)
    {
        if (!_windows.TryGetValue(window, out var found) || (found.ExStyle & LayeredStyle) == 0)
            return false;

        found.Alpha = alpha;
        Log($"alpha {Id(window)} {alpha}");
        return true;
    }

    public bool TryReadStyle(WindowId window, out uint style)
    {
        style = 0;
        if (!_windows.TryGetValue(window, out var found))
            return false;

        style = found.Style;
        return true;
    }

    public bool TryWriteStyle(WindowId window, uint style)
    {
        if (!_windows.TryGetValue(window, out var found))
            return false;

        found.Style = style;
        Log($"style {Id(window)} {style:x}");
        return true;
    }

    public bool TryReadBackdrop(WindowId window, out uint backdrop)
    {
        backdrop = 0;
        if (!_windows.TryGetValue(window, out var found))
            return false;

        backdrop = found.Backdrop;
        return true;
    }

    public bool TrySetBackdrop(WindowId window, uint backdrop)
    {
        if (!_windows.TryGetValue(window, out var found))
            return false;

        found.Backdrop = backdrop;
        Log($"backdrop {Id(window)} {backdrop}");
        return true;
    }

    public bool HasRegion(WindowId window) => _windows.TryGetValue(window, out var found) && found.Region;

    public bool TryFrame(WindowId window, out Rectangle outer, out Rectangle visible)
    {
        outer = default;
        visible = default;
        if (!_windows.TryGetValue(window, out var found))
            return false;

        outer = found.Outer;
        visible = found.Visible;
        return true;
    }

    public bool TrySetRegion(WindowId window, IReadOnlyList<Point> outline)
    {
        if (!_windows.TryGetValue(window, out var found))
            return false;

        found.Region = outline.Count > 0;
        Log($"region {Id(window)} {outline.Count}");
        return true;
    }

    public bool Exists(WindowId window) => _windows.ContainsKey(window);

    public bool Shown(WindowId window) => _windows.TryGetValue(window, out var found) && found.Shown;

    public bool Minimized(WindowId window) => _windows.TryGetValue(window, out var found) && found.Minimized;

    public bool Hung(WindowId window) => _windows.TryGetValue(window, out var found) && found.Hung;

    public int Dpi(WindowId window) => _windows.TryGetValue(window, out var found) ? found.Dpi : 96;

    public bool Repainting(WindowId window) => _windows.TryGetValue(window, out var found) && clock() < found.PaintedAt;

    public bool TryOuter(WindowId window, out Rectangle outer)
    {
        outer = default;
        if (!_windows.TryGetValue(window, out var found))
            return false;

        outer = found.Outer;
        return true;
    }

    public bool TryVisible(WindowId window, out Rectangle visible)
    {
        visible = default;
        if (!_windows.TryGetValue(window, out var found))
            return false;

        visible = found.Visible;
        return true;
    }

    public void SetAttribute(WindowId window, int attribute, uint value)
    {
        if (!_windows.TryGetValue(window, out var found))
            return;

        found.Attributes[attribute] = value;
        Log($"attr {Id(window)} {attribute}={value:x}");
    }

    public void SetBounds(WindowId window, Rectangle outer, Delivery delivery)
    {
        if (!_windows.TryGetValue(window, out var found))
            return;

        var grew = outer.Width != found.Outer.Width || outer.Height != found.Outer.Height;
        found.Visible = new Rectangle(outer.X + Inset, outer.Y + Inset, outer.Width - (2 * Inset), outer.Height - (2 * Inset));
        if (grew)
            found.PaintedAt = clock() + PaintTime;

        Log($"bounds {Id(window)} {Text(outer)} {delivery}");
    }

    public void Show(WindowId window, ShowAs show) => Change(window, show, "show");

    public void ShowAsync(WindowId window, ShowAs show) => Change(window, show, "show-async");

    public void Raise(WindowId window, bool top) => Log($"raise {Id(window)} {(top ? "top" : "bottom")}");

    public void RaiseAsync(WindowId window, bool top) => Log($"raise-async {Id(window)} {(top ? "top" : "bottom")}");

    public void Under(WindowId window, WindowId above) => Log($"under {Id(window)} {Id(above)}");

    public bool Painted(WindowId window) => true;

    public bool HoldOnTop(WindowId window)
    {
        if (!_windows.TryGetValue(window, out var found) || (found.ExStyle & TopmostStyle) != 0)
            return false;

        found.ExStyle |= TopmostStyle;
        Log($"hold {Id(window)}");
        return true;
    }

    public void LetGo(WindowId window)
    {
        if (_windows.TryGetValue(window, out var found))
            found.ExStyle &= ~TopmostStyle;

        Log($"let-go {Id(window)}");
    }

    public bool Foreground(WindowId window)
    {
        Log($"foreground {Id(window)}");
        return true;
    }

    public void Close(WindowId window) => Log($"close {Id(window)}");

    public void Pace()
    {
    }

    public IStandInSurface? OpenStandIn(WindowId window)
    {
        if (!CanStandIn || !_windows.ContainsKey(window))
            return null;

        Log($"standin open {Id(window)}");
        var standIn = new FakeStandIn(this, window);
        StandIns.Add(standIn);
        return standIn;
    }

    public void StopStandIns() => Log("standin stop");

    internal Size OuterSize(WindowId window) => _windows.TryGetValue(window, out var found) ? new Size(found.Outer.Width, found.Outer.Height) : default;

    private void Change(WindowId window, ShowAs show, string call)
    {
        if (!_windows.TryGetValue(window, out var found))
            return;

        found.Shown = show != ShowAs.Hide;
        found.Minimized = show == ShowAs.ShowMinimized;
        Log($"{call} {Id(window)} {show}");
    }
}

internal sealed class FakeWindow
{
    internal bool Shown { get; set; } = true;

    internal bool Minimized { get; set; }

    internal bool Hung { get; set; }

    internal int Dpi { get; set; } = 96;

    /// <summary>The frame the window shows. Its outer rectangle is <see cref="FakeDesktop.Inset"/> pixels wider on every side.</summary>
    internal Rectangle Visible { get; set; }

    internal Rectangle Outer => new(
        Visible.X - FakeDesktop.Inset,
        Visible.Y - FakeDesktop.Inset,
        Visible.Width + (2 * FakeDesktop.Inset),
        Visible.Height + (2 * FakeDesktop.Inset));

    // WS_VISIBLE | WS_OVERLAPPEDWINDOW | WS_CLIPSIBLINGS.
    internal uint Style { get; set; } = 0x14CF0000;

    internal uint ExStyle { get; set; }

    internal byte Alpha { get; set; } = 255;

    internal uint Backdrop { get; set; }

    internal bool Region { get; set; }

    /// <summary>When the window has painted the size it was last given, in the desktop's clock.</summary>
    internal double PaintedAt { get; set; }

    internal Dictionary<int, uint> Attributes { get; } = [];
}

/// <summary>A stand-in the adapter opened. It keeps the last piece of each slot it was given.</summary>
internal sealed class FakeStandIn(FakeDesktop desktop, WindowId window) : IStandInSurface
{
    internal WindowId Window { get; } = window;

    internal bool Closed { get; private set; }

    internal bool Hidden { get; private set; }

    internal Rectangle Placed { get; private set; }

    internal byte Alpha { get; private set; }

    internal StandInPiece? Picture { get; private set; }

    internal StandInPiece? Right { get; private set; }

    internal StandInPiece? Bottom { get; private set; }

    public nint Handle => 0;

    // A window takes its new size at once, so that is what a thumbnail of it measures.
    public Size? PictureSize() => desktop.OuterSize(Window);

    public void Place(Rectangle bounds)
    {
        Placed = bounds;
        Hidden = false;
        desktop.Log($"standin place {FakeDesktop.Id(Window)} {FakeDesktop.Text(bounds)}");
    }

    public void Show(StandInSlot slot, StandInPiece? piece, byte alpha)
    {
        Alpha = alpha;
        switch (slot)
        {
            case StandInSlot.Picture:
                Picture = piece;
                break;
            case StandInSlot.Right:
                Right = piece;
                break;
            default:
                Bottom = piece;
                break;
        }

        desktop.Log($"standin show {FakeDesktop.Id(Window)} {slot}");
    }

    public void SetAlpha(byte alpha)
    {
        Alpha = alpha;
        desktop.Log($"standin alpha {FakeDesktop.Id(Window)} {alpha}");
    }

    public void Hide()
    {
        Hidden = true;
        desktop.Log($"standin hide {FakeDesktop.Id(Window)}");
    }

    public void Close()
    {
        if (Closed)
            return;

        Closed = true;
        desktop.Log($"standin close {FakeDesktop.Id(Window)}");
    }
}
