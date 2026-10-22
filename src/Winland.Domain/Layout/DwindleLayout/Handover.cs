namespace Winland.Domain;

internal sealed partial class DwindleLayout
{
    private protected override void Rename(ClientId from, ClientId to)
    {
        if (_nodes.TryGetValue(from, out var node))
            node.Id = to;
        if (_suspended.TryGetValue(from, out var suspension))
            suspension.Node.Id = to;

        SlotHandover.Move(_nodes, from, to);
        SlotHandover.Move(_suspended, from, to);
        if (_splitTarget == from)
            _splitTarget = to;
        _drag.Forget(from);
    }
}
