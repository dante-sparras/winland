namespace Winland;

internal sealed partial class DwindleLayout
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
        outgoing = SlotHandover.Describe(client, bounds, limits);

        _clients[index] = SlotHandover.Rename(client, next, incoming);
        if (_nodes.TryGetValue(from, out var node))
            node.Id = to;
        if (_suspended.TryGetValue(from, out var suspension))
            suspension.Node.Id = to;

        SlotHandover.Move(_nodes, from, to);
        SlotHandover.Move(_suspended, from, to);
        _floating.Hand(from, to);
        SlotHandover.Move(_pseudo, from, to);
        _natural.Remove(from);
        _natural[to] = incoming.Bounds;
        _limits.Remove(from);
        _limits[to] = new SizeLimits(incoming.MinSize, incoming.MaxSize) { Tiled = incoming.TiledLimits };
        if (_splitTarget == from)
            _splitTarget = to;
        _drag.Forget(from);

        return true;
    }
}
