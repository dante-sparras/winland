using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

/// <summary>
/// The click-through window that shows a frame of a window's effects. It is a layered window, so each frame is drawn
/// into a bitmap with the alpha of its pixels and handed over together with the frame's position and size.
/// </summary>
internal sealed unsafe class EffectLayer : IDisposable
{
    private readonly HWND _window;
    private HDC _memory;
    private HBITMAP _bitmap;
    private HGDIOBJ _previous;
    private void* _bits;
    private int _width;
    private int _height;
    private bool _shown;

    private EffectLayer(HWND window) => _window = window;

    internal HWND Window => _window;

    /// <summary>A new hidden layer, or null when Windows made none.</summary>
    internal static EffectLayer? Create()
    {
        var window = StandInHost.CreateLayer();
        return window.IsNull ? null : new EffectLayer(window);
    }

    /// <summary>
    /// Shows <paramref name="rows"/>, <paramref name="width"/> by <paramref name="height"/> premultiplied pixels, with their top
    /// left at <paramref name="at"/> on the screen and everything at <paramref name="alpha"/>.
    /// </summary>
    internal bool Present(byte* rows, int pitch, int width, int height, System.Drawing.Point at, byte alpha, HWND above)
    {
        if (!Fit(width, height))
            return false;

        var copied = (long)width * 4;
        var target = (byte*)_bits;
        for (var y = 0; y < height; y++)
            Buffer.MemoryCopy(rows + (long)y * pitch, target + y * copied, copied, copied);

        var screen = PInvoke.GetDC(HWND.Null);
        try
        {
            var size = new SIZE(width, height);
            var origin = new System.Drawing.Point(0, 0);
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
            var presented = PInvoke.UpdateLayeredWindow(_window, screen, &at, &size, _memory, &origin, new COLORREF(0), &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
            if (!presented)
                return false;
        }
        finally
        {
            _ = PInvoke.ReleaseDC(HWND.Null, screen);
        }

        Stack(above);
        return true;
    }

    /// <summary>Changes the alpha of the frame shown, without drawing it again.</summary>
    internal void Fade(byte alpha)
    {
        if (!_shown)
            return;

        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
        _ = PInvoke.UpdateLayeredWindow(_window, HDC.Null, null, null, HDC.Null, null, new COLORREF(0), &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
    }

    internal void Hide()
    {
        if (_shown)
            _ = PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_HIDE);

        _shown = false;
    }

    public void Dispose()
    {
        StandInHost.Close(_window);
        Free();
    }

    // Just above the window it stands in for, so whatever covers the window covers this too. A layer already there stays put, as
    // the border and glow stack on it. A null handle is the top.
    private void Stack(HWND above)
    {
        var placed = above == _window ? SET_WINDOW_POS_FLAGS.SWP_NOZORDER : 0;
        _ = PInvoke.SetWindowPos(
            _window,
            above,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW | placed);
        _shown = true;
    }

    private bool Fit(int width, int height)
    {
        if (_bitmap != HBITMAP.Null && _width == width && _height == height)
            return true;

        Free();
        var screen = PInvoke.GetDC(HWND.Null);
        try
        {
            _memory = PInvoke.CreateCompatibleDC(screen);
            var info = new BITMAPINFO();
            info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
            info.bmiHeader.biWidth = width;
            info.bmiHeader.biHeight = -height;
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            void* bits;
            _bitmap = PInvoke.CreateDIBSection(_memory, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
            if (_bitmap.IsNull || bits is null)
            {
                Free();
                return false;
            }

            _bits = bits;
            _previous = PInvoke.SelectObject(_memory, _bitmap);
            _width = width;
            _height = height;
            return true;
        }
        finally
        {
            _ = PInvoke.ReleaseDC(HWND.Null, screen);
        }
    }

    private void Free()
    {
        if (!_memory.IsNull)
        {
            if (!_previous.IsNull)
                _ = PInvoke.SelectObject(_memory, _previous);
            _ = PInvoke.DeleteDC(_memory);
        }

        if (!_bitmap.IsNull)
            _ = PInvoke.DeleteObject(_bitmap);

        _memory = HDC.Null;
        _bitmap = HBITMAP.Null;
        _previous = HGDIOBJ.Null;
        _bits = null;
        _width = 0;
        _height = 0;
    }
}
