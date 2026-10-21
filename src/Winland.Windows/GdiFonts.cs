using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Winland.Core;

namespace Winland.Windows;

/// <summary>
/// Creates GDI fonts from a LOGFONTW. CreateFont takes its character set as a byte, which the generated interop
/// cannot marshal, so it fails at run time.
/// </summary>
internal static class GdiFonts
{
    internal static unsafe DeleteObjectSafeHandle Create(int pixelHeight, int weight, FONT_QUALITY quality, string family)
    {
        var font = new LOGFONTW
        {
            lfHeight = -Math.Max(1, pixelHeight),
            lfWeight = weight,
            lfCharSet = FONT_CHARSET.DEFAULT_CHARSET,
            lfOutPrecision = FONT_OUTPUT_PRECISION.OUT_DEFAULT_PRECIS,
            lfClipPrecision = FONT_CLIP_PRECISION.CLIP_DEFAULT_PRECIS,
            lfQuality = quality,
        };
        var name = family.AsSpan(0, Math.Min(family.Length, 31));
        var face = new Span<char>(&font.lfFaceName, 32);
        name.CopyTo(face);
        face[name.Length] = '\0';
        return PInvoke.CreateFontIndirect(in font);
    }
}
