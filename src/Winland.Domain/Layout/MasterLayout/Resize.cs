namespace Winland.Domain;

internal sealed partial class MasterLayout
{
    /// <summary>
    /// Resizes a tile by a pixel delta. A mouse resize names the grabbed corner; a dispatcher resize names none.
    /// </summary>
    private void ResizeTile(Tile tile, int dx, int dy, (bool Left, bool Top)? corner = null)
    {
        var orientation = Dynamic();
        var centered = orientation == MasterOrientation.Center && SlaveCount() >= _master.SlaveCountForCenter;
        if (_tiles.Count == 1 && !centered)
            return;

        var area = TilingArea();
        var width = Math.Max(1, area.Width);
        var height = Math.Max(1, area.Height);
        var delta = orientation switch
        {
            MasterOrientation.Left => dx / (double)width,
            MasterOrientation.Right => -dx / (double)width,
            MasterOrientation.Bottom => -dy / (double)height,
            MasterOrientation.Top => dy / (double)height,
            _ => CenterDelta(tile, dx, width, centered, corner),
        };

        foreach (var node in _tiles)
        {
            if (node.Master)
                node.MasterFraction = Math.Clamp(node.MasterFraction + delta, 0.05, 0.95);
        }

        var vertical = orientation is MasterOrientation.Left or MasterOrientation.Right or MasterOrientation.Center;
        var resizeDelta = vertical ? dy : dx;
        var column = SameColumn(tile, orientation);
        if (resizeDelta != 0 && column.Count > 1)
        {
            var total = vertical ? area.Height : area.Width;
            var unit = total / (double)column.Count;
            if (!_master.SmartResizing)
                tile.SizeFraction = Math.Clamp(tile.SizeFraction + (resizeDelta / unit), 0.05, 1.95);
            else
                SmartResize(tile, column, resizeDelta, vertical, total, unit, corner);
        }

        Recalculate();
    }

    private double CenterDelta(Tile tile, int dx, int width, bool centered, (bool Left, bool Top)? corner)
    {
        var delta = dx / (double)width;
        if (!centered)
            return delta;

        if (!tile.Master || corner is not null)
            delta *= 2;
        if ((!tile.Master && Sticks(tile.Box.X, TilingArea().X)) || (tile.Master && corner is { Left: true } && _master.SmartResizing))
            delta = -delta;

        return delta;
    }

    private void SmartResize(Tile tile, List<Tile> column, int resizeDelta, bool vertical, int total, double unit, (bool Left, bool Top)? corner)
    {
        var index = column.IndexOf(tile);
        if (index < 0)
            return;

        var area = TilingArea();
        // The grabbed top or left edge, or a tile against the far edge, takes its space from the tiles before it.
        var resizePrev = vertical
            ? (corner is { Top: true } || Sticks(tile.Box.Y + tile.Box.Height, area.Y + area.Height)) && !Sticks(tile.Box.Y, area.Y)
            : (corner is { Left: true } || Sticks(tile.Box.X + tile.Box.Width, area.X + area.Width)) && !Sticks(tile.Box.X, area.X);

        var others = new List<Tile>();
        if (resizePrev)
        {
            for (var i = index - 1; i >= 0; i--)
                others.Add(column[i]);
        }
        else
        {
            for (var i = index + 1; i < column.Count; i++)
                others.Add(column[i]);
        }

        var sizeLeft = 0.0;
        foreach (var other in others)
            sizeLeft += vertical ? other.Box.Height : other.Box.Width;

        var minSize = total / (double)column.Count * 0.2;
        var nodeSize = vertical ? tile.Box.Height : tile.Box.Width;
        var maxIncrease = sizeLeft - (others.Count * minSize);
        var maxDecrease = minSize - nodeSize;

        // A large minimum can leave this tile and the others below the smallest share. Then nothing can give.
        if (maxDecrease > maxIncrease)
            return;

        var diff = Math.Clamp(resizePrev ? -resizeDelta : resizeDelta, maxDecrease, maxIncrease);
        tile.SizeFraction += diff / unit;
        foreach (var other in others)
        {
            var size = vertical ? other.Box.Height : other.Box.Width;
            var each = maxIncrease != 0 ? diff * (size - minSize) / maxIncrease : diff / Math.Max(1, others.Count);
            other.SizeFraction -= each / unit;
        }
    }

    private List<Tile> SameColumn(Tile tile, MasterOrientation orientation)
    {
        var result = new List<Tile>();
        if (tile.Master || orientation != MasterOrientation.Center || SlaveCount() < _master.SlaveCountForCenter)
        {
            foreach (var node in _tiles)
            {
                if (node.Master == tile.Master)
                    result.Add(node);
            }

            return result;
        }

        var extraRight = _master.CenterFallback == MasterOrientation.Right;
        var slaveIndex = 0;
        var tileIndex = -1;
        foreach (var node in _tiles)
        {
            if (node.Master)
                continue;

            if (ReferenceEquals(node, tile))
                tileIndex = slaveIndex;

            slaveIndex++;
        }

        var tileOnLeft = tileIndex >= 0 && (tileIndex % 2 == 0) != extraRight;
        slaveIndex = 0;
        foreach (var node in _tiles)
        {
            if (node.Master)
                continue;

            var onLeft = (slaveIndex % 2 == 0) != extraRight;
            if (onLeft == tileOnLeft)
                result.Add(node);

            slaveIndex++;
        }

        return result;
    }
}
