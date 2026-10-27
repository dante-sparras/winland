using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

// Stryker disable once all : each member calls Windows on a real window.
internal sealed class Win32Desktop : IDesktop
{
    private const uint CloseMessage = 0x0010;

    // HWND_TOP raises a window above every window that is not topmost. HWND_BOTTOM lowers it below all of them.
    private static readonly HWND Top = new(0);
    private static readonly HWND Bottom = new(1);
    private static readonly HWND Topmost = new(-1);
    private static readonly HWND NotTopmost = new(-2);

    // A program that took this long to answer a call that waits on it is handled as hung for a while: its next
    // placements and z-order changes are posted, not waited for. One busy program then holds Winland up once, not on
    // every step until Windows calls it hung after five seconds.
    private const long SlowMilliseconds = 250;
    private const long SlowForMilliseconds = 3000;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<WindowId, long> _slowUntil = new();

    private readonly Win32WindowStyles _styles = new();
    private readonly Win32WindowFrame _frames = new();
    private readonly Win32DwmSurface _surface = new();

    public bool TryRead(WindowId window, out WindowChrome chrome) => _styles.TryRead(window, out chrome);

    public bool TryWriteExStyle(WindowId window, uint exStyle) => _styles.TryWriteExStyle(window, exStyle);

    public bool TrySetAlpha(WindowId window, byte alpha) => _styles.TrySetAlpha(window, alpha);

    public bool TryReadStyle(WindowId window, out uint style) => _frames.TryReadStyle(window, out style);

    public bool TryWriteStyle(WindowId window, uint style) => _frames.TryWriteStyle(window, style);

    public bool TryReadBackdrop(WindowId window, out uint backdrop) => _surface.TryReadBackdrop(window, out backdrop);

    public bool TrySetBackdrop(WindowId window, uint backdrop) => _surface.TrySetBackdrop(window, backdrop);

    public bool HasRegion(WindowId window) => _surface.HasRegion(window);

    public bool TryFrame(WindowId window, out Rectangle outer, out Rectangle visible) => _surface.TryFrame(window, out outer, out visible);

    public bool TrySetRegion(WindowId window, IReadOnlyList<Point> outline) => _surface.TrySetRegion(window, outline);

    public bool Exists(WindowId window) => PInvoke.IsWindow(Handle(window));

    public bool Shown(WindowId window) => PInvoke.IsWindowVisible(Handle(window));

    public bool Minimized(WindowId window) => PInvoke.IsIconic(Handle(window));

    public bool Hung(WindowId window)
    {
        if (PInvoke.IsHungAppWindow(Handle(window)))
            return true;

        if (!_slowUntil.TryGetValue(window, out var until))
            return false;

        if (Environment.TickCount64 < until)
            return true;

        _slowUntil.TryRemove(window, out _);
        return false;
    }

    private void Timed(WindowId window, Action call)
    {
        var start = Environment.TickCount64;
        call();
        if (Environment.TickCount64 - start >= SlowMilliseconds)
            _slowUntil[window] = Environment.TickCount64 + SlowForMilliseconds;
    }

    public int Dpi(WindowId window) => (int)PInvoke.GetDpiForWindow(Handle(window));

    public bool Repainting(WindowId window) => PInvoke.GetUpdateRect(Handle(window), out _, false);

    public bool TryOuter(WindowId window, out Rectangle outer)
    {
        outer = default;
        if (!PInvoke.GetWindowRect(Handle(window), out var rect))
            return false;

        outer = From(rect);
        return true;
    }

    public unsafe bool TryVisible(WindowId window, out Rectangle visible)
    {
        visible = default;
        Span<byte> value = stackalloc byte[sizeof(RECT)];
        var result = PInvoke.DwmGetWindowAttribute(Handle(window), DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, value);
        if (result.Failed)
            return false;

        visible = From(System.Runtime.InteropServices.MemoryMarshal.Read<RECT>(value));
        return true;
    }

    public unsafe void SetAttribute(WindowId window, int attribute, uint value) =>
        _ = PInvoke.DwmSetWindowAttribute(Handle(window), (DWMWINDOWATTRIBUTE)attribute, &value, sizeof(uint));

    public void SetBounds(WindowId window, Rectangle outer, Delivery delivery)
    {
        var flags = SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        if (delivery != Delivery.Wait)
            flags |= SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER | SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS;
        if (delivery == Delivery.PostKeepingSize)
            flags |= SET_WINDOW_POS_FLAGS.SWP_NOSIZE;

        if (delivery == Delivery.Wait)
            Timed(window, () => PInvoke.SetWindowPos(Handle(window), HWND.Null, outer.X, outer.Y, outer.Width, outer.Height, flags));
        else
            _ = PInvoke.SetWindowPos(Handle(window), HWND.Null, outer.X, outer.Y, outer.Width, outer.Height, flags);
    }

    public void Show(WindowId window, ShowAs show) => Timed(window, () => PInvoke.ShowWindow(Handle(window), Command(show)));

    public void ShowAsync(WindowId window, ShowAs show) => PInvoke.ShowWindowAsync(Handle(window), Command(show));

    public void Raise(WindowId window, bool top) => Timed(window, () => Restack(window, top, 0));

    public void RaiseAsync(WindowId window, bool top) => Restack(window, top, SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);

    public unsafe bool Painted(WindowId window) => !PInvoke.GetUpdateRect(Handle(window), null, false);

