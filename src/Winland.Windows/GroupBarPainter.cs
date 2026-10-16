using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

/// <summary>
/// Renders one Group's tab bar into a 32-bit bitmap and hands it to a layered window.
/// The strip is almost transparent (alpha 1) so it still takes clicks. Indicators keep their alpha.
/// GDI drops the alpha of the pixels it draws text on, so those pixels are given the title color's alpha afterwards.
/// </summary>
internal static class GroupBarPainter
{
    private const string DefaultFont = "Segoe UI";

    // NONANTIALIASED_QUALITY: antialiased text would blend with the black of the transparent strip.
    private const FONT_QUALITY Quality = FONT_QUALITY.NONANTIALIASED_QUALITY;

    /// <summary>The live title of each tab, or the title the core knows when the window has none.</summary>
    internal static string[] Titles(GroupBar bar)
    {
        var titles = new string[bar.Tabs.Count];
        for (var i = 0; i < bar.Tabs.Count; i++)
        {
            var live = WindowAdapter.WindowTitle(new HWND((nint)bar.Tabs[i].Window.Value));
            titles[i] = live.Length > 0 ? live : bar.Tabs[i].Title;
        }

        return titles;
    }

    internal static unsafe void Paint(HWND hwnd, GroupBar bar, IReadOnlyList<string> titles)
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
        info.bmiHeader.biCompression = 0;
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
        pixels.Fill(0x01000000);
        for (var i = 0; i < bar.Tabs.Count; i++)
        {
            var color = bar.Style.IndicatorColor(bar.Tabs[i].Focused, bar.Locked);
            Fill(pixels, width, height, bar.IndicatorBounds(i), color);
        }

        if (bar.Style.RenderTitles)
            DrawTitles(memory, pixels, width, height, bar, titles);

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

    private static unsafe void DrawTitles(HDC memory, Span<uint> pixels, int width, int height, GroupBar bar, IReadOnlyList<string> titles)
    {
        _ = PInvoke.SetBkMode(memory, BACKGROUND_MODE.TRANSPARENT);
        var family = bar.Style.FontFamily.Length == 0 ? DefaultFont : bar.Style.FontFamily;
        for (var i = 0; i < bar.Tabs.Count && i < titles.Count; i++)
        {
            var tab = bar.Tabs[i];
            var row = bar.TitleBounds(i);
            var padding = bar.Style.TextPadding;
            var box = new RECT(row.X + padding, row.Y + bar.Style.TextOffset, row.X + row.Width - padding, row.Y + row.Height + bar.Style.TextOffset);
            if (box.right <= box.left || box.bottom <= box.top)
                continue;

            var weight = tab.Focused ? bar.Style.FontWeightActive : bar.Style.FontWeightInactive;
            using var font = PInvoke.CreateFont(
                -Math.Max(1, bar.Style.FontSize),
                0,
                0,
                0,
                weight,
                0,
                0,
                0,
                FONT_CHARSET.DEFAULT_CHARSET,
                FONT_OUTPUT_PRECISION.OUT_DEFAULT_PRECIS,
                FONT_CLIP_PRECISION.CLIP_DEFAULT_PRECIS,
                Quality,
                0,
                family);
            var previous = PInvoke.SelectObject(memory, font);
            var color = bar.Style.TitleColor(tab.Focused, bar.Locked);
            _ = PInvoke.SetTextColor(memory, new COLORREF((uint)(color.R | (color.G << 8) | (color.B << 16))));
            var text = titles[i];
            fixed (char* chars = text)
            {
                _ = PInvoke.DrawText(
                    memory,
                    chars,
                    text.Length,
                    &box,
                    DRAW_TEXT_FORMAT.DT_CENTER | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_SINGLELINE
                        | DRAW_TEXT_FORMAT.DT_END_ELLIPSIS | DRAW_TEXT_FORMAT.DT_NOPREFIX);
            }

            _ = PInvoke.SelectObject(memory, previous);
            ApplyTextAlpha(pixels, width, height, row with { Y = row.Y + bar.Style.TextOffset }, color.A);
        }
    }

    // Premultiplied BGRA.
    private static void Fill(Span<uint> pixels, int width, int height, Rectangle box, BorderColor color)
    {
        var alpha = (uint)color.A;
        var pixel = (alpha << 24)
            | ((uint)(color.R * alpha / 255) << 16)
            | ((uint)(color.G * alpha / 255) << 8)
            | (uint)(color.B * alpha / 255);
        if (pixel == 0)
            pixel = 0x01000000;

        ForEach(pixels, width, height, box, (ref uint value) => value = pixel);
    }

    // Gives the pixels GDI drew text on the title color's alpha, premultiplied. Unantialiased text holds
    // the exact color, so one alpha fits every such pixel. The result keeps an alpha of at least 1.
    internal static void ApplyTextAlpha(Span<uint> pixels, int width, int height, Rectangle box, byte alpha) =>
        ForEach(pixels, width, height, box, (ref uint value) =>
        {
            if ((value & 0xFF000000) != 0)
                return;

            uint a = alpha;
            var pixel = (a << 24)
                | ((((value >> 16) & 0xFF) * a / 255) << 16)
                | ((((value >> 8) & 0xFF) * a / 255) << 8)
                | ((value & 0xFF) * a / 255);
            value = pixel == 0 ? 0x01000000 : pixel;
        });

    private delegate void PixelAction(ref uint value);

    private static void ForEach(Span<uint> pixels, int width, int height, Rectangle box, PixelAction action)
    {
        var left = Math.Max(0, box.X);
        var top = Math.Max(0, box.Y);
        var right = Math.Min(width, box.X + box.Width);
        var bottom = Math.Min(height, box.Y + box.Height);
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
                action(ref pixels[(y * width) + x]);
        }
    }
}
