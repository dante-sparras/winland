namespace Winland.Application.Tests;

public class AdoptTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void WindowsOpenBeforeStartupAreTakenInAsOneStepThatPlacesEachOnce()
    {
        var core = new WindowManager();
        var decided = new List<Outcome>();
        core.Decided = decided.Add;

        var outcome = core.Adopt([Appeared(1), Appeared(2), Appeared(3)]);

        Assert.Equal([outcome], decided);
        Assert.Equal(3, outcome.Placements.Count);
        Assert.Equal(3, outcome.Placements.Select(placement => placement.Client).Distinct().Count());
    }

    [Fact]
    public void AdoptingTogetherEndsWhereAdoptingOneAtATimeDoes()
    {
        var oneAtATime = new WindowManager();
        oneAtATime.Handle(Appeared(1));
        oneAtATime.Handle(Appeared(2));
        var expected = oneAtATime.Handle(Appeared(3)).State;

        var outcome = new WindowManager().Adopt([Appeared(1), Appeared(2), Appeared(3)]);

        Assert.Equal(
            expected.Clients.Select(client => (client.Window, client.Bounds)),
            outcome.State.Clients.Select(client => (client.Window, client.Bounds)));
        foreach (var placement in outcome.Placements)
            Assert.Equal(outcome.State.Clients.Single(client => client.Id == placement.Client).Bounds, placement.Bounds);
    }

    private static WindowAppeared Appeared(long window) => new(new WindowId(window), Area);
}
