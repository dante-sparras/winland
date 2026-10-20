namespace Winland;

internal sealed partial class MonocleLayout
{
    public bool Substitute(WindowId current, WindowId next, Taken incoming, out Taken outgoing)
    {
        outgoing = default;
        var from = new ClientId(current.Value);
        var to = new ClientId(next.Value);
        var index = IndexOf(from);
        if (index < 0 || from == to || IndexOf(to) >= 0)
            return false;

        var client = _clients[index];
        var bounds = _floating.TryGet(from, out var floating)
            ? floating
            : _natural.TryGetValue(from, out var natural) ? natural : default;
        _limits.TryGetValue(from, out var limits);
        outgoing = SlotHandover.Describe(client, bounds, limits);

        _clients[index] = SlotHandover.Rename(client, next, incoming);
        SlotHandover.Move(_tiled, from, to);
        _floating.Hand(from, to);
        _pseudo.Hand(from, to);
        _natural.Remove(from);
        _natural[to] = incoming.Bounds;
        _limits.Remove(from);
        _limits[to] = new SizeLimits(incoming.MinSize, incoming.MaxSize) { Tiled = incoming.TiledLimits };
        return true;
    }
}
