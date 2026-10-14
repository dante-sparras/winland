namespace Winland.Core.Tiling;

/// <summary>
/// Places a set of windows over one work area, above the tiles. One window is a centered panel,
/// two thirds of the area each way, as Omarchy drops a single scratchpad window down. Two or more share the full width, side by side.
/// </summary>
public static class OverlayTiles
{
    /// <summary>One raised panel for a single id. Otherwise one raised column per id, where the last column takes the leftover width.</summary>
    /// <param name="ids">The windows, left to right.</param>
    /// <param name="area">The work area they are drawn over.</param>
    /// <returns>A raised placement for each id, or none for an empty area.</returns>
    public static IReadOnlyList<Placement> Cover(IReadOnlyList<long> ids, TileRect area)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0 || area.Width <= 0 || area.Height <= 0)
        {
            return [];
        }

        if (ids.Count == 1)
        {
            var panelWidth = Math.Max(1, area.Width * 2 / 3);
            var panelHeight = Math.Max(1, area.Height * 2 / 3);
            return
            [
                new Placement(
                    ids[0],
                    area.X + ((area.Width - panelWidth) / 2),
                    area.Y + ((area.Height - panelHeight) / 2),
                    panelWidth,
                    panelHeight,
                    true,
                    true),
            ];
        }

        var result = new List<Placement>(ids.Count);
        var width = Math.Max(1, area.Width / ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            var x = area.X + (i * width);
            var span = i == ids.Count - 1 ? area.X + area.Width - x : width;
            result.Add(new Placement(ids[i], x, area.Y, Math.Max(1, span), area.Height, true, true));
        }

        return result;
    }
}
