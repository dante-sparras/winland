namespace Winland.Core;

/// <summary>Strips caption buttons and the thick resize border from a window style.</summary>
public static class CaptionStyle
{
    private const long SysMenu = 0x00080000;
    private const long MinimizeBox = 0x00020000;
    private const long MaximizeBox = 0x00010000;
    private const long ThickFrame = 0x00040000;

    /// <summary>Non-client hit for the minimize button.</summary>
    public const int MinimizeButton = 8;

    /// <summary>Non-client hit for the maximize button.</summary>
    public const int MaximizeButton = 9;

    /// <summary>Clears close, minimize, maximize, and the thick resize border. The caption stays.</summary>
    /// <param name="style">Current window style bits.</param>
    /// <returns>The same style without caption buttons or a thick frame.</returns>
    public static long WithoutButtons(long style) => WithoutButtons(style, keepThickFrame: false);

    /// <summary>
    /// Clears close, minimize, and maximize. The caption stays.
    /// A popup keeps its thick frame when <paramref name="keepThickFrame"/> is set, so admission still treats it as a tile.
    /// </summary>
    /// <param name="style">Current window style bits.</param>
    /// <param name="keepThickFrame">True when the thick resize border must stay.</param>
    /// <returns>The same style without caption buttons.</returns>
    public static long WithoutButtons(long style, bool keepThickFrame)
    {
        var mask = SysMenu | MinimizeBox | MaximizeBox;
        if (!keepThickFrame)
        {
            mask |= ThickFrame;
        }

        return style & ~mask;
    }

    /// <summary>True when a caption hit would minimize or maximize the window.</summary>
    /// <param name="hit">Non-client hit-test result.</param>
    /// <returns>True for the minimize and maximize buttons.</returns>
    public static bool IsSizeButton(int hit) => hit is MinimizeButton or MaximizeButton;
}
