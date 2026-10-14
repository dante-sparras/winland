namespace Winland.Platform.Win32;

/// <summary>A window's screen rectangle in physical pixels. The right and bottom edges are outside it.</summary>
/// <param name="Left">Left edge.</param>
/// <param name="Top">Top edge.</param>
/// <param name="Right">One past the right edge.</param>
/// <param name="Bottom">One past the bottom edge.</param>
public readonly record struct WindowBounds(int Left, int Top, int Right, int Bottom)
{
    /// <summary>True when the screen point is inside.</summary>
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>Reads the pointer, a key, or a window rectangle at the moment it is asked.</summary>
public static class PointerProbe
{
    /// <summary>Virtual key of the left mouse button.</summary>
    public const int LeftButton = 0x01;

    /// <summary>Virtual key of the right mouse button.</summary>
    public const int RightButton = 0x02;

    /// <summary>Virtual key of either Shift key.</summary>
    public const int Shift = 0x10;

    /// <summary>Virtual key of either Control key.</summary>
    public const int Control = 0x11;

    /// <summary>Virtual key of either Alt key.</summary>
    public const int Alt = 0x12;

    /// <summary>The pointer in screen pixels.</summary>
    /// <returns>False when Windows could not report it.</returns>
    public static bool TryCursor(out int x, out int y)
    {
        var read = Native.GetCursorPos(out var point);
        x = point.X;
        y = point.Y;
        return read;
    }

    /// <summary>True while <paramref name="virtualKey"/> is held.</summary>
    public static bool IsDown(int virtualKey) => (Native.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>The window's rectangle in screen pixels.</summary>
    /// <returns>False when Windows could not read it. <paramref name="bounds"/> is then empty.</returns>
    public static bool TryWindow(nint hwnd, out WindowBounds bounds)
    {
        if (!Native.GetWindowRect(hwnd, out var rect))
        {
            bounds = default;
            return false;
        }

        bounds = new WindowBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return true;
    }
}
