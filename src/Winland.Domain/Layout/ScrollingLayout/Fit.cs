using static Winland.Domain.OrdinalText;

namespace Winland.Domain;

internal sealed partial class ScrollingLayout
{
    private IReadOnlyList<Placement> MoveTape(WindowId? active, string argument)
    {
        if (Is(argument, "+col") || Is(argument, "col"))
            return StepColumn(active, forward: true);
        if (Is(argument, "-col"))
            return StepColumn(active, forward: false);
        if (!TryRelative(argument, out var delta))
        {
            Fail("failed to parse offset");
            return [];
        }

        AdjustOffset(-delta);
        Recalculate();
        var center = ColumnAtCenter();
        if (center is { Cells.Count: > 0 })
            Focus(center.Cells[0]);
        return Placements();
    }

    private IReadOnlyList<Placement> FitMessage(WindowId? active, string mode)
    {
        if (active is not WindowId window)
        {
            Fail("no focused window");
            return [];
        }

        if (CellFor(window) is not Cell cell || cell.Column is null || _columns.Count == 0)
        {
            Fail("can't fit: no window or columns");
            return [];
        }

        if (Is(mode, "active"))
        {
            cell.Column.Width = 1;
            SetOffset(OffsetBefore(cell.Column));
        }
        else if (Is(mode, "expand"))
        {
            Expand(cell.Column);
        }
        else if (Is(mode, "all"))
        {
            var share = 1.0 / _columns.Count;
            foreach (var column in _columns)
                column.Width = share;
            SetOffset(0);
        }
        else if (Is(mode, "toend"))
        {
            if (!FitRange(cell, toEnd: true))
                return [];
        }
        else if (Is(mode, "tobeg"))
        {
            if (!FitRange(cell, toEnd: false))
                return [];
        }
        else if (Is(mode, "visible"))
        {
            FitVisible();
        }

        Recalculate();
        return Placements();
    }

    private void Expand(Column current)
    {
        var others = 0.0;
        foreach (var column in _columns)
        {
            if (!ReferenceEquals(column, current) && ColumnVisible(column, full: false))
                others += column.Width;
        }

        var remaining = Math.Clamp(1 - others, 0, 1);
        if (remaining == current.Width)
            return;

        current.Width = remaining;
        SetOffset(0);
        FitColumn(current);
    }

    private bool FitRange(Cell cell, bool toEnd)
    {
        var begun = false;
        var foundAt = 0;
        if (toEnd)
        {
            for (var i = 0; i < _columns.Count; i++)
            {
                if (!begun && !ReferenceEquals(_columns[i], cell.Column))
                    continue;
                if (!begun)
                {
                    begun = true;
                    foundAt = i;
                }

                _columns[i].Width = 1.0 / (_columns.Count - foundAt);
            }
        }
        else
        {
            for (var i = _columns.Count - 1; i >= 0; i--)
            {
                if (!begun && !ReferenceEquals(_columns[i], cell.Column))
                    continue;
                if (!begun)
                {
                    begun = true;
                    foundAt = i;
                }

                _columns[i].Width = 1.0 / (foundAt + 1);
            }
        }

        if (!begun)
        {
            if (toEnd)
                Fail("couldn't find beginning");
            return false;
        }

        SetOffset(toEnd ? OffsetBeforeIndex(foundAt) : 0);
        return true;
    }

    private void FitVisible()
    {
        var begun = false;
        var foundAt = 0;
        var visible = new List<Column>();
        for (var i = 0; i < _columns.Count; i++)
        {
            var seen = ColumnVisible(_columns[i], full: false);
            if (!begun && !seen)
                continue;
            if (!begun)
            {
                begun = true;
                foundAt = i;
            }

            if (!seen)
                break;
            visible.Add(_columns[i]);
        }

        if (!begun || visible.Count == 0)
            return;

        var share = 1.0 / visible.Count;
        foreach (var column in visible)
            column.Width = share;
        SetOffset(OffsetBeforeIndex(foundAt));
    }

    private double OffsetBefore(Column column) => OffsetBeforeIndex(_columns.IndexOf(column));

    private double OffsetBeforeIndex(int index)
    {
        if (index <= 0)
            return 0;

        var width = Usable().Width;
        var offset = 0.0;
        for (var i = 0; i < index && i < _columns.Count; i++)
            offset += width * _columns[i].Width;
        return offset;
    }

    private IReadOnlyList<Placement> CenterMessage(WindowId? active)
    {
        if (active is not WindowId window || CellFor(window) is not Cell cell || cell.Column is null)
        {
            Fail("no window");
            return [];
        }

        CenterColumn(cell.Column);
        Recalculate();
        return Placements();
    }

    private IReadOnlyList<Placement> InhibitMessage(string[] tokens)
    {
        if (tokens.Length > 2)
        {
            Fail("too many args");
            return [];
        }

        if (tokens.Length == 1)
        {
            if (_inhibited)
                Uninhibit();
            else
                Inhibit();
        }
        else if (Is(At(tokens, 1), "0") || Is(At(tokens, 1), "false"))
        {
            Uninhibit();
        }
        else
        {
            Inhibit();
        }

        return Placements();
    }

    private IReadOnlyList<Placement> FitIntoView(WindowId? active)
    {
        if (active is not WindowId window)
        {
            Fail("no focused window");
            return [];
        }

        if (CellFor(window) is not Cell cell || cell.Column is null || _columns.Count == 0)
        {
            Fail("can't fit: no window or columns");
            return [];
        }

        FitColumn(cell.Column);
        Recalculate();
        return Placements();
    }
}
