using System.Globalization;
using static Winland.OrdinalText;

namespace Winland;

internal sealed partial class ScrollingLayout
{
    public WindowId? MoveFocus(WindowId active, string arguments)
    {
        if (!FocusToward(active, arguments, out var next))
            return null;

        return next;
    }

    public IReadOnlyList<Placement> MoveWindow(WindowId window, string arguments)
    {
        if (!TrySide(First(arguments), out var side))
            return [];

        var id = new ClientId(window.Value);
        if (Minimized(id))
            return [];

        if (_floating.TryGetValue(id, out var floating))
            return MoveFloating(id, floating, side);

        if (CellFor(window) is not Cell cell || cell.Column is null)
            return [];

        if (!MoveCell(cell, Rotate(side)))
            return [];

        _focus = window;
        Recalculate();
        return Placements();
    }

    public IReadOnlyList<Placement> ResizeActive(WindowId window, string arguments)
    {
        if (!TryResize(arguments, out var x, out var y, out var exact))
            return [];

        var id = new ClientId(window.Value);
        if (Minimized(id))
            return [];

        if (_floating.TryGetValue(id, out var floating))
            return ResizeFloating(id, floating, x, y, exact);

        if (CellFor(window) is not Cell cell || cell.Column is not Column column)
            return [];

        var horizontal = IsHorizontal();
        var primaryDelta = horizontal ? x : y;
        var secondaryDelta = horizontal ? y : x;
        var primary = PrimaryLength();
        var secondary = SecondaryLength();
        if (primary > 0)
        {
            var fraction = exact ? (primaryDelta / primary) - column.Width : primaryDelta / primary;
            column.Width = Math.Clamp(column.Width + fraction, ScrollingOptions.MinColumnWidth, ScrollingOptions.MaxColumnWidth);
        }

        if (secondary > 0)
            ResizeRow(cell, exact ? secondaryDelta - (cell.Size * secondary) : secondaryDelta, secondary);

        CenterOrFit(column);
        Recalculate();
        return Placements();
    }

    public IReadOnlyList<Placement> ToggleFloating(WindowId window)
    {
        var id = new ClientId(window.Value);
        var index = _clients.FindIndex(client => client.Id == id);
        if (index < 0 || _clients[index].Minimized)
            return [];

        if (_floating.ContainsKey(id))
            return Tile(id, window);

        if (!_cells.Remove(id, out var cell))
            return [];

        var tile = cell.Logical.Width > 0 ? cell.Logical : cell.Box;
        RemoveCell(cell);
        var floated = FloatingBounds(id, tile);
        _floating[id] = floated;
        SetFloating(id, true);
        Recalculate();
        var placements = new List<Placement>(Placements()) { new(id, floated) };
        return placements;
    }

    public IReadOnlyList<Placement> LayoutMessage(WindowId? active, string arguments)
    {
        LayoutMessageError = null;
        LayoutMessageFocus = null;
        var tokens = Tokens(arguments);
        if (tokens.Length == 0)
        {
            Fail("bad layoutmsg");
            return [];
        }

        if (Is(tokens[0], "move"))
            return MoveTape(active, At(tokens, 1));
        if (Is(tokens[0], "colresize"))
            return ResizeColumn(active, tokens);
        if (Is(tokens[0], "fit"))
            return FitMessage(active, At(tokens, 1));
        if (Is(tokens[0], "focus"))
            return FocusMessage(active, At(tokens, 1));
        if (Is(tokens[0], "promote") || Is(tokens[0], "consume") || Is(tokens[0], "expel") || Is(tokens[0], "consume_or_expel"))
            return ColumnMessage(active, tokens);
        if (Is(tokens[0], "swapcol"))
            return SwapColumns(active, tokens);
        if (Is(tokens[0], "center"))
            return CenterMessage(active);
        if (Is(tokens[0], "inhibit_scroll"))
            return InhibitMessage(tokens);
        if (Is(tokens[0], "fit_into_view"))
            return FitIntoView(active);

        Fail("no such layoutmsg for scrolling");
        return [];
    }

