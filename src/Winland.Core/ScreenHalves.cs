namespace Winland.Core;

/// <summary>
/// One screen as two monitors side by side: its left half and its right half.
/// A development switch, for running what needs two monitors on a Host that has one screen.
/// The Host reads the switch once at start and lists its monitors through this, so everything above it sees two monitors.
/// </summary>
public static class ScreenHalves
{
    /// <summary>The switch's file name in the user folder. The switch is on while the file is there, whatever it holds.</summary>
    public const string FileName = "two-monitors";

    /// <summary>The monitors to report for <paramref name="screens"/>: two for each screen, its left half and then its right.</summary>
    /// <param name="screens">The screens Windows lists, in physical pixels.</param>
    /// <returns>
    /// Twice as many rectangles, in the same order, so the two at <c>2 * i</c> and <c>2 * i + 1</c> are the halves of screen <c>i</c>.
    /// </returns>
    public static IReadOnlyList<TileRect> Split(IReadOnlyList<TileRect> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        var halves = new TileRect[screens.Count * 2];
        for (var i = 0; i < screens.Count; i++)
        {
            (halves[2 * i], halves[(2 * i) + 1]) = Halves(screens[i]);
        }

        return halves;
    }

    /// <summary>
    /// The half of <paramref name="screen"/> that <paramref name="window"/> is on, picked the way Windows picks a window's monitor:
    /// the half that holds more of it, and the nearer half for a window that is off the screen.
    /// </summary>
    /// <param name="screen">The screen Windows says the window is on.</param>
    /// <param name="window">The window's outer rectangle.</param>
    /// <returns>
    /// One of the two rectangles <see cref="Split"/> makes of <paramref name="screen"/>. The left one when each half holds as much.
    /// </returns>
    public static TileRect Of(TileRect screen, TileRect window)
    {
        var (left, right) = Halves(screen);
        var onLeft = Across(window, left);
        var onRight = Across(window, right);
        if (onLeft == 0 && onRight == 0)
        {
            // Off the screen: the half its middle is nearest to.
            return (long)window.X + (window.Width / 2) >= right.X ? right : left;
        }

        return onRight > onLeft ? right : left;
    }

    private static (TileRect Left, TileRect Right) Halves(TileRect screen)
    {
        var left = screen.Width / 2;
        return (
            new TileRect(screen.X, screen.Y, left, screen.Height),
            new TileRect(screen.X + left, screen.Y, screen.Width - left, screen.Height));
    }

    /// <summary>How many pixels of <paramref name="half"/>'s width <paramref name="window"/> covers. The halves are equally tall.</summary>
    private static long Across(TileRect window, TileRect half) =>
        Math.Max(0, Math.Min((long)window.X + window.Width, (long)half.X + half.Width) - Math.Max(window.X, half.X));
}
