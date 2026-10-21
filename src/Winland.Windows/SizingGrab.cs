using Winland.Core;

namespace Winland.Windows;

/// <summary>What the system's move and size loop turned out to be.</summary>
internal enum DragKind
{
    /// <summary>Not known yet. A window's reply to a hit test can say nothing, and the window's own change settles it.</summary>
    Unclear,
    Move,
    Size,
}

/// <summary>
/// Tells a drag that sizes a window from one that moves it. The system reports only that a drag began. The
/// window's reply to a hit test at the pointer answers when it names a sizing border or names the title or a
/// button. A window may snap its size to a grid as the sizing begins, which pulls its edge from under the
/// pointer, so its reply can also be "nowhere" or "client". Then the first change to the window decides.
/// </summary>
internal static class SizingGrab
{
    private const int HitNowhere = 0;
    private const int HitClient = 1;
    private const int HitSizeBox = 4;
    private const int HitLeft = 10;
    private const int HitBottomRight = 17;

    /// <summary>The window's reply to a hit test, or null when it did not answer in time.</summary>
    internal static DragKind Classify(int? hit)
    {
        if (hit is not int code || code is HitNowhere or HitClient)
            return DragKind.Unclear;

        return code == HitSizeBox || code is >= HitLeft and <= HitBottomRight ? DragKind.Size : DragKind.Move;
    }

    /// <summary>A change in the window's size is a sizing. A change in its position alone is a move.</summary>
    internal static DragKind Resolve(Rectangle start, Rectangle now)
    {
        if (now.Width != start.Width || now.Height != start.Height)
            return DragKind.Size;

        return now.X != start.X || now.Y != start.Y ? DragKind.Move : DragKind.Unclear;
    }
}
