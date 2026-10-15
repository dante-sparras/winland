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
}
