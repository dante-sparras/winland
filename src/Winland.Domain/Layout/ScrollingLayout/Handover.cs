namespace Winland.Domain;

internal sealed partial class ScrollingLayout
{
    private protected override void Rename(ClientId from, ClientId to)
    {
        var current = new WindowId(from.Value);
        var next = new WindowId(to.Value);
        if (_cells.TryGetValue(from, out var cell))
            cell.Id = to;

        SlotHandover.Move(_cells, from, to);
        SlotHandover.Move(_suspended, from, to);
        _widths.Remove(current);
        if (_focus == current)
            _focus = next;
        if (_drag.Forget(from))
            _dragging = false;
    }
}
