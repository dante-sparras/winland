namespace Winland;

internal sealed partial class MasterLayout
{
    public bool Substitute(WindowId current, WindowId next, Taken incoming, out Taken outgoing)
    {
        outgoing = default;
        var from = Id(current);
        var to = Id(next);
        var index = FindClient(from);
        if (index < 0 || from == to || FindClient(to) >= 0)
            return false;

        var client = _clients[index];
        var bounds = _floating.TryGet(from, out var floating)
            ? floating
            : _natural.TryGetValue(from, out var natural) ? natural : default;
        _limits.TryGetValue(from, out var limits);
        outgoing = SlotHandover.Describe(client, bounds, limits.Min, limits.Max);

        _clients[index] = SlotHandover.Rename(client, next, incoming);
        if (Find(current) is { } tile)
            tile.Id = to;
        if (_suspended.TryGetValue(from, out var suspended))
            suspended.Tile.Id = to;

        SlotHandover.Move(_suspended, from, to);
        _floating.Hand(from, to);
        _natural.Remove(from);
        _natural[to] = incoming.Bounds;
        _limits.Remove(from);
        _limits[to] = new SizeLimits(incoming.MinSize, incoming.MaxSize);
        if (_focused == current)
            _focused = next;
        if (_previousFocus == current)
            _previousFocus = next;
        if (_grab is { } grab && grab.Id == from)
            _grab = null;

        return true;
    }
}
