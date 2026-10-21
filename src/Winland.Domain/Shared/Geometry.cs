namespace Winland.Domain;

public readonly record struct Point(int X, int Y);

/// <summary>A width and height in pixels. A null size on a window is no limit on that axis pair.</summary>
public readonly record struct Size(int Width, int Height);

public readonly record struct Rectangle(int X, int Y, int Width, int Height);

/// <summary>A direction on the screen, as a Layout reads it from a dispatcher's l, r, u or d.</summary>
internal enum Side
{
    Left,
    Right,
    Up,
    Down,
}
