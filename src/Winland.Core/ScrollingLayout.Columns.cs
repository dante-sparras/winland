namespace Winland;

internal sealed partial class ScrollingLayout
{
    private void InsertTiled(ClientId id, WindowId window)
    {
        var cell = new Cell(id);
        _cells[id] = cell;
        var width = TakeWidth(window);
        var focus = FocusColumn();
        var column = focus is null ? Append(width) : InsertAfter(focus, width);
        AddCell(column, cell);
        column.LastFocused = cell;
        FitColumn(column);
    }

    private double? TakeWidth(WindowId window)
    {
        if (!_widths.Remove(window, out var width))
            return null;

        return Math.Clamp(width, ScrollingOptions.MinColumnWidth, ScrollingOptions.MaxColumnWidth);
    }

    private Column Append(double? width)
    {
        var column = NewColumn(width);
        _columns.Add(column);
        return column;
    }

    private Column InsertAfter(Column after, double? width)
    {
        var column = NewColumn(width);
        var index = _columns.IndexOf(after);
        if (index < 0 || index + 1 >= _columns.Count)
            _columns.Add(column);
        else
            _columns.Insert(index + 1, column);
        return column;
    }

    private Column InsertAt(int index, double? width)
    {
        var column = NewColumn(width);
        _columns.Insert(Math.Clamp(index, 0, _columns.Count), column);
        return column;
    }

    private Column NewColumn(double? width) => new(width ?? _scrolling.ColumnWidth);

    private static void AddCell(Column column, Cell cell, int? after = null)
    {
        var count = column.Cells.Count;
        var next = count + 1;
        var scale = count / (double)next;
        foreach (var existing in column.Cells)
            existing.Size *= scale;

        cell.Column = column;
        cell.Size = 1.0 / next;
        if (after is int slot)
            column.Cells.Insert(Math.Clamp(slot + 1, 0, column.Cells.Count), cell);
        else
            column.Cells.Add(cell);
    }

    private void RemoveCell(Cell cell)
    {
        if (cell.Column is not Column column)
            return;

        var alone = column.Cells.Count <= 1;
        var last = _columns.Count > 0 && ReferenceEquals(_columns[^1], column);
        if (alone && last)
            _offset -= PrimaryLength() * column.Width;

        column.Cells.Remove(cell);
        cell.Column = null;
        if (ReferenceEquals(column.LastFocused, cell))
            column.LastFocused = column.Cells.Count == 0 ? null : column.Cells[0];

        if (column.Cells.Count == 0)
        {
            _columns.Remove(column);
            var limit = Math.Max(MaxExtent() - PrimaryLength(), 1);
            SetOffset(Math.Clamp(_offset, 0, limit));
            return;
        }

        var sum = 0.0;
        foreach (var item in column.Cells)
            sum += item.Size;
        if (sum <= 0)
            return;

        foreach (var item in column.Cells)
            item.Size /= sum;
    }

    private void Recalculate()
    {
        var usable = Usable();
        if (usable.Width <= 0 || usable.Height <= 0 || _columns.Count == 0)
            return;

        var horizontal = IsHorizontal();
        var primary = horizontal ? usable.Width : usable.Height;
        var secondary = horizontal ? usable.Height : usable.Width;
        var fullscreen = OneColumn();
        var camera = Camera(primary, MaxExtent(primary, fullscreen));
        var reversed = IsReversed();
        var border = _layoutOptions.BorderSize;

        for (var i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            var start = 0.0;
            for (var earlier = 0; earlier < i; earlier++)
                start += StripSize(earlier, primary, fullscreen);

            var size = StripSize(i, primary, fullscreen);
            var primaryPos = reversed ? primary - start - size + camera : start - camera;
            var cursor = 0.0;
            for (var j = 0; j < column.Cells.Count; j++)
            {
                var cell = column.Cells[j];
                var span = cell.Size * secondary;
                if (FullscreenPlacement(cell.Id) is Rectangle covering)
                {
                    cell.Logical = covering;
                    cell.Box = covering;
                    cursor += span;
                    continue;
                }

                double x;
                double y;
                double width;
                double height;
                if (horizontal)
                {
                    x = usable.X + primaryPos;
                    y = usable.Y + cursor;
                    width = size;
                    height = span;
                }
                else
                {
                    x = usable.X + cursor;
                    y = usable.Y + primaryPos;
                    width = span;
                    height = size;
                }

                var gaps = GapsFor(i, j, column.Cells.Count, horizontal);
                x += gaps.Left;
                y += gaps.Top;
                width = Math.Max(1, width - gaps.Left - gaps.Right);
                height = Math.Max(1, height - gaps.Top - gaps.Bottom);
                x += border;
                y += border;
                width = Math.Max(0, width - (border * 2.0));
                height = Math.Max(0, height - (border * 2.0));
                var box = _layoutOptions.KeepAspect(Pixels(x, y, width, height), _area, TiledCount());
                cell.Logical = box;
                cell.Box = box;
                cursor += span;
            }
        }
    }

    private int TiledCount()
    {
        var count = 0;
        foreach (var column in _columns)
            count += column.Cells.Count;

        return count;
    }

    private (int Left, int Top, int Right, int Bottom) GapsFor(int column, int cell, int cells, bool horizontal)
    {
        var gaps = _layoutOptions.GapsIn;
        var reversed = IsReversed();
        var notBefore = reversed ? column + 1 < _columns.Count : column > 0;
        var notAfter = reversed ? column > 0 : column + 1 < _columns.Count;
        var notAbove = reversed ? cell + 1 < cells : cell > 0;
        var notBelow = reversed ? cell > 0 : cell + 1 < cells;
        var left = horizontal ? notBefore : notAbove;
        var right = horizontal ? notAfter : notBelow;
        var top = horizontal ? notAbove : notBefore;
        var bottom = horizontal ? notBelow : notAfter;
        return (left ? gaps.Left : 0, top ? gaps.Top : 0, right ? gaps.Right : 0, bottom ? gaps.Bottom : 0);
    }
}
