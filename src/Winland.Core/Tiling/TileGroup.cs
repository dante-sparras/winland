namespace Winland.Core.Tiling;

/// <summary>A tile that holds more than one window.</summary>
/// <param name="Members">The windows in the order the group's strip lists them.</param>
/// <param name="Shown">The member the tile shows. The others are hidden behind it.</param>
public readonly record struct TileGroup(IReadOnlyList<long> Members, long Shown);
