namespace Winland.Domain;

internal sealed partial class MasterLayout
{
    private protected override void Rename(ClientId from, ClientId to)
    {
        var current = new WindowId(from.Value);
        var next = new WindowId(to.Value);
        if (Find(current) is { } tile)
            tile.Id = to;
        if (_suspended.TryGetValue(from, out var suspended))
            suspended.Tile.Id = to;

        SlotHandover.Move(_suspended, from, to);
        if (_focused == current)
            _focused = next;
        if (_previousFocus == current)
            _previousFocus = next;
        _drag.Forget(from);
    }
}