    public IReadOnlyList<Placement> Drag(WindowId window, PointerDrag drag)
    {
        return drag.Phase switch
        {
            PointerDragPhase.Begin => BeginDrag(window, drag.Pointer, drag.Resize),
            PointerDragPhase.Move => MoveDrag(drag.Pointer, drag.Resize),
            PointerDragPhase.End => EndDrag(drag.Pointer),
            _ => [],
        };
    }

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

        _offset -= delta;
        Recalculate();
        var center = ColumnAtCenter();
        if (center is { Cells.Count: > 0 })
            Focus(center.Cells[0]);
        return Placements();
    }

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
        Column? used = null;
        foreach (var column in _columns)
        {
            if (!ReferenceEquals(column, current) && ColumnVisible(column, full: false))
                others += column.Width;
            else
                used = column;
        }

        var remaining = Math.Clamp(1 - others, 0, 1);
        if (remaining == current.Width)
            return;

        current.Width = remaining;
        SetOffset(0);
        CenterOrFit(used);
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

    private bool FocusToward(WindowId active, string direction, out WindowId? next)
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
            next = Remember(column, PreviousCell(column, cell) ?? column.Cells[^1]);
            return next is not null;
        }

        if (nextInColumn)
        {
            next = Remember(column, NextCell(column, cell) ?? column.Cells[0]);
            return next is not null;
        }

        if (!previousColumn && !nextColumn)
            return false;

        var neighbor = previousColumn ? Previous(column) : Next(column);
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

        CenterOrFit(neighbor);
        Recalculate();
        next = Remember(neighbor, target);
        return next is not null;
    }

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

    private Cell? BestNeighbor(Cell current, Column? target)
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

        CenterOrFit(cell.Column);
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

    private void ResizeRow(Cell cell, double delta, double usable)
    {
        if (cell.Column is not Column column || column.Cells.Count < 2 || usable <= 0)
            return;

        var index = column.Cells.IndexOf(cell);
        var neighbor = index + 1 < column.Cells.Count ? index + 1 : index - 1;
        if (neighbor < 0)
            return;

        var sign = neighbor > index ? 1 : -1;
        var other = column.Cells[neighbor];
        var adjust = Math.Clamp(sign * delta / usable, -(cell.Size - MinRow), other.Size - MinRow);
        cell.Size = Math.Clamp(cell.Size + adjust, MinRow, MaxRow);
        other.Size = Math.Clamp(other.Size - adjust, MinRow, MaxRow);
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
        if (_floating.TryGetValue(id, out var floating) && floating.Width > 0 && floating.Height > 0)
            _lastFloat[id] = (floating.Width, floating.Height);

        _floating.Remove(id);
        SetFloating(id, false);
        InsertTiled(id, window);
        _focus = window;
        Recalculate();
        return Placements();
    }

    private Rectangle FloatingBounds(ClientId id, Rectangle tile)
    {
        var width = 640;
        var height = 400;
        if (_lastFloat.TryGetValue(id, out var last) && last.Width >= 5 && last.Height >= 5)
        {
            width = last.Width;
            height = last.Height;
        }

        if (Math.Abs(width - tile.Width) < 5 && Math.Abs(height - tile.Height) < 5)
        {
            width += 10;
            height += 10;
        }

        var floated = new Rectangle(
            tile.X + (tile.Width / 2) - (width / 2),
            tile.Y + (tile.Height / 2) - (height / 2),
            width,
            height);
        return FitOnArea(floated, _layoutOptions.FloatingWorkArea(_area));
    }

    private static Rectangle FitOnArea(Rectangle box, Rectangle work)
    {
        if (work.Width <= 0 || work.Height <= 0)
            return box;

        var x = Math.Max(box.X, work.X);
        var y = Math.Max(box.Y, work.Y);
        if (x + box.Width > work.X + work.Width)
            x = work.X + work.Width - box.Width;
        if (y + box.Height > work.Y + work.Height)
            y = work.Y + work.Height - box.Height;
        return new Rectangle(x, y, box.Width, box.Height);
    }

    private IReadOnlyList<Placement> MoveFloating(ClientId id, Rectangle box, Side side)
    {
        var frame = _layoutOptions.FloatingWorkArea(_area);
        var moved = side switch
        {
            Side.Left => box with { X = frame.X },
            Side.Right => box with { X = frame.X + frame.Width - box.Width },
            Side.Up => box with { Y = frame.Y },
            Side.Down => box with { Y = frame.Y + frame.Height - box.Height },
            _ => box,
        };
        if (moved == box)
            return [];

        _floating[id] = moved;
        return [new Placement(id, moved)];
    }

    private IReadOnlyList<Placement> ResizeFloating(ClientId id, Rectangle box, int x, int y, bool exact)
    {
        var deltaX = exact ? x - box.Width : x;
        var deltaY = exact ? y - box.Height : y;
        var resized = new Rectangle(box.X - (deltaX / 2), box.Y - (deltaY / 2), box.Width + deltaX, box.Height + deltaY);
        if (resized.Width < 1 || resized.Height < 1)
            return [];

        _floating[id] = resized;
        return [new Placement(id, resized)];
    }

    private IReadOnlyList<Placement> BeginDrag(WindowId window, Point pointer, bool resize)
    {
        var id = new ClientId(window.Value);
        if (!TryDragBounds(id, out var bounds, out var floating))
        {
            _grab = null;
            return [];
        }

        var (left, top) = BorderPointer.GrabbedCorner(_layoutOptions.ResizeCorner, floating, pointer, bounds);
        _grab = new MouseGrab(id, resize, floating, pointer, bounds, left, top);
        return [];
    }

    private IReadOnlyList<Placement> MoveDrag(Point pointer, bool resize)
    {
        if (_grab is not MouseGrab grab || grab.Resize != resize)
            return [];

        if (resize)
            return grab.WasFloating ? ResizeDraggedFloating(grab, pointer) : ResizeDraggedTiled(grab, pointer);

        if (!grab.WasFloating && !grab.Lifted && !Lift(grab))
            return [];

        return Follow(grab, pointer);
    }

    private IReadOnlyList<Placement> EndDrag(Point pointer)
    {
        _dragging = false;
        if (_grab is not MouseGrab grab)
            return [];

        _grab = null;
        if (grab.Lifted)
            return DropTiled(grab.Id, pointer);

        if (grab.WasFloating && _floating.ContainsKey(grab.Id))
            return WithFloating(grab.Id);

        Recalculate();
        return Placements();
    }

    private bool Lift(MouseGrab grab)
    {
        if (!_cells.Remove(grab.Id, out var cell))
            return false;

        var box = cell.Logical.Width > 0 ? cell.Logical : cell.Box;
        RemoveCell(cell);
        SetFloating(grab.Id, true);
        _floating[grab.Id] = box;
        grab.Lifted = true;
        grab.Start = box;
        grab.HotX = grab.Grab.X - box.X;
        grab.HotY = grab.Grab.Y - box.Y;
        Recalculate();
        return true;
    }

    private IReadOnlyList<Placement> Follow(MouseGrab grab, Point pointer)
    {
        var next = new Rectangle(pointer.X - grab.HotX, pointer.Y - grab.HotY, grab.Start.Width, grab.Start.Height);
        next = SnapFloating(grab.Id, next, SnapEdges.All, resize: false);
        _floating[grab.Id] = next;
        return WithFloating(grab.Id);
    }

    private IReadOnlyList<Placement> ResizeDraggedFloating(MouseGrab grab, Point pointer)
    {
        var dx = pointer.X - grab.Grab.X;
        var dy = pointer.Y - grab.Grab.Y;
        var width = Math.Max(1, grab.Left ? grab.Start.Width - dx : grab.Start.Width + dx);
        var height = Math.Max(1, grab.Top ? grab.Start.Height - dy : grab.Start.Height + dy);
        var x = grab.Left ? grab.Start.X + grab.Start.Width - width : grab.Start.X;
        var y = grab.Top ? grab.Start.Y + grab.Start.Height - height : grab.Start.Y;
        var next = SnapFloating(grab.Id, new Rectangle(x, y, width, height), SnapEdges.FromGrab(grab.Left, grab.Top), resize: true);
        _floating[grab.Id] = next;
        return WithFloating(grab.Id);
    }

    public IReadOnlyList<Placement> PlaceFloating(WindowId window, Rectangle bounds)
    {
        var id = new ClientId(window.Value);
        if (!_floating.ContainsKey(id) || bounds.Width < 1 || bounds.Height < 1)
            return [];

        _floating[id] = bounds;
        return WithFloating(id);
    }

    private Rectangle SnapFloating(ClientId self, Rectangle proposed, SnapEdges edges, bool resize)
    {
        if (!_layoutOptions.SnapEnabled)
            return proposed;

        var others = new List<Rectangle>();
        foreach (var client in _clients)
        {
            if (client.Id == self || client.Minimized)
                continue;

            if (TryBounds(client.Window, out var bounds))
                others.Add(bounds);
        }

        var monitor = _monitor.Width > 0 && _monitor.Height > 0 ? _monitor : _area;
        return FloatingSnap.Apply(proposed, edges, resize, others, _area, monitor, _layoutOptions);
    }

    private IReadOnlyList<Placement> ResizeDraggedTiled(MouseGrab grab, Point pointer)
    {
        var dx = pointer.X - grab.Last.X;
        var dy = pointer.Y - grab.Last.Y;
        grab.Last = pointer;
        if (dx == 0 && dy == 0)
            return [];

        _dragging = true;
        return ResizeActive(grab.Window, string.Create(CultureInfo.InvariantCulture, $"{dx} {dy}"));
    }

    private IReadOnlyList<Placement> DropTiled(ClientId id, Point pointer)
    {
        _floating.Remove(id);
        SetFloating(id, false);
        var window = new WindowId(id.Value);
        var cell = new Cell(id);
        _cells[id] = cell;
        var target = ColumnAt(pointer);
        if (target is null)
        {
            var created = Append(null);
            AddCell(created, cell);
            FitColumn(created);
        }
        else if (OnEdge(target, pointer, out var before))
        {
            var index = _columns.IndexOf(target);
            var created = InsertAt(before ? index : index + 1, null);
            AddCell(created, cell);
            FitColumn(created);
        }
        else
        {
            AddCell(target, cell, IndexForHeight(target, pointer.Y));
            FitColumn(target);
        }

        _focus = window;
        Recalculate();
        return Placements();
    }

    private Column? ColumnAt(Point pointer)
    {
        Column? closest = null;
        var best = double.MaxValue;
        foreach (var column in _columns)
        {
            if (column.Cells.Count == 0)
                continue;

            var box = column.Cells[0].Logical;
            foreach (var cell in column.Cells)
            {
                if (Intersects(cell.Logical, new Rectangle(pointer.X, pointer.Y, 1, 1)))
                    return column;
            }

            var dx = pointer.X < box.X ? box.X - pointer.X : pointer.X >= box.X + box.Width ? pointer.X - (box.X + box.Width) : 0;
            var dy = pointer.Y < box.Y ? box.Y - pointer.Y : pointer.Y >= box.Y + box.Height ? pointer.Y - (box.Y + box.Height) : 0;
            var distance = (dx * (double)dx) + (dy * (double)dy);
            if (distance < best)
            {
                best = distance;
                closest = column;
            }
        }

        return closest;
    }

    private bool OnEdge(Column column, Point pointer, out bool before)
    {
        before = false;
        if (column.Cells.Count == 0)
            return false;

        var box = column.Cells[0].Logical;
        double origin;
        double size;
        double point;
        if (IsHorizontal())
        {
            origin = box.X;
            size = box.Width;
            point = pointer.X;
        }
        else
        {
            origin = box.Y;
            size = box.Height;
            point = pointer.Y;
        }

        if (size <= 0)
            return false;

        before = point <= origin + (size * 0.3);
        return before || point >= origin + (size * 0.7);
    }

    private bool TryDragBounds(ClientId id, out Rectangle bounds, out bool floating)
    {
        bounds = default;
        floating = false;
        var index = _clients.FindIndex(client => client.Id == id);
        if (index < 0 || _clients[index].Minimized)
            return false;

        if (_floating.TryGetValue(id, out bounds))
        {
            floating = true;
            return true;
        }

        if (_cells.TryGetValue(id, out var cell))
        {
            bounds = cell.Logical.Width > 0 ? cell.Logical : cell.Box;
            return bounds.Width > 0 && bounds.Height > 0;
        }

        return false;
    }

    private IReadOnlyList<Placement> WithFloating(ClientId id)
    {
        var placements = new List<Placement>(Placements());
        if (_floating.TryGetValue(id, out var box))
            placements.Add(new Placement(id, box));
        return placements;
    }

    private void Focus(Cell cell)
    {
        var window = WindowOf(cell);
        if (window is not WindowId id)
            return;

        _focus = id;
        LayoutMessageFocus = id;
        if (cell.Column is Column column)
            column.LastFocused = cell;
    }

    private WindowId? WindowOf(Cell? cell)
    {
        if (cell is null)
            return null;

        foreach (var client in _clients)
        {
            if (client.Id == cell.Id)
                return client.Window;
        }

        return null;
    }

    private Column? Next(Column column)
    {
        var index = _columns.IndexOf(column);
        return index >= 0 && index + 1 < _columns.Count ? _columns[index + 1] : null;
    }

    private Column? Previous(Column column)
    {
        var index = _columns.IndexOf(column);
        return index > 0 ? _columns[index - 1] : null;
    }

    private static Cell? NextCell(Column column, Cell cell)
    {
        var index = column.Cells.IndexOf(cell);
        return index >= 0 && index + 1 < column.Cells.Count ? column.Cells[index + 1] : null;
    }

    private static Cell? PreviousCell(Column column, Cell cell)
    {
        var index = column.Cells.IndexOf(cell);
        return index > 0 ? column.Cells[index - 1] : null;
    }

    private bool Minimized(ClientId id)
    {
        var index = _clients.FindIndex(client => client.Id == id);
        return index >= 0 && _clients[index].Minimized;
    }

    private static string First(string arguments)
    {
        var tokens = Tokens(arguments);
        return tokens.Length == 0 ? "" : tokens[0];
    }

    private static bool TrySide(string text, out Side side)
    {
        if (text.Length == 0)
        {
            side = Side.Left;
            return false;
        }

        switch (char.ToLowerInvariant(text[0]))
        {
            case 'l':
                side = Side.Left;
                return true;
            case 'r':
                side = Side.Right;
                return true;
            case 'u':
            case 't':
                side = Side.Up;
                return true;
            case 'd':
            case 'b':
                side = Side.Down;
                return true;
            default:
                side = Side.Left;
                return false;
        }
    }

    private static bool TryResize(string arguments, out int x, out int y, out bool exact)
    {
        x = 0;
        y = 0;
        exact = false;
        var tokens = Tokens(arguments);
        var index = 0;
        if (tokens.Length > 0 && Is(tokens[0], "exact"))
        {
            exact = true;
            index = 1;
        }

        if (tokens.Length - index < 2)
            return false;

        return int.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
            && int.TryParse(tokens[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
    }

    private enum Side
    {
        Left,
        Right,
        Up,
        Down,
    }

    private sealed class MouseGrab
    {
        public MouseGrab(ClientId id, bool resize, bool wasFloating, Point grab, Rectangle start, bool left, bool top)
        {
            Id = id;
            Resize = resize;
            WasFloating = wasFloating;
            Grab = grab;
            Last = grab;
            Start = start;
            Left = left;
            Top = top;
            if (wasFloating)
            {
                HotX = grab.X - start.X;
                HotY = grab.Y - start.Y;
            }
        }

        public ClientId Id { get; }

        public WindowId Window => new(Id.Value);

        public bool Resize { get; }

        public bool WasFloating { get; }

        public Point Grab { get; }

        public Point Last { get; set; }

        public Rectangle Start { get; set; }

        public bool Left { get; }

        public bool Top { get; }

        public bool Lifted { get; set; }

        public int HotX { get; set; }

        public int HotY { get; set; }
    }
}
