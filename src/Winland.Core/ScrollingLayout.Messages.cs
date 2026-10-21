using static Winland.Core.OrdinalText;

namespace Winland.Core;

internal sealed partial class ScrollingLayout
{
    private IReadOnlyList<Placement> StepColumn(WindowId? active, bool forward)
    {
        var cell = active is WindowId window ? CellFor(window) : null;
        if (!forward && cell is null)
        {
            if (_columns.Count == 0)
                return [];

            var last = _columns[^1];
            CenterColumn(last);
            Recalculate();
            if (last.Cells.Count > 0)
                Focus(last.Cells[^1]);
            return Placements();
        }

        if (cell?.Column is not Column column)
        {
            Fail("no window");
            return [];
        }

        var neighbor = forward ? Next(column) : Previous(column);
        if (neighbor is null)
        {
            if (!forward)
                return Placements();

            SetOffset(MaxExtent());
            Recalculate();
            return Placements();
        }

        CenterOrFit(neighbor);
        Recalculate();
        if (neighbor.Cells.Count > 0)
            Focus(forward ? neighbor.Cells[0] : neighbor.Cells[^1]);
        return Placements();
    }

    private IReadOnlyList<Placement> ResizeColumn(WindowId? active, string[] tokens)
    {
        if (active is not WindowId window || CellFor(window) is not Cell cell || cell.Column is not Column column)
            return [];

        var argument = At(tokens, 1);
        if (Is(argument, "all"))
        {
            if (!TryDouble(At(tokens, 2), out var width))
                return [];

            foreach (var item in _columns)
                item.Width = width;
            Recalculate();
            return Placements();
        }

        if (Is(argument, "+conf"))
            StepConfigured(column, forward: true);
        else if (Is(argument, "-conf"))
            StepConfigured(column, forward: false);
        else if (argument.Length > 0 && (argument[0] == '+' || argument[0] == '-'))
        {
            if (TryRelative(argument, out var delta))
                column.Width += delta;
        }
        else if (TryDouble(argument, out var absolute))
        {
            column.Width = absolute;
        }

        column.Width = Math.Clamp(column.Width, ScrollingOptions.MinColumnWidth, ScrollingOptions.MaxColumnWidth);
        CenterOrFit(column);
        Recalculate();
        return Placements();
    }

    private void StepConfigured(Column column, bool forward)
    {
        var widths = _scrolling.ExplicitColumnWidths;
        if (widths.Count == 0)
            return;

        if (forward)
        {
            for (var i = 0; i < widths.Count; i++)
            {
                if (widths[i] > column.Width)
                {
                    column.Width = widths[i];
                    return;
                }

                if (i == widths.Count - 1)
                    column.Width = widths[0];
            }

            return;
        }

        for (var i = widths.Count - 1; i >= 0; i--)
        {
            if (widths[i] < column.Width)
            {
                column.Width = widths[i];
                return;
            }

            if (i == 0)
                column.Width = widths[^1];
        }
    }

    private IReadOnlyList<Placement> FocusMessage(WindowId? active, string direction)
    {
        if (active is not WindowId window || direction.Length == 0 || CellFor(window) is null)
        {
            Fail("no window to focus");
            return [];
        }

        if (!FocusToward(window, direction, out var next) || next is not WindowId focused)
            return Placements();

        LayoutMessageFocus = focused;
        return Placements();
    }

    // Without commit this only finds the Client: the strip does not scroll and nothing is remembered.
    private bool FocusToward(WindowId active, string direction, out WindowId? next, bool commit = true, bool wrap = true)
    {
        next = null;
        if (direction.Length == 0 || CellFor(active) is not Cell cell || cell.Column is not Column column)
            return false;

        var token = char.ToLowerInvariant(direction[0]);
        var vertical = !IsHorizontal();
        var previousInColumn = (!vertical && token is 'u' or 't') || (vertical && token == 'l');
        var nextInColumn = (!vertical && token is 'b' or 'd') || (vertical && token == 'r');
        var previousColumn = (!vertical && token == 'l') || (vertical && token is 'u' or 't');
        var nextColumn = (!vertical && token == 'r') || (vertical && token is 'b' or 'd');
        if (previousInColumn)
        {
            if (!wrap && PreviousCell(column, cell) is null)
                return false;

            next = Choose(column, PreviousCell(column, cell) ?? column.Cells[^1], commit);
            return next is not null;
        }

        if (nextInColumn)
        {
            if (!wrap && NextCell(column, cell) is null)
                return false;

            next = Choose(column, NextCell(column, cell) ?? column.Cells[0], commit);
            return next is not null;
        }

        if (!previousColumn && !nextColumn)
            return false;

        var neighbor = previousColumn ? Previous(column) : Next(column);
        if (neighbor is null && !wrap)
            return false;

        if (neighbor is null)
        {
            if (_scrolling.WrapFocus)
                neighbor = previousColumn ? _columns[^1] : _columns[0];
            else
                neighbor = previousColumn ? _columns[0] : _columns[^1];
        }

        var target = BestNeighbor(cell, neighbor);
        if (target is null)
            return false;

        if (!commit)
        {
            next = WindowOf(target);
            return next is not null;
        }

        // movefocus scrolls to the column only with scrolling:follow_focus. layoutmsg focus always does.
        if (wrap || _scrolling.FollowFocus)
        {
            CenterOrFit(neighbor);
            Recalculate();
            _moved = true;
        }

        next = Remember(neighbor, target);
        return next is not null;
    }

