namespace Winland;

internal sealed partial class MasterLayout
{
    private void Recalculate()
    {
        var master = FirstMaster();
        if (master is null)
            return;

        var area = TilingArea();
        if (area.Width <= 0 || area.Height <= 0)
        {
            foreach (var tile in _tiles)
                tile.Box = area;
            return;
        }

        var orientation = Resolve(out var centered);
        var slaves = SlaveCount();
        if (_tiles.Count == 1 && !centered)
        {
            PlaceAlone(master, area, orientation);
            return;
        }

        if (orientation is MasterOrientation.Top or MasterOrientation.Bottom)
            PlaceBands(area, orientation, master, slaves);
        else
            PlaceColumns(area, orientation, master, slaves, centered);
    }

    private void PlaceAlone(Tile master, Rectangle area, MasterOrientation orientation)
    {
        if (!_master.AlwaysKeepPosition)
            master.Box = area;
        else
        {
            var width = Share(area.Width, master.MasterFraction);
            var x = orientation switch
            {
                MasterOrientation.Right => area.Width - width,
                MasterOrientation.Center => (area.Width - width) / 2,
                _ => 0,
            };
            master.Box = new Rectangle(area.X + x, area.Y, width, area.Height);
        }
    }

    private void PlaceBands(Rectangle area, MasterOrientation orientation, Tile master, int slaves)
    {
        var masterHeight = slaves > 0 ? Share(area.Height, master.MasterFraction) : area.Height;
        var masterY = orientation == MasterOrientation.Bottom ? area.Y + area.Height - masterHeight : area.Y;
        PlaceStack(Masters(), area.X, masterY, area.Width, masterHeight, vertical: false);
        if (slaves == 0)
            return;

        var slaveHeight = area.Height - masterHeight;
        var slaveY = orientation == MasterOrientation.Top ? masterY + masterHeight : area.Y;
        PlaceStack(Slaves(), area.X, slaveY, area.Width, slaveHeight, vertical: false);
    }

    private void PlaceColumns(Rectangle area, MasterOrientation orientation, Tile master, int slaves, bool centered)
    {
        var masterWidth = area.Width;
        if (slaves > 0 || centered)
            masterWidth = Share(area.Width, master.MasterFraction);

        var masterX = area.X;
        if (orientation == MasterOrientation.Right)
            masterX = area.X + area.Width - masterWidth;
        else if (centered)
            masterX = area.X + ((area.Width - masterWidth) / 2);

        PlaceStack(Masters(), masterX, area.Y, masterWidth, area.Height, vertical: true);
        if (slaves == 0)
            return;

        if (centered)
        {
            PlaceCenterSlaves(area, masterX, masterWidth);
            return;
        }

        var slaveWidth = area.Width - masterWidth;
        var slaveX = orientation == MasterOrientation.Right ? area.X : masterX + masterWidth;
        PlaceStack(Slaves(), slaveX, area.Y, slaveWidth, area.Height, vertical: true);
    }

    private void PlaceCenterSlaves(Rectangle area, int masterX, int masterWidth)
    {
        var extraRight = _master.CenterFallback == MasterOrientation.Right;
        var left = new List<Tile>();
        var right = new List<Tile>();
        var onRight = extraRight;
        foreach (var slave in Slaves())
        {
            (onRight ? right : left).Add(slave);
            onRight = !onRight;
        }

        var leftWidth = masterX - area.X;
        var rightX = masterX + masterWidth;
        var rightWidth = area.X + area.Width - rightX;
        PlaceStack(left, area.X, area.Y, leftWidth, area.Height, vertical: true);
        PlaceStack(right, rightX, area.Y, rightWidth, area.Height, vertical: true);
    }

    private void PlaceStack(List<Tile> tiles, int x, int y, int width, int height, bool vertical)
    {
        if (tiles.Count == 0 || width < 0 || height < 0)
            return;

        var total = vertical ? height : width;
        var sizes = Sizes(tiles, total);
        // The stack honors a window's minimum on this axis when the minimums fit.
        // The master/slave split owns the other axis, so a minimum wider than the column
        // is not applied there. When the minimums sum to more than the stack, the
        // proportional sizes stay and the stack does not grow past its column.
        RaiseToMinimum(tiles, sizes, total, vertical, vertical ? y : x);
        var cursor = 0;
        for (var i = 0; i < tiles.Count; i++)
        {
            var size = sizes[i];
            tiles[i].Box = vertical
                ? new Rectangle(x, y + cursor, width, size)
                : new Rectangle(x + cursor, y, size, height);
            cursor += size;
        }
    }

