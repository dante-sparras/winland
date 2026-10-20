namespace Winland;

internal sealed partial class ScrollingLayout
{
    public bool Substitute(WindowId current, WindowId next, Taken incoming, out Taken outgoing)
    {
        outgoing = default;
        var from = new ClientId(current.Value);
        var to = new ClientId(next.Value);
        var index = _clients.FindIndex(client => client.Id == from);
        if (index < 0 || from == to || _clients.Exists(client => client.Id == to))
            return false;

        var client = _clients[index];
        var bounds = _floating.TryGet(from, out var floating)
            ? floating
            : _natural.TryGetValue(from, out var natural) ? natural : default;
        _limits.TryGetValue(from, out var limits);
        double? width = _cells.TryGetValue(from, out var cell) && cell.Column is Column column
            ? column.Width
            : _suspended.TryGetValue(from, out var suspension) ? suspension.Column.Width : null;
        outgoing = SlotHandover.Describe(client, bounds, limits, width);

        _clients[index] = SlotHandover.Rename(client, next, incoming);
        if (cell is not null)
            cell.Id = to;

        SlotHandover.Move(_cells, from, to);
        SlotHandover.Move(_suspended, from, to);
        _floating.Hand(from, to);
        _pseudo.Hand(from, to);
        _natural.Remove(from);
        _natural[to] = incoming.Bounds;
        _limits.Remove(from);
        _limits[to] = new SizeLimits(incoming.MinSize, incoming.MaxSize) { Tiled = incoming.TiledLimits };
        _widths.Remove(current);
        if (_focus == current)
            _focus = next;
        if (_drag.Forget(from))
            _dragging = false;

        return true;
    }
}
