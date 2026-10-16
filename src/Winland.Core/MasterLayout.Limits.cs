namespace Winland;

internal sealed partial class MasterLayout
{
    private void RaiseToMinimum(List<Tile> tiles, int[] sizes, int total, bool vertical, int origin)
    {
        var count = tiles.Count;
        if (count == 0 || sizes.Length != count || total <= 0)
            return;

        var area = TilingArea();
        var areaStart = vertical ? area.Y : area.X;
        var areaSpan = vertical ? area.Height : area.Width;
        // No tile is left smaller than a fifth of an even share, the least a resize leaves a tile.
        var floor = total / count / 5;
        var needs = new int[count];
        long sum = 0;
        for (var i = 0; i < count; i++)
        {
            var atStart = i == 0 && origin == areaStart;
            var atEnd = i == count - 1 && origin + total == areaStart + areaSpan;
            needs[i] = Math.Max(AlongNeed(tiles[i], vertical, atStart, atEnd), floor);
            sum += needs[i];
        }

        if (sum > total)
            return;

        var extra = total - (int)sum;
        var flex = 0;
        for (var i = 0; i < count; i++)
            flex += Math.Max(0, sizes[i] - needs[i]);

        var cursor = 0;
        for (var i = 0; i < count; i++)
        {
            int size;
            if (i == count - 1)
                size = total - cursor;
            else if (flex == 0)
                size = needs[i];
            else
            {
                var share = (int)((long)extra * Math.Max(0, sizes[i] - needs[i]) / flex);
                size = needs[i] + share;
            }

            sizes[i] = Math.Max(0, size);
            cursor += sizes[i];
        }
    }

    private int AlongNeed(Tile tile, bool vertical, bool atStart, bool atEnd)
    {
        if (!_limits.TryGetValue(tile.Id, out var limits) || limits.Min is not { } min)
            return 0;

        var want = vertical ? min.Height : min.Width;
        if (want < 1)
            return 0;

        var gaps = _layout.GapsIn;
        var before = atStart ? 0 : vertical ? gaps.Top : gaps.Left;
        var after = atEnd ? 0 : vertical ? gaps.Bottom : gaps.Right;
        var chrome = before + after + (_layout.BorderSize * 2);
        var need = (long)want + chrome;
        return need > int.MaxValue ? int.MaxValue : (int)need;
    }
}
