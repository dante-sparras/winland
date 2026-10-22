namespace Winland.Domain;

internal sealed partial class MonocleLayout
{
    private protected override void Rename(ClientId from, ClientId to)
    {
        SlotHandover.Move(_tiled, from, to);
    }
}
