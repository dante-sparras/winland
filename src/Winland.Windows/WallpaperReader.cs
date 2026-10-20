using System.Runtime.InteropServices;

namespace Winland.Windows;

/// <summary>One monitor's wallpaper as Windows has it set: where the monitor is, the picture, how it is fitted and the background color.</summary>
internal sealed record WallpaperScreen(Rectangle Area, string? Path, WallpaperPosition Position, uint Background);

/// <summary>A decoded picture: opaque 0xAARRGGBB pixels, <see cref="Width"/> to a row.</summary>
internal sealed record Picture(int Width, int Height, uint[] Pixels);

/// <summary>
/// Reads the desktop wallpaper through <c>IDesktopWallpaper</c> and decodes its picture with GDI+. It only reads: the
/// wallpaper Windows shows is never set.
/// </summary>
// Stryker disable once all : reads the real wallpaper through COM and GDI+.
internal static class WallpaperReader
{
    // The last picture this decoded, so a change of blur options does not decode the file again.
    private static readonly Lock Gate = new();
    private static string? _cachedPath;
    private static DateTime _cachedStamp;
    private static Picture? _cached;

    /// <summary>The wallpaper of each monitor Windows reports, or an empty list when it cannot be read.</summary>
    internal static IReadOnlyList<WallpaperScreen> Screens()
    {
        var screens = new List<WallpaperScreen>();
        IDesktopWallpaper? wallpaper = null;
        try
        {
            wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            wallpaper.GetBackgroundColor(out var color);
            wallpaper.GetPosition(out var position);
            var background = 0xFF000000u | ((color & 0xFF) << 16) | (color & 0xFF00) | ((color >> 16) & 0xFF);
            wallpaper.GetMonitorDevicePathCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                try
                {
                    wallpaper.GetMonitorDevicePathAt(i, out var id);
                    wallpaper.GetMonitorRECT(id, out var rect);
                    wallpaper.GetWallpaper(id, out var path);
                    var area = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                    if (area.Width > 0 && area.Height > 0)
                        screens.Add(new WallpaperScreen(area, string.IsNullOrWhiteSpace(path) ? null : path, Enum.IsDefined(position) ? position : WallpaperPosition.Fill, background));
                }
                catch (COMException)
                {
                    // A monitor that is gone, or that has no wallpaper of its own, is left out.
                }
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            Console.Error.WriteLine($"The wallpaper was not read: {exception.Message}");
        }
        finally
        {
            if (wallpaper is not null)
                _ = Marshal.ReleaseComObject(wallpaper);
        }

        return screens;
    }

    /// <summary>Decodes the picture at <paramref name="path"/>, or null when it is missing or not a picture.</summary>
    internal static Picture? Load(string path)
    {
        try
        {
            lock (Gate)
            {
                var stamp = File.GetLastWriteTimeUtc(path);
                if (_cached is not null && _cachedPath == path && _cachedStamp == stamp)
                    return _cached;

                var picture = Decode(path);
                _cachedPath = path;
                _cachedStamp = stamp;
                _cached = picture;
                return picture;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Forgets the decoded picture, which can be large.</summary>
    internal static void Release()
    {
        lock (Gate)
        {
            _cachedPath = null;
            _cached = null;
        }
    }

    // The largest picture worth decoding: 16384 by 16384 pixels is a gigabyte of floats once blurred.
    private const long MaxPixels = 64L * 1024 * 1024;

    private static nint _gdiToken;

    private static unsafe Picture? Decode(string path)
    {
        if (_gdiToken == 0)
        {
            var input = new GdiplusStartupInput { GdiplusVersion = 1 };
            if (GdiplusStartup(out var token, ref input, 0) != 0)
                return null;

            _gdiToken = (nint)token;
        }

        if (GdipCreateBitmapFromFile(path, out var bitmap) != 0 || bitmap == 0)
            return null;

        try
        {
            if (GdipGetImageWidth(bitmap, out var width) != 0 || GdipGetImageHeight(bitmap, out var height) != 0 || width == 0 || height == 0
                || (long)width * height > MaxPixels)
                return null;

            var rect = new GdiRect { Width = (int)width, Height = (int)height };
            var data = new BitmapData();

            // ReadOnly, Format32bppRgb.
            if (GdipBitmapLockBits(bitmap, ref rect, 1, 0x00022009, ref data) != 0)
                return null;

            try
            {
                var pixels = new uint[(int)width * (int)height];
                for (var row = 0; row < height; row++)
                {
                    var line = new ReadOnlySpan<uint>((byte*)data.Scan0 + ((long)row * data.Stride), (int)width);
                    for (var column = 0; column < width; column++)
                        pixels[(row * (int)width) + column] = line[column] | 0xFF000000u;
                }

                return new Picture((int)width, (int)height, pixels);
            }
            finally
            {
                _ = GdipBitmapUnlockBits(bitmap, ref data);
            }
        }
        finally
        {
            _ = GdipDisposeImage(bitmap);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public nint DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GdiRect
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapData
    {
        public uint Width;
        public uint Height;
        public int Stride;
        public int PixelFormat;
        public nint Scan0;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WallpaperRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("gdiplus.dll")]
    private static extern int GdiplusStartup(out nuint token, ref GdiplusStartupInput input, nint output);

    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    private static extern int GdipCreateBitmapFromFile(string filename, out nint bitmap);

    [DllImport("gdiplus.dll")]
    private static extern int GdipGetImageWidth(nint image, out uint width);

    [DllImport("gdiplus.dll")]
    private static extern int GdipGetImageHeight(nint image, out uint height);

    [DllImport("gdiplus.dll")]
    private static extern int GdipBitmapLockBits(nint bitmap, ref GdiRect rect, uint flags, int format, ref BitmapData data);

    [DllImport("gdiplus.dll")]
    private static extern int GdipBitmapUnlockBits(nint bitmap, ref BitmapData data);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDisposeImage(nint image);

    // IDesktopWallpaper, in vtable order. The methods this does not call only hold their places.
    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);

        void GetMonitorDevicePathAt(uint monitorIndex, [MarshalAs(UnmanagedType.LPWStr)] out string monitorId);

        void GetMonitorDevicePathCount(out uint count);

        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out WallpaperRect rect);

        void SetBackgroundColor(uint color);

        void GetBackgroundColor(out uint color);

        void SetPosition(WallpaperPosition position);

        void GetPosition(out WallpaperPosition position);
    }

    [ComImport]
    [Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass;
}