    private WindowId? Choose(Column column, Cell? chosen, bool commit) =>
        commit ? Remember(column, chosen) : WindowOf(chosen);

    private WindowId? Remember(Column? column, Cell? chosen)
    {
        var window = WindowOf(chosen);
        if (window is not WindowId id)
            return null;

        _focus = id;
        if (column is not null)
            column.LastFocused = chosen;
        return id;
    }

    private static Cell? BestNeighbor(Cell current, Column? target)
    {
        if (target is null || target.Cells.Count == 0)
            return null;

        var top = current.Logical.Y;
        var bottom = top + current.Logical.Height;
        Cell? topmost = null;
        foreach (var candidate in target.Cells)
        {
            var candidateTop = candidate.Logical.Y;
            var candidateBottom = candidateTop + candidate.Logical.Height;
            if (!(candidateTop < bottom && candidateBottom > top))
                continue;
            if (ReferenceEquals(target.LastFocused, candidate))
                return candidate;
            if (topmost is null || candidate.Logical.Y < topmost.Logical.Y)
                topmost = candidate;
        }

        return topmost ?? target.Cells[0];
    }

    private IReadOnlyList<Placement> ColumnMessage(WindowId? active, string[] tokens)
    {
        if (active is not WindowId window || CellFor(window) is not Cell cell || cell.Column is not Column column)
        {
            Fail("no window focused");
            return [];
        }

        var name = tokens[0];
        var index = _columns.IndexOf(column);
        if (Is(name, "promote"))
        {
            Expel(cell, index);
        }
        else if (Is(name, "expel"))
        {
            if (column.Cells.Count < 2)
            {
                Fail("column has only one window");
                return [];
            }

            var last = column.Cells[^1];
            Expel(last, Next(column) is null ? null : index);
        }
        else if (Is(name, "consume"))
        {
            var next = Next(column);
            if (next is null || next.Cells.Count == 0)
            {
                Fail("no next column");
                return [];
            }

            var taken = next.Cells[0];
            RemoveCell(taken);
            AddCell(column, taken);
            CenterOrFit(column);
        }
        else if (Is(name, "consume_or_expel"))
        {
            if (tokens.Length < 2)
            {
                Fail("not enough args");
                return [];
            }

            var previous = Is(At(tokens, 1), "prev");
            var nextward = Is(At(tokens, 1), "next");
            if (!previous && !nextward)
            {
                Fail("invalid direction, expected prev or next");
                return [];
            }

            if (column.Cells.Count > 1)
            {
                Expel(cell, previous ? index - 1 : index);
            }
            else
            {
                var adjacent = previous ? Previous(column) : Next(column);
                if (adjacent is null)
                {
                    Fail("no adjacent column");
                    return [];
                }

                RemoveCell(cell);
                AddCell(adjacent, cell);
                CenterOrFit(adjacent);
            }
        }

        Recalculate();
        return Placements();
    }

    private void Expel(Cell cell, int? after)
    {
        var index = after is int slot ? slot + 1 : _columns.Count;
        var created = InsertAt(index, null);
        RemoveCell(cell);
        AddCell(created, cell);
        CenterOrFit(created);
    }

    private IReadOnlyList<Placement> SwapColumns(WindowId? active, string[] tokens)
    {
        if (tokens.Length < 2)
        {
            Fail("not enough args");
            return [];
        }

        if (active is not WindowId window || CellFor(window) is not Cell cell || cell.Column is not Column column)
        {
            Fail("no window");
            return [];
        }

        if (_columns.Count < 2)
        {
            Fail("not enough columns to swap");
            return [];
        }

        var index = _columns.IndexOf(column);
        if (index < 0)
        {
            Fail("no current column");
            return [];
        }

        int target;
        if (Is(At(tokens, 1), "l"))
            target = _scrolling.WrapSwapCol ? (index == 0 ? _columns.Count - 1 : index - 1) : index == 0 ? 0 : index - 1;
        else if (Is(At(tokens, 1), "r"))
            target = _scrolling.WrapSwapCol
                ? (index == _columns.Count - 1 ? 0 : index + 1)
                : index == _columns.Count - 1 ? _columns.Count - 1 : index + 1;
        else
        {
            Fail("no target (invalid direction?)");
            return [];
        }

        (_columns[index], _columns[target]) = (_columns[target], _columns[index]);
        CenterOrFit(column);
        Recalculate();
        return Placements();
    }

