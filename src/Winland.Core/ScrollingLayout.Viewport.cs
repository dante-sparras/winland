namespace Winland;

internal sealed partial class ScrollingLayout
{
    private double Camera(double primary, double max)
    {
        if (_dragging)
            return _offset;

        if (max < primary)
            SetOffset(Math.Round((max - primary) / 2.0, MidpointRounding.AwayFromZero));
        if (max > primary && _offset < 0 && _scrolling.FocusFitMethod != ScrollingFit.Center)
            SetOffset(0);
        return _offset;
    }

    private void FitColumn(Column column)
    {
        if (!TryStrip(column, out var start, out var size, out var primary))
            return;

        var lo = start - primary + size;
        var hi = start;
        if (lo > hi)
            SetOffset(start - ((primary - size) / 2.0));
        else
            SetOffset(Math.Clamp(_offset, lo, hi));
    }

    private void CenterColumn(Column column)
    {
        if (!TryStrip(column, out var start, out var size, out var primary))
            return;

        SetOffset(start - ((primary - size) / 2.0));
    }

    private void CenterOrFit(Column? column)
    {
        if (column is null)
            return;

        if (_scrolling.FocusFitMethod == ScrollingFit.Fit)
            FitColumn(column);
        else
            CenterColumn(column);
    }

    private bool TryStrip(Column column, out double start, out double size, out double primary)
    {
        start = 0;
        size = 0;
        primary = PrimaryLength();
        var index = _columns.IndexOf(column);
        if (index < 0 || primary <= 0)
            return false;

        var fullscreen = OneColumn();
        for (var i = 0; i < index; i++)
            start += StripSize(i, primary, fullscreen);
        size = StripSize(index, primary, fullscreen);
        return true;
    }

    private double StripSize(int index, double primary, bool fullscreen) =>
        fullscreen ? primary : primary * _columns[index].Width;

    private bool OneColumn() => _scrolling.FullscreenOnOneColumn && _columns.Count == 1;

    private double MaxExtent() => MaxExtent(PrimaryLength(), OneColumn());

    private double MaxExtent(double primary, bool fullscreen)
    {
        if (_columns.Count == 0 || primary <= 0)
            return 0;
        if (fullscreen)
            return primary;

        var total = 0.0;
        foreach (var column in _columns)
            total += primary * column.Width;
        return total;
    }

    private bool ColumnVisible(Column column, bool full)
    {
        if (!TryStrip(column, out var start, out var size, out var primary))
            return false;

        var end = start + size;
        var viewEnd = _offset + primary;
        if (!full)
            return start < viewEnd && _offset < end;
        return start >= _offset && end <= viewEnd;
    }

    private Column? ColumnAtCenter()
    {
        if (_columns.Count == 0)
            return null;

        var primary = PrimaryLength();
        var fullscreen = OneColumn();
        var center = _offset + (primary / 2.0);
        var end = 0.0;
        for (var i = 0; i < _columns.Count; i++)
        {
            end += StripSize(i, primary, fullscreen);
            if (end >= center - 2)
                return _columns[i];
        }

        return _columns[^1];
    }

    private void SetOffset(double offset)
    {
        _offset = _inhibited ? _inhibitedOffset : offset;
    }

    private void AdjustOffset(double delta)
    {
        if (!_inhibited)
            _offset += delta;
    }

    private void Inhibit()
    {
        _inhibitedOffset = _offset;
        _inhibited = true;
    }

    private void Uninhibit() => _inhibited = false;

    private double PrimaryLength()
    {
        var usable = Usable();
        return IsHorizontal() ? usable.Width : usable.Height;
    }

    private double SecondaryLength()
    {
        var usable = Usable();
        return IsHorizontal() ? usable.Height : usable.Width;
    }

    private double VisibleLength(Cell cell)
    {
        var view = Viewport();
        var box = cell.Logical;
        var horizontal = IsHorizontal();
        var start = horizontal ? Math.Max(view.X, box.X) : Math.Max(view.Y, box.Y);
        var end = horizontal
            ? Math.Min(view.X + view.Width, box.X + box.Width)
            : Math.Min(view.Y + view.Height, box.Y + box.Height);
        return Math.Max(0, end - start);
    }

    private Rectangle Usable()
    {
        var gaps = _layoutOptions.GapsOut;
        var width = Math.Max(0, _area.Width - gaps.Left - gaps.Right);
        var height = Math.Max(0, _area.Height - gaps.Top - gaps.Bottom);
        return new Rectangle(_area.X + gaps.Left, _area.Y + gaps.Top, width, height);
    }

    private Rectangle Viewport() =>
        _monitor.Width > 0 && _monitor.Height > 0 ? _monitor : _area;

    private bool IsHorizontal() =>
        _scrolling.Direction is ScrollingDirection.Right or ScrollingDirection.Left;

    private bool IsReversed() =>
        _scrolling.Direction is ScrollingDirection.Left or ScrollingDirection.Up;
}
