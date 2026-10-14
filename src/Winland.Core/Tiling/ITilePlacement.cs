namespace Winland.Core.Tiling;

/// <summary>Puts tiles on the glass and reports the size each window kept.</summary>
public interface ITilePlacement
{
    /// <summary>Moves and shows or hides windows to match <paramref name="placements"/>.</summary>
    /// <param name="placements">Tiles to glide onto.</param>
    void Apply(IReadOnlyList<Placement> placements);

    /// <summary>
    /// Moves windows onto these tiles immediately.
    /// A workspace change uses this so the desktop animation does not show the old rectangle.
    /// </summary>
    /// <param name="placements">Tiles to land on, with no glide.</param>
    void PlaceNow(IReadOnlyList<Placement> placements);

    /// <summary>The size each window says it can shrink to. A window that does not answer is left out.</summary>
    /// <param name="ids">Windows to ask.</param>
    /// <returns>One minimum for each window that answered.</returns>
    IReadOnlyList<WindowMinimum> Minimums(IReadOnlyCollection<long> ids);

    /// <summary>Windows that landed on the tile <paramref name="placements"/> gave them but stayed larger than it.</summary>
    /// <param name="placements">The tiles just applied.</param>
    /// <returns>The size each window kept. A side that fit its tile is zero.</returns>
    IReadOnlyList<WindowMinimum> Overflows(IReadOnlyList<Placement> placements);
}
