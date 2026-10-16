using Winland.Windows;

namespace Winland.Tests;

/// <summary>Outcomes decided in one order and applied in another, as when a pipe thread and the main thread race.</summary>
public class ApplyOrderTests
{
    private static readonly WindowId First = new(1);
    private static readonly WindowId Second = new(2);
    private static readonly State Clients = new([new Client(new ClientId(1), First), new Client(new ClientId(2), Second)]);

    [Fact]
    public void OutcomesInOrderPassUnchanged()
    {
        var order = new ApplyOrder();
        var older = Decided(1) with { Focus = First };
        var newer = Decided(2) with { Focus = Second };

        var admitted = order.Admit(older);
        Assert.Same(older, admitted.Outcome);
        Assert.True(admitted.Current);
        Assert.Same(newer, order.Admit(newer).Outcome);
    }

    [Fact]
    public void AnOlderHideDoesNotUndoANewerShow()
    {
        var order = new ApplyOrder();
        order.Admit(Decided(2) with { Show = [First] });

        var admitted = order.Admit(Decided(1) with { Hide = [First, Second] });

        Assert.False(admitted.Current);
        Assert.Equal([Second], admitted.Outcome.Hide);
    }

    [Fact]
    public void AnOlderPlacementDoesNotMoveAWindowANewerOnePlaced()
    {
        var order = new ApplyOrder();
        order.Admit(Decided(2) with { Placements = [new Placement(new ClientId(1), new Rectangle(0, 0, 10, 10))] });

        var admitted = order.Admit(Decided(1) with
        {
            Placements =
            [
                new Placement(new ClientId(1), new Rectangle(5, 5, 10, 10)),
                new Placement(new ClientId(2), new Rectangle(20, 0, 10, 10)),
            ],
        });

        Assert.Equal([new Placement(new ClientId(2), new Rectangle(20, 0, 10, 10))], admitted.Outcome.Placements);
    }

    [Fact]
    public void AnOlderFocusLosesOnlyToANewerFocus()
    {
        var order = new ApplyOrder();
        order.Admit(Decided(2));
        Assert.Equal(First, order.Admit(Decided(1) with { Focus = First }).Outcome.Focus);

        order.Admit(Decided(4) with { Focus = Second });
        Assert.Null(order.Admit(Decided(3) with { Focus = First }).Outcome.Focus);
    }

    [Fact]
    public void AnOlderOutcomeStillCarriesOutItsOneTimeRequests()
    {
        var order = new ApplyOrder();
        order.Admit(Decided(2) with { Show = [First] });
        var launch = new Launch("app", null, false);

        var admitted = order.Admit(Decided(1) with { Close = First, Launch = launch, Kill = new KillRequest(7) });

        Assert.Equal(First, admitted.Outcome.Close);
        Assert.Same(launch, admitted.Outcome.Launch);
        Assert.Equal(new KillRequest(7), admitted.Outcome.Kill);
    }

    [Fact]
    public void EachCoreKeepsItsOwnOrder()
    {
        var order = new ApplyOrder();
        order.Admit(Decided(5, core: 1) with { Show = [First] });

        var other = order.Admit(Decided(1, core: 2) with { Hide = [First] });

        Assert.True(other.Current);
        Assert.Equal([First], other.Outcome.Hide);
    }

    [Fact]
    public void AnOutcomeMadeOutsideACoreIsApplied()
    {
        var order = new ApplyOrder();
        order.Admit(Decided(2) with { Show = [First] });
        var loose = new Outcome([], Clients) { Hide = [First] };

        var admitted = order.Admit(loose);

        Assert.True(admitted.Current);
        Assert.Same(loose, admitted.Outcome);
    }

    private static Outcome Decided(long sequence, long core = 1) =>
        new([], Clients) { Order = new OutcomeOrder(core, sequence) };
}
