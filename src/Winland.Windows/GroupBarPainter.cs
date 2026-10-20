using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

/// <summary>
/// Renders one Group's tab bar into a 32-bit bitmap and hands it to a layered window.
/// The strip is almost transparent (alpha 1) so it still takes clicks. Indicators keep their alpha.
/// GDI knows no alpha, so the titles are drawn white on black into a mask with grayscale antialiasing, and each title's
/// color is laid over the bar as much as the mask covers each pixel.
/// </summary>
internal static class GroupBarPainter
{
    private const string DefaultFont = "Segoe UI";

    // Grayscale antialiasing, not ClearType, so the mask's channels are one coverage.
    private const FONT_QUALITY Quality = FONT_QUALITY.ANTIALIASED_QUALITY;

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
        var style = bar.Style;
        var count = bar.Tabs.Count;
        for (var i = 0; i < count; i++)
        {
            var active = bar.Tabs[i].Focused;
            var order = bar.DrawOrder(i);
            var corners = BarShapes.Rounded(order, count, style.Rounding, style.RoundOnlyEdges);
            var indicator = bar.IndicatorBounds(i);
            var color = style.IndicatorColor(active, bar.Locked);
            Fill(pixels, width, height, indicator, (x, y) => Shaded(color, BarShapes.Coverage(x, y, indicator, style.Rounding, style.RoundingPower, corners)));

            if (!style.Gradients)
                continue;

            // The pinned release fills the title row with a vertical gradient of the tab's colors, rounded on its own.
            var row = bar.TitleBounds(i);
            var colors = style.GradientColors(active, bar.Locked);
            var rowCorners = BarShapes.Rounded(order, count, style.GradientRounding, style.GradientRoundOnlyEdges);
            Fill(pixels, width, height, row, (x, y) =>
            {
                var shade = BarShapes.GradientAt(colors, (y - row.Y + 0.5) / row.Height);
                return Shaded(shade, BarShapes.Coverage(x, y, row, style.GradientRounding, style.GradientRoundingPower, rowCorners));
            });
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
        // The titles go white on black into a mask of their own, so their antialiased edges are coverage, not a blend with
        // whatever the strip holds.
        var info = new BITMAPINFO();
        info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = width;
        info.bmiHeader.biHeight = -height;
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = 0;
        var maskDc = PInvoke.CreateCompatibleDC(memory);
        void* maskBits;
        var maskBitmap = PInvoke.CreateDIBSection(maskDc, &info, DIB_USAGE.DIB_RGB_COLORS, &maskBits, HANDLE.Null, 0);
        if (maskBitmap.IsNull || maskBits is null)
        {
            _ = PInvoke.DeleteDC(maskDc);
            return;
        }

        var oldMask = PInvoke.SelectObject(maskDc, maskBitmap);
        var mask = new Span<uint>(maskBits, width * height);
        mask.Clear();
        _ = PInvoke.SetBkMode(maskDc, BACKGROUND_MODE.TRANSPARENT);
        _ = PInvoke.SetTextColor(maskDc, new COLORREF(0x00FFFFFF));
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
            using var font = GdiFonts.Create(bar.Style.FontSize, weight, Quality, family);
            var previous = PInvoke.SelectObject(maskDc, font);
            var text = titles[i];
            fixed (char* chars = text)
            {
                _ = PInvoke.DrawText(
                    maskDc,
                    chars,
                    text.Length,
                    &box,
                    DRAW_TEXT_FORMAT.DT_CENTER | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_SINGLELINE
                        | DRAW_TEXT_FORMAT.DT_END_ELLIPSIS | DRAW_TEXT_FORMAT.DT_NOPREFIX);
            }

            _ = PInvoke.SelectObject(maskDc, previous);
            _ = PInvoke.GdiFlush();
            ComposeText(pixels, mask, width, height, row with { Y = row.Y + bar.Style.TextOffset }, bar.Style.TitleColor(tab.Focused, bar.Locked));
        }

        _ = PInvoke.SelectObject(maskDc, oldMask);
        _ = PInvoke.DeleteObject(maskBitmap);
        _ = PInvoke.DeleteDC(maskDc);
    }

    // Premultiplied BGRA of a color over the part of a pixel a shape covers. A pixel keeps an alpha of at least 1, so the
    // strip still takes clicks there.
    private static uint Shaded(BorderColor color, double coverage)
    {
        var alpha = (uint)Math.Round(color.A * Math.Clamp(coverage, 0, 1));
        var pixel = (alpha << 24)
            | ((uint)(color.R * alpha / 255) << 16)
            | ((uint)(color.G * alpha / 255) << 8)
            | (uint)(color.B * alpha / 255);
        return pixel == 0 ? 0x01000000 : pixel;
    }

    private static void Fill(Span<uint> pixels, int width, int height, Rectangle box, Func<int, int, uint> pixel)
    {
        var left = Math.Max(0, box.X);
        var top = Math.Max(0, box.Y);
        var right = Math.Min(width, box.X + box.Width);
        var bottom = Math.Min(height, box.Y + box.Height);
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
                pixels[(y * width) + x] = pixel(x, y);
        }
    }

    // Lays a title's color over the bar as much as the mask covers each pixel: the mask's white is full coverage, and its
    // grays are the antialiased edges. The result is premultiplied and keeps an alpha of at least 1.
    internal static void ComposeText(Span<uint> pixels, ReadOnlySpan<uint> mask, int width, int height, Rectangle box, BorderColor color)
    {
        var left = Math.Max(0, box.X);
        var top = Math.Max(0, box.Y);
        var right = Math.Min(width, box.X + box.Width);
        var bottom = Math.Min(height, box.Y + box.Height);
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var index = (y * width) + x;
                var cover = Math.Max((mask[index] >> 16) & 0xFF, Math.Max((mask[index] >> 8) & 0xFF, mask[index] & 0xFF));
                if (cover == 0)
                    continue;

                var a = (uint)color.A * cover / 255;
                var over = (a << 24) | ((color.R * a / 255) << 16) | ((color.G * a / 255) << 8) | (color.B * a / 255);
                var blended = Blend(over, pixels[index]);
                pixels[index] = (blended >> 24) == 0 ? 0x01000000 : blended;
            }
        }
    }

    // Premultiplied "over".
    private static uint Blend(uint top, uint under)
    {
        var keep = 255 - (top >> 24);
        uint Channel(int shift) => Math.Min(255u, ((top >> shift) & 0xFF) + ((((under >> shift) & 0xFF) * keep) + 127) / 255);
        return (Channel(24) << 24) | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }
}
