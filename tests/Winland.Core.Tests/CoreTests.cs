using Winland;

namespace Winland.Tests;

public class CoreTests
{
    [Fact]
    public void AppearingWindowIsPlacedOnItsWorkArea()
    {
        var core = new Core();

        var outcome = core.Handle(new WindowAppeared(new WindowId(42), new Rectangle(0, 0, 1920, 1080)));

        var client = Assert.Single(outcome.State.Clients);
        var placement = Assert.Single(outcome.Placements);
        Assert.Equal(new WindowId(42), client.Window);
        Assert.Equal(client.Id, placement.Client);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), placement.Bounds);
    }
}