    public void Under(WindowId window, WindowId above) =>
        _ = PInvoke.SetWindowPos(
            Handle(window),
            Handle(above),
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    public bool HoldOnTop(WindowId window)
    {
        if ((PInvoke.GetWindowLong(Handle(window), WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) & (int)WINDOW_EX_STYLE.WS_EX_TOPMOST) != 0)
            return false;

        Band(window, Topmost);
        return true;
    }

    public void LetGo(WindowId window) => Band(window, NotTopmost);

    public bool Foreground(WindowId window) => Winland.Infrastructure.Foreground.MoveTo(window);

    public bool IsForeground(WindowId window) => PInvoke.GetForegroundWindow() == Handle(window);

    public void ForegroundDesktop() => Winland.Infrastructure.Foreground.ToDesktop();

    public void Close(WindowId window) => PInvoke.PostMessage(Handle(window), CloseMessage, default, default);

    public void Pace()
    {
        if (PresentClock.WaitForFrame())
            return;

        // DwmFlush returns after the next composition pass. Without composition it fails at once.
        if (PInvoke.DwmFlush().Failed)
            Thread.Sleep(15);
    }

    public IStandInSurface? OpenStandIn(WindowId window)
    {
        var hwnd = Handle(window);
        var overlay = StandInHost.Create();
        if (overlay.IsNull)
            return null;

        if (PInvoke.DwmRegisterThumbnail(overlay, hwnd, out var thumbnail).Failed)
        {
            StandInHost.Close(overlay);
            return null;
        }

        return new Win32StandInSurface(hwnd, overlay, thumbnail);
    }

    public void StopStandIns() => StandInHost.Stop();

    private static void Restack(WindowId window, bool top, SET_WINDOW_POS_FLAGS flags) =>
        _ = PInvoke.SetWindowPos(
            Handle(window),
            top ? Top : Bottom,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | flags);

    private static void Band(WindowId window, HWND after) =>
        _ = PInvoke.SetWindowPos(
            Handle(window),
            after,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    private static SHOW_WINDOW_CMD Command(ShowAs show) => show switch
    {
        ShowAs.Hide => SHOW_WINDOW_CMD.SW_HIDE,
        ShowAs.ShowMinimized => SHOW_WINDOW_CMD.SW_SHOWMINNOACTIVE,
        _ => SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE,
    };

    private static HWND Handle(WindowId window) => new((nint)window.Value);

    private static Rectangle From(RECT rect) =>
        new(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
}

// Stryker disable once all : each member calls Windows on a real window.
internal sealed class Win32StandInSurface(HWND window, HWND overlay, nint picture) : IStandInSurface
{
    // DWM_TNP_RECTDESTINATION, DWM_TNP_RECTSOURCE, DWM_TNP_OPACITY, DWM_TNP_VISIBLE, DWM_TNP_SOURCECLIENTAREAONLY.
    private const uint ThumbnailParts = 0x1 | 0x2 | 0x4 | 0x8 | 0x10;
    private const uint ThumbnailOpacity = 0x4;

    // The thumbnails that fill the area the window gained along its right and bottom edges, or 0 before one was needed.
    private nint _right;
    private nint _bottom;
    private bool _closed;

    public nint Handle => (nint)overlay;

    public Size? PictureSize() =>
        PInvoke.DwmQueryThumbnailSourceSize(picture, out var size).Succeeded ? new Size(size.Width, size.Height) : null;

    public void Place(Rectangle bounds)
    {
        // Just above the window, so whatever covers the window covers its stand-in too. A null handle is the top.
        // The border stacks on the stand-in, so a stand-in that is already next to the window stays put.
        var above = PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_HWNDPREV);
        var placed = above == overlay ? SET_WINDOW_POS_FLAGS.SWP_NOZORDER : 0;
        _ = PInvoke.SetWindowPos(
            overlay,
            above,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW | placed);
    }

    public void Show(StandInSlot slot, StandInPiece? piece, byte alpha)
    {
        // The thumbnails of the edges are made the first time they are needed.
        if (slot == StandInSlot.Right && piece is not null && _right == 0)
            _right = EdgeThumbnail();
        if (slot == StandInSlot.Bottom && piece is not null && _bottom == 0)
            _bottom = EdgeThumbnail();

        var thumbnail = slot switch
        {
            StandInSlot.Right => _right,
            StandInSlot.Bottom => _bottom,
            _ => picture,
        };
        if (thumbnail == 0)
            return;

        var source = piece?.Source ?? default;
        var destination = piece?.Destination ?? default;
        var properties = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = ThumbnailParts,
            rcDestination = new RECT(destination.X, destination.Y, destination.X + destination.Width, destination.Y + destination.Height),
            rcSource = new RECT(source.X, source.Y, source.X + source.Width, source.Y + source.Height),
            opacity = alpha,
            fVisible = piece is not null,
            fSourceClientAreaOnly = false,
        };
        _ = PInvoke.DwmUpdateThumbnailProperties(thumbnail, in properties);
    }

    public void SetAlpha(byte alpha)
    {
        var properties = new DWM_THUMBNAIL_PROPERTIES { dwFlags = ThumbnailOpacity, opacity = alpha };
        foreach (var thumbnail in (ReadOnlySpan<nint>)[picture, _right, _bottom])
        {
            if (thumbnail != 0)
                _ = PInvoke.DwmUpdateThumbnailProperties(thumbnail, in properties);
        }
    }

    public void Hide() => PInvoke.ShowWindow(overlay, SHOW_WINDOW_CMD.SW_HIDE);

    public void Close()
    {
        if (_closed)
            return;

        _closed = true;
        foreach (var thumbnail in (ReadOnlySpan<nint>)[picture, _right, _bottom])
        {
            if (thumbnail != 0)
                _ = PInvoke.DwmUnregisterThumbnail(thumbnail);
        }

        StandInHost.Close(overlay);
    }

    private nint EdgeThumbnail() =>
        PInvoke.DwmRegisterThumbnail(overlay, window, out var thumbnail).Succeeded ? thumbnail : 0;
}
