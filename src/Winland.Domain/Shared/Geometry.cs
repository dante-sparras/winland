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

internal static class Sides
{
    private static readonly (string Word, Side Side)[] Words =
    [
        ("l", Side.Left),
        ("left", Side.Left),
        ("r", Side.Right),
        ("right", Side.Right),
        ("u", Side.Up),
        ("up", Side.Up),
        ("t", Side.Up),
        ("top", Side.Up),
        ("d", Side.Down),
        ("down", Side.Down),
        ("b", Side.Down),
        ("bottom", Side.Down),
    ];

    /// <summary>Reads a whole direction word, <c>l</c> or <c>left</c>, <c>r</c> or <c>right</c>, <c>u</c>, <c>up</c>, <c>t</c> or <c>top</c>, <c>d</c>, <c>down</c>, <c>b</c> or <c>bottom</c>, in any case.</summary>
    public static bool TryParse(string text, out Side side)
    {
        foreach (var (word, parsed) in Words)
        {
            if (text.Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                side = parsed;
                return true;
            }
        }

        side = Side.Left;
        return false;
    }
}