    private bool MoveCell(Cell cell, Side side)
    {
        if (cell.Column is not Column column)
            return false;

        var index = _columns.IndexOf(column);
        if (side == Side.Left)
        {
            var previous = Previous(column);
            if (previous is null && index == 0 && column.Cells.Count == 1)
                return false;

            RemoveCell(cell);
            if (previous is null)
            {
                var created = InsertAt(0, null);
                AddCell(created, cell);
                CenterOrFit(created);
            }
            else
            {
                AddCell(previous, cell, IndexForHeight(previous, cell.Logical.Y));
                CenterOrFit(previous);
            }

            return true;
        }

        if (side == Side.Right)
        {
            var next = Next(column);
            if (next is null && index == _columns.Count - 1 && column.Cells.Count == 1)
                return false;

            RemoveCell(cell);
            if (next is null)
            {
                var created = Append(null);
                AddCell(created, cell);
                CenterOrFit(created);
            }
            else
            {
                AddCell(next, cell, IndexForHeight(next, cell.Logical.Y));
                CenterOrFit(next);
            }

            return true;
        }

        var slot = column.Cells.IndexOf(cell);
        if (side == Side.Up)
        {
            if (slot <= 0)
                return false;
            SwapSlots(column, slot, slot - 1);
            return true;
        }

        if (slot < 0 || slot >= column.Cells.Count - 1)
            return false;

        SwapSlots(column, slot, slot + 1);
        return true;
    }

    private static int IndexForHeight(Column column, int y)
    {
        if (column.Cells.Count == 0)
            return -1;

        for (var i = 0; i < column.Cells.Count; i++)
        {
            if (column.Cells[i].Logical.Y < y)
                continue;
            return i == 0 ? 0 : i - 1;
        }

        return column.Cells.Count - 1;
    }

    private static void SwapSlots(Column column, int first, int second)
    {
        (column.Cells[first], column.Cells[second]) = (column.Cells[second], column.Cells[first]);
        (column.Cells[first].Size, column.Cells[second].Size) = (column.Cells[second].Size, column.Cells[first].Size);
    }

    // The grabbed bottom edge trades with the next row and the grabbed top edge with the previous one.
    // With no corner the row trades with the previous row when there is one, so a positive delta still grows it.
    private void ResizeRow(Cell cell, int delta, (bool Left, bool Top)? corner)
    {
        var usable = SecondaryLength();
        if (cell.Column is not Column column || column.Cells.Count < 2 || usable <= 0)
            return;

        var next = NextCell(column, cell);
        var previous = PreviousCell(column, cell);
        var toward = (double)delta;
        var top = corner is { Top: true };
        if (corner is null && previous is not null)
        {
            top = true;
            toward = -toward;
        }

        if (!top && next is not null)
        {
            if (next.Size <= MinRow && toward >= 0)
                return;

            Trade(cell, next, toward / usable);
        }
        else if (top && previous is not null)
        {
            if ((previous.Size <= MinRow && toward <= 0) || (cell.Size <= MinRow && delta >= 0))
                return;

            Trade(previous, cell, toward / usable);
        }
    }

    // Moves the edge between two rows by a fraction of the column. Neither row goes below the smallest row.
    private static void Trade(Cell upper, Cell lower, double fraction)
    {
        var lo = -(upper.Size - MinRow);
        var hi = lower.Size - MinRow;

        // Eleven or more equal rows are each below the smallest row, so neither can give.
        if (lo > hi)
            return;

        var adjust = Math.Clamp(fraction, lo, hi);
        upper.Size = Math.Clamp(upper.Size + adjust, MinRow, MaxRow);
        lower.Size = Math.Clamp(lower.Size - adjust, MinRow, MaxRow);
    }

    private Side Rotate(Side side) => _scrolling.Direction switch
    {
        ScrollingDirection.Left => side switch
        {
            Side.Left => Side.Right,
            Side.Right => Side.Left,
            _ => side,
        },
        ScrollingDirection.Up => side switch
        {
            Side.Up => Side.Right,
            Side.Down => Side.Left,
            Side.Left => Side.Up,
            Side.Right => Side.Down,
            _ => side,
        },
        ScrollingDirection.Down => side switch
        {
            Side.Up => Side.Left,
            Side.Down => Side.Right,
            Side.Left => Side.Up,
            Side.Right => Side.Down,
            _ => side,
        },
        _ => side,
    };

    private IReadOnlyList<Placement> Tile(ClientId id, WindowId window)
    {
        _floating.Sink(id);
        SetFloating(id, false);
        InsertTiled(id, window);
        _focus = window;
        Recalculate();
        return Placements();
    }
}
