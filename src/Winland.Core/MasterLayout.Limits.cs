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
        var needs = new int[count];
        long sum = 0;
        for (var i = 0; i < count; i++)
        {
            var atStart = i == 0 && origin == areaStart;
            var atEnd = i == count - 1 && origin + total == areaStart + areaSpan;
            needs[i] = AlongNeed(tiles[i], vertical, atStart, atEnd);
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

    private void GrowToMinimum(Tile tile)
    {
        if (!_limits.TryGetValue(tile.Id, out var limits) || limits.Min is not { } min)
            return;

        var area = TilingArea();
        var presented = Present(tile.Box);
        var box = tile.Box;
        if (min.Width > presented.Width)
        {
            var added = min.Width - presented.Width;
            if (box.X + box.Width == area.X + area.Width)
                added += _layout.GapsIn.Right;

            box = box with { Width = box.Width + added };
        }

        if (min.Height > presented.Height)
        {
            var added = min.Height - presented.Height;
            if (box.Y + box.Height == area.Y + area.Height)
                added += _layout.GapsIn.Bottom;

            box = box with { Height = box.Height + added };
        }

        tile.Box = box;
    }

    private static Client Adopted(
        ClientId id,
        WindowId window,
        WorkspaceId workspace,
        string windowClass,
        string title,
        bool floating,
        long processId,
        string initialClass,
        string initialTitle) =>
        new(
            id,
            window,
            Floating: floating,
            Workspace: workspace,
            Class: windowClass,
            Title: title,
            ProcessId: processId,
            InitialClass: initialClass.Length == 0 ? windowClass : initialClass,
            InitialTitle: initialTitle.Length == 0 ? title : initialTitle);

    private static Rectangle ClampToLimits(Rectangle bounds, Size? min, Size? max)
    {
        var width = bounds.Width;
        var height = bounds.Height;
        if (max is { } hi)
        {
            if (hi.Width < int.MaxValue && width > hi.Width)
                width = hi.Width;
            if (hi.Height < int.MaxValue && height > hi.Height)
                height = hi.Height;
        }

        if (min is { } lo)
        {
            if (lo.Width > 0 && width < lo.Width)
                width = lo.Width;
            if (lo.Height > 0 && height < lo.Height)
                height = lo.Height;
        }

        if (width == bounds.Width && height == bounds.Height)
            return bounds;

        return new Rectangle(bounds.X, bounds.Y, width, height);
    }

    private readonly record struct ClientLimits(Size? Min, Size? Max);
}
