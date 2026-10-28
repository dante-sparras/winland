using System.Reflection;

namespace Winland.Application.Tests;

public class OutcomeThenTests
{
    private static readonly State Empty = new([]);

    [Fact]
    public void EveryPropertyHasARuleInThen()
    {
        var properties = typeof(Outcome).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // A new property changes this count. Give it a rule in Outcome.Then, then update the count.
        Assert.Equal(30, properties.Length);
    }

    [Fact]
    public void RequestsJoinInOrderAndTheStateIsTheSecondsOwn()
    {
        var first = new Outcome([], Empty) { Closes = [new WindowId(1)], Events = [new ClientClosed(new WindowId(1))] };
        var after = new State([]) { ActiveWorkspace = new WorkspaceId("2") };
        var second = new Outcome([], after) { Closes = [new WindowId(2)], Events = [new ClientClosed(new WindowId(2))] };

        var both = first.Then(second);

        Assert.Equal([new WindowId(1), new WindowId(2)], both.Closes);
        Assert.Equal(2, both.Events.Count);
        Assert.Same(after, both.State);
    }

    [Fact]
    public void TheTraitsOfBothAreKept()
    {
        var first = new Outcome([], Empty) { Traits = DispatcherTraits.WarpsToFocused };
        var second = new Outcome([], Empty) { Traits = DispatcherTraits.WarpsToEmptyMonitor };

        Assert.Equal(DispatcherTraits.WarpsToFocused | DispatcherTraits.WarpsToEmptyMonitor, first.Then(second).Traits);
        Assert.Equal(DispatcherTraits.WarpsToFocused, first.Then(new Outcome([], Empty)).Traits);
        Assert.Equal(DispatcherTraits.WarpsToEmptyMonitor, new Outcome([], Empty).Then(second).Traits);
        Assert.Null(new Outcome([], Empty).Then(new Outcome([], Empty)).Traits);
    }

    [Fact]
    public void TheFirstErrorWins()
    {
        var failed = new Outcome([], Empty) { Error = "error: no such workspace" };
        var later = new Outcome([], Empty) { Error = "error: window not found" };

        Assert.Equal("error: no such workspace", failed.Then(later).Error);
        Assert.Equal("error: window not found", new Outcome([], Empty).Then(later).Error);
    }

    [Fact]
    public void AValueTheSecondLeavesUnsetKeepsTheFirsts()
    {
        var warp = new WarpTo(new Point(5, 5));
        var first = new Outcome([], Empty, Focus: new WindowId(1)) { Warp = warp };

        var both = first.Then(new Outcome([], Empty));

        Assert.Equal(new WindowId(1), both.Focus);
        Assert.Equal(warp, both.Warp);
    }
}