    private int[] Sizes(List<Tile> tiles, int total)
    {
        var count = tiles.Count;
        var sizes = new int[count];
        if (count == 0 || total <= 0)
            return sizes;

        if (_master.SmartResizing)
        {
            var accumulated = 0.0;
            foreach (var tile in tiles)
                accumulated += total / (double)count * tile.SizeFraction;

            if (accumulated > 0)
            {
                var scale = total / accumulated;
                foreach (var tile in tiles)
                    tile.SizeFraction *= scale;
            }

            var average = total / (double)count;
            var cursor = 0.0;
            for (var i = 0; i < count; i++)
            {
                var start = Round(cursor);
                cursor += average * tiles[i].SizeFraction;
                var end = i == count - 1 ? total : Math.Clamp(Round(cursor), start, total);
                sizes[i] = end - start;
            }

            return sizes;
        }

        var remaining = (double)total;
        var left = count;
        var sequential = 0.0;
        for (var i = 0; i < count; i++)
        {
            double size;
            if (left <= 1)
                size = remaining;
            else
            {
                size = remaining / left * tiles[i].SizeFraction;
                if (size > remaining * 0.9)
                    size = remaining * 0.9;
            }

            var start = Round(sequential);
            sequential += size;
            var end = i == count - 1 ? total : Math.Clamp(Round(sequential), start, total);
            sizes[i] = end - start;
            remaining -= size;
            left--;
        }

        return sizes;
    }

    private MasterOrientation Resolve(out bool centered)
    {
        centered = false;
        var orientation = Dynamic();
        if (orientation != MasterOrientation.Center)
            return orientation;

        if (SlaveCount() >= _master.SlaveCountForCenter)
        {
            centered = true;
            return MasterOrientation.Center;
        }

        return _master.CenterFallback == MasterOrientation.Center
            ? MasterOrientation.Left
            : _master.CenterFallback;
    }

    private MasterOrientation Dynamic() => _explicit ?? _master.Orientation;

    // A window below its minimum grows over its neighbours rather than its tile growing off the Monitor.
    private Rectangle Present(Tile tile)
    {
        var presented = Present(tile.Box, tile.Id);
        return _limits.TryGetValue(tile.Id, out var limits) ? limits.ForTiles.Raise(presented, TilingArea()) : presented;
    }

    private Rectangle Present(Rectangle box, ClientId? client = null)
    {
        var area = TilingArea();
        var gaps = _layout.GapsIn;
        var left = box.X == area.X ? 0 : gaps.Left;
        var top = box.Y == area.Y ? 0 : gaps.Top;
        var right = box.X + box.Width == area.X + area.Width ? 0 : gaps.Right;
        var bottom = box.Y + box.Height == area.Y + area.Height ? 0 : gaps.Bottom;
        var presented = _layout.KeepAspect(Shrink(box, left, top, right, bottom), _area, _tiles.Count);
        var border = client is ClientId id ? _layout.BorderFor(id) : _layout.BorderSize;
        return Shrink(presented, border, border, border, border);
    }

    private Rectangle TilingArea()
    {
        var gaps = _layout.GapsOut;
        var width = Math.Max(0, _area.Width - gaps.Left - gaps.Right);
        var height = Math.Max(0, _area.Height - gaps.Top - gaps.Bottom);
        return new Rectangle(_area.X + gaps.Left, _area.Y + gaps.Top, width, height);
    }

    private static int Share(int total, double fraction)
    {
        var size = Round(total * fraction);
        if (size < 0)
            return 0;
        return size > total ? total : size;
    }

    private static int Round(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static Rectangle Shrink(Rectangle box, int left, int top, int right, int bottom)
    {
        var width = Math.Max(0, box.Width - left - right);
        var height = Math.Max(0, box.Height - top - bottom);
        return new Rectangle(box.X + left, box.Y + top, width, height);
    }
}
