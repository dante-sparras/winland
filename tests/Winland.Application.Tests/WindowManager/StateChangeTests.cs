namespace Winland.Application.Tests;

public class StateChangeTests
{
    private static readonly Client One = new(new ClientId(1), new WindowId(1));
    private static readonly Client Two = new(new ClientId(2), new WindowId(2), Workspace: WorkspaceId.Initial);
    private static readonly Client Three = new(new ClientId(3), new WindowId(3));

    [Fact]
    public void ClosedAndOpenedClientsKeepTheOrderTheirStatesListThem()
    {
        var change = StateChange.Between(new State([Three, One, Two]), new State([Two]));

        Assert.Equal([Three.Window, One.Window], change.Closed);
        Assert.Empty(change.Opened);

        var back = StateChange.Between(new State([Two]), new State([One, Two, Three]));
        Assert.Equal([One.Window, Three.Window], back.Opened);
    }

    [Fact]
    public void EachClientIsFoundBeforeAndAfterByWindow()
    {
        var moved = Two with { Workspace = new WorkspaceId("2") };

        var change = StateChange.Between(new State([One, Two]), new State([One, moved]));

        Assert.Equal(WorkspaceId.Initial, change.Was[Two.Window].Workspace);
        Assert.Equal(new WorkspaceId("2"), change.Is[Two.Window].Workspace);
    }

    [Fact]
    public void AClientWasShownWhenItWasThereAndNotHidden()
    {
        var before = new State([One, Two]) { Hidden = [Two] };

        var change = StateChange.Between(before, new State([One, Two, Three]) { Hidden = [One] });

        Assert.True(change.WasShown(One.Window));
        Assert.False(change.WasShown(Two.Window));
        Assert.False(change.WasShown(Three.Window));
        Assert.Contains(One.Window, change.HiddenAfter);
    }
}
