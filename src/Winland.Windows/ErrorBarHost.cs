using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

/// <summary>
/// Draws the <c>seterror</c> bar as a topmost layered window on its own thread, as the pinned release draws its error
/// overlay: a dark strip with a two-pixel border in the error's color and the message in light text. The bar takes no
/// clicks, and goes away with this process.
/// </summary>
internal static class ErrorBarHost
{
    private const string ClassName = "WinlandErrorBar";
    private const string Font = "Segoe UI";
    private const int ClassAlreadyExists = 1410;
    private const uint SyncMessage = 0x8000 + 0x48;
    private const uint QuitMessage = 0x0012;
    private const uint MouseActivateMessage = 0x0021;
    private const int NoActivate = 3;
    private const int BorderWidth = 2;

    private static readonly WNDPROC Proc = OnWindow;
    private static readonly Lock Gate = new();
    private static ErrorBar? _pending;
    private static ErrorBar? _shown;
    private static HWND _window;
    private static bool _posted;
    private static uint _thread;
    private static Thread? _worker;

    /// <summary>Shows <paramref name="bar"/>, or takes the bar away when it is null.</summary>
    internal static void Update(ErrorBar? bar)
    {
        uint thread;
        lock (Gate)
        {
            if (_worker is null && bar is null)
                return;

            _pending = bar;
            if (_worker is null)
            {
                _worker = new Thread(Run) { IsBackground = true, Name = "Winland error bar" };
                _worker.Start();
            }

            if (_posted || _thread == 0)
                return;

            _posted = true;
            thread = _thread;
        }

        _ = PInvoke.PostThreadMessage(thread, SyncMessage, default, default);
    }

    /// <summary>Removes the bar and ends its thread.</summary>
    internal static void Stop()
    {
        Thread? worker;
        uint thread;
        lock (Gate)
        {
            worker = _worker;
            thread = _thread;
            _worker = null;
            _thread = 0;
            _pending = null;
            _posted = false;
        }

        if (worker is null)
            return;

        if (thread != 0)
            _ = PInvoke.PostThreadMessage(thread, QuitMessage, default, default);

        _ = worker.Join(TimeSpan.FromSeconds(2));
    }

