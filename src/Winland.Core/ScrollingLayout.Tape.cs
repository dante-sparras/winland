namespace Winland;

// The tape as the scrollMove gesture moves it. Offsets are in shares of the view, as the pinned release measures them.
internal sealed partial class ScrollingLayout
{
    internal double ViewLength => PrimaryLength();

    internal double NormalizedOffset
    {
        get
        {
            var primary = PrimaryLength();
            return primary <= 0 ? 0 : _offset / primary;
        }
    }

    /// <summary>Moves the tape by pixels. A positive delta moves the columns toward the end of the view.</summary>
    internal IReadOnlyList<Placement> MoveTape(double delta)
    {
        if (delta == 0)
            return [];

        AdjustOffset(-delta);
        Recalculate();
        return Placements();
    }

    internal int? ColumnOf(WindowId window) =>
        CellFor(window)?.Column is Column column ? _columns.IndexOf(column) : null;

    /// <summary>
    /// Settles the tape on the column whose place is nearest the projected offset: centered, or fitted by
    /// <c>scrolling:focus_fit_method</c>, as the pinned release snaps a scrollMove swipe. The column, or null with none.
    /// </summary>
    internal int? SnapToProjected(double projectedNormalized)
    {
        var primary = PrimaryLength();
        if (_columns.Count == 0 || primary <= 0 || MaxExtent() <= 0)
            return null;

        var projected = projectedNormalized * primary;
        var fullscreen = OneColumn();
        int? best = null;
        var bestOffset = 0.0;
        var bestDelta = 0.0;
        var bestCenterDelta = 0.0;
        var start = 0.0;
        for (var i = 0; i < _columns.Count; i++)
        {
            var size = StripSize(i, primary, fullscreen);
            var center = start - ((primary - size) / 2.0);
            var offset = center;
            if (_scrolling.FocusFitMethod == ScrollingFit.Fit)
            {
                var lo = start - primary + size;
                var hi = start;
                if (lo <= hi)
                {
                    var edge = projected < center ? lo : hi;
                    offset = Math.Abs(projected - center) <= Math.Abs(projected - edge) ? center : edge;
                }
            }

            var delta = Math.Abs(offset - projected);
            var centerDelta = Math.Abs(start + (size / 2.0) - (projected + (primary / 2.0)));
            if (best is null || delta < bestDelta || (delta == bestDelta && centerDelta < bestCenterDelta))
            {
                best = i;
                bestOffset = offset;
                bestDelta = delta;
                bestCenterDelta = centerDelta;
            }

            start += size;
        }

        SetOffset(bestOffset);
        Recalculate();
        return best;
    }

    /// <summary>The window a column gives the focus to: the one focused last there, or else its first.</summary>
    internal WindowId? FocusOfColumn(int index)
    {
        if (index < 0 || index >= _columns.Count || _columns[index].Cells.Count == 0)
            return null;

        var column = _columns[index];
        var cell = column.LastFocused is Cell last && column.Cells.Contains(last) ? last : column.Cells[0];
        return new WindowId(cell.Id.Value);
    }

    /// <summary>The first window of the column in the middle of the view.</summary>
    internal WindowId? FirstAtCenter() =>
        ColumnAtCenter() is { Cells.Count: > 0 } column ? new WindowId(column.Cells[0].Id.Value) : null;

    internal IReadOnlyList<Placement> TapePlacements() => Placements();
}
