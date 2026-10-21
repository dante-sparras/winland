using static Winland.OrdinalText;

namespace Winland;

internal sealed partial class ScrollingLayout
{
    // movefocus and the dispatchers that look for a neighbour do not wrap around the strip, as Winland
    // looks in a direction across Monitors there. Only layoutmsg focus follows scrolling:wrap_focus.
    public WindowId? MoveFocus(WindowId active, string arguments)
    {
        if (!FocusToward(active, arguments, out var next, wrap: false))
            return null;

        return next;
    }

    public WindowId? Neighbor(WindowId active, string arguments) =>
        FocusToward(active, arguments, out var next, commit: false, wrap: false) ? next : null;

    public IReadOnlyList<Placement> MoveWindow(WindowId window, string arguments)
    {
        if (!TrySide(First(arguments), out var side))
            return [];

        var id = new ClientId(window.Value);
        if (Minimized(id))
            return [];

        if (_floating.Contains(id))
            return _floating.MoveToEdge(id, side, _layoutOptions.FloatingWorkArea(_area), _layoutOptions.BorderFor(id));

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
        if (!ResizeArguments.TryParse(arguments, out var x, out var y, out var exact))
            return [];

        var id = new ClientId(window.Value);
        if (Minimized(id))
            return [];

        if (_floating.Contains(id))
            return _floating.Resize(id, x, y, exact, _layoutOptions.FloatingOnScreen(_area));

        if (CellFor(window) is not Cell cell || cell.Column is null)
            return [];

        // exact names the window's size, which is the cell less its gaps and border.
        if (exact)
        {
            x -= cell.Logical.Width;
            y -= cell.Logical.Height;
        }

        return ResizeCell(cell, x, y, corner: null);
    }

    /// <summary>
    /// Resizes a tiled cell by a pixel delta. A mouse resize names the grabbed corner; a dispatcher resize names none.
    /// The camera stays where it is. The column's far edge stays inside the viewport, and a grabbed near edge
    /// moves the camera so the far edge stays put. Rows take their space from the row on the grabbed side,
    /// or with no corner from the row before, when there is one.
    /// </summary>
    private IReadOnlyList<Placement> ResizeCell(Cell cell, int deltaX, int deltaY, (bool Left, bool Top)? corner)
    {
        if (cell.Column is not Column column || !TryStrip(column, out var start, out var size, out var primary))
            return [];

        var horizontal = IsHorizontal();
        var primaryDelta = horizontal ? deltaX : deltaY;
        var secondaryDelta = horizontal ? deltaY : deltaX;
        var onScreenStart = start - _offset;
        var onScreenEnd = start + size - _offset;
        SetOffset(_offset);

        var old = column.Width;
        var nearEdge = horizontal ? corner is { Left: true } : corner is { Top: true };
        if (nearEdge)
        {
            var actual = Math.Clamp(old - (primaryDelta / primary), ScrollingOptions.MinColumnWidth, ScrollingOptions.MaxColumnWidth) - old;
            if (actual * primary > onScreenStart)
                actual = onScreenStart / primary;

            if (actual != 0)
            {
                column.Width = old + actual;
                AdjustOffset(actual * primary);
            }
        }
        else
        {
            var actual = Math.Clamp(old + (primaryDelta / primary), ScrollingOptions.MinColumnWidth, ScrollingOptions.MaxColumnWidth) - old;
            if (onScreenEnd + (actual * primary) > primary)
                actual = (primary - onScreenEnd) / primary;

            if (actual != 0)
                column.Width = old + actual;
        }

        ResizeRow(cell, secondaryDelta, corner);
        Recalculate();
        return Placements();
    }

    public IReadOnlyList<Placement> ToggleFloating(WindowId window)
    {
        var id = new ClientId(window.Value);
        var index = _clients.FindIndex(client => client.Id == id);
        if (index < 0 || _clients[index].Minimized)
            return [];

        if (_floating.Contains(id))
            return Tile(id, window);

        if (!_cells.Remove(id, out var cell))
            return [];

        var tile = cell.Logical.Width > 0 ? cell.Logical : cell.Box;
        RemoveCell(cell);
        var floated = _floating.Around(id, tile, _layoutOptions.FloatingOnScreen(_area));
        _floating.Set(id, floated);
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
}