    private static void Run()
    {
        _ = PInvoke.PeekMessage(out _, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
        try
        {
            Register();
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return;
        }

        lock (Gate)
        {
            if (!ReferenceEquals(_worker, Thread.CurrentThread))
                return;

            _thread = PInvoke.GetCurrentThreadId();
            _posted = true;
            _ = PInvoke.PostThreadMessage(_thread, SyncMessage, default, default);
        }

        while (PInvoke.GetMessage(out var message, default, 0, 0) > 0)
        {
            try
            {
                if (message.hwnd.IsNull && message.message == SyncMessage)
                {
                    Sync();
                    continue;
                }

                PInvoke.TranslateMessage(in message);
                PInvoke.DispatchMessage(in message);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Console.Error.WriteLine(exception.Message);
            }
        }

        if (!_window.IsNull)
            _ = PInvoke.DestroyWindow(_window);

        _window = HWND.Null;
        _shown = null;
    }

    private static void Sync()
    {
        ErrorBar? wanted;
        lock (Gate)
        {
            wanted = _pending;
            _posted = false;
        }

        if (wanted is null)
        {
            if (!_window.IsNull)
                _ = PInvoke.DestroyWindow(_window);

            _window = HWND.Null;
            _shown = null;
            return;
        }

        if (_window.IsNull)
            _window = Create();

        if (_window.IsNull)
            return;

        if (wanted != _shown)
        {
            Paint(_window, wanted);
            _shown = wanted;
        }

        _ = PInvoke.SetWindowPos(
            _window,
            new HWND(-1),
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
    }

    private static unsafe void Paint(HWND hwnd, ErrorBar bar)
    {
        var width = bar.Bounds.Width;
        var height = bar.Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        var screen = PInvoke.GetDC(HWND.Null);
        var memory = PInvoke.CreateCompatibleDC(screen);
        var info = new BITMAPINFO();
        info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = width;
        info.bmiHeader.biHeight = -height;
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        void* bits;
        var bitmap = PInvoke.CreateDIBSection(memory, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
        if (bitmap.IsNull || bits is null)
        {
            _ = PInvoke.DeleteDC(memory);
            _ = PInvoke.ReleaseDC(HWND.Null, screen);
            return;
        }

        var old = PInvoke.SelectObject(memory, bitmap);
        var pixels = new Span<uint>(bits, width * height);

        // The pinned release's bar: the strip at 6% grey, and its border in the error's color.
        const uint Background = 0x000F0F0F;
        pixels.Fill(Background);
        DrawText(memory, bar, width, height);

        var radius = Math.Min(10, Math.Max(0, (height / 2) - 1));
        var border = Premultiplied(bar.Color);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                var inside = Inside(x, y, width, height, radius, 0);
                if (!inside)
                {
                    pixels[index] = 0;
                    continue;
                }

                pixels[index] = Inside(x, y, width, height, radius, BorderWidth)
                    ? 0xFF000000 | (pixels[index] & 0x00FFFFFF)
                    : Over(border, 0xFF000000 | Background);
            }
        }

        var size = new SIZE(width, height);
        var origin = new System.Drawing.Point(0, 0);
        var position = new System.Drawing.Point(bar.Bounds.X, bar.Bounds.Y);
        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        _ = PInvoke.UpdateLayeredWindow(hwnd, screen, &position, &size, memory, &origin, new COLORREF(0), &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);

        _ = PInvoke.SelectObject(memory, old);
        _ = PInvoke.DeleteObject(bitmap);
        _ = PInvoke.DeleteDC(memory);
        _ = PInvoke.ReleaseDC(HWND.Null, screen);
    }

    // The message in 90% grey, wrapped to the strip and cut at its bottom.
    private static unsafe void DrawText(HDC memory, ErrorBar bar, int width, int height)
    {
        _ = PInvoke.SetBkMode(memory, BACKGROUND_MODE.TRANSPARENT);
        using var font = GdiFonts.Create(bar.FontSize, 400, FONT_QUALITY.ANTIALIASED_QUALITY, Font);
        var previous = PInvoke.SelectObject(memory, font);
        _ = PInvoke.SetTextColor(memory, new COLORREF(0x00E6E6E6));
        var inset = BorderWidth + Math.Min(10, Math.Max(0, (height / 2) - 1));
        var box = new RECT(inset, 1, width - inset, height - 1);
        var text = bar.Text;
        fixed (char* chars = text)
        {
            _ = PInvoke.DrawText(memory, chars, text.Length, &box, DRAW_TEXT_FORMAT.DT_LEFT | DRAW_TEXT_FORMAT.DT_WORDBREAK | DRAW_TEXT_FORMAT.DT_NOPREFIX);
        }

        _ = PInvoke.SelectObject(memory, previous);
    }

    // Whether a pixel lies in the rounded rectangle shrunk by inset on every side.
    private static bool Inside(int x, int y, int width, int height, int radius, int inset)
    {
        var left = inset;
        var top = inset;
        var right = width - 1 - inset;
        var bottom = height - 1 - inset;
        if (x < left || x > right || y < top || y > bottom)
            return false;

        var r = Math.Max(0, radius - inset);
        var cx = x < left + r ? left + r : x > right - r ? right - r : x;
        var cy = y < top + r ? top + r : y > bottom - r ? bottom - r : y;
        var dx = x - cx;
        var dy = y - cy;
        return (dx * dx) + (dy * dy) <= r * r;
    }

    private static uint Premultiplied(BorderColor color)
    {
        uint alpha = color.A;
        return (alpha << 24)
            | ((uint)(color.R * alpha / 255) << 16)
            | ((uint)(color.G * alpha / 255) << 8)
            | (uint)(color.B * alpha / 255);
    }

    // A premultiplied color over an opaque one.
    private static uint Over(uint top, uint below)
    {
        var alpha = top >> 24;
        uint Channel(int shift) => ((top >> shift) & 0xFF) + ((((below >> shift) & 0xFF) * (255 - alpha)) / 255);
        return 0xFF000000 | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }

    private static LRESULT OnWindow(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam) =>
        message == MouseActivateMessage ? new LRESULT(NoActivate) : PInvoke.DefWindowProc(hwnd, message, wParam, lParam);

    private static unsafe void Register()
    {
        var module = PInvoke.GetModuleHandle((string?)null);
        var instance = (HINSTANCE)module.DangerousGetHandle();
        module.SetHandleAsInvalid();
        fixed (char* name = ClassName)
        {
            var info = new WNDCLASSEXW
            {
                cbSize = 80,
                lpfnWndProc = Proc,
                hInstance = instance,
                lpszClassName = name,
            };
            if (PInvoke.RegisterClassEx(in info) != 0)
                return;

            if (Marshal.GetLastPInvokeError() != ClassAlreadyExists)
                throw new InvalidOperationException($"The error bar window class was not registered ({Marshal.GetLastPInvokeError()}).");
        }
    }

    private static unsafe HWND Create() =>
        PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE
                | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TRANSPARENT,
            ClassName,
            "",
            WINDOW_STYLE.WS_POPUP,
            0,
            0,
            0,
            0,
            HWND.Null,
            null,
            null,
            null);
}
