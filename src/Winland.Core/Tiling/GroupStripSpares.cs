namespace Winland.Core.Tiling;

/// <summary>
/// The hidden strip windows the desktop keeps for the next group. Opening one costs a frame, and a switch between a Workspace
/// with groups and one without would open them every time, so a hidden strip is kept for a while after it was last shown.
/// Each holds a whole window, so after that all but <see cref="Kept"/> close.
/// </summary>
public static class GroupStripSpares
{
    /// <summary>The hidden strips kept however long ago they were shown.</summary>
    public const int Kept = 1;

    /// <summary>How long a hidden strip is kept after it was last shown, in milliseconds.</summary>
    public const long QuietMs = 5 * 60_000;

    /// <summary>True when the hidden strip shown longest ago should close.</summary>
    /// <param name="held">Strip windows that exist, shown and hidden.</param>
    /// <param name="shown">Strips on the glass now.</param>
    /// <param name="hiddenForMs">How long ago that hidden strip was last shown, in milliseconds.</param>
    /// <returns>False while no more than <see cref="Kept"/> are hidden, or before <see cref="QuietMs"/> is up.</returns>
    public static bool Closes(int held, int shown, long hiddenForMs) => held > shown + Kept && hiddenForMs >= QuietMs;
}
