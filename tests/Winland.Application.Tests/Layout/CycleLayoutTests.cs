namespace Winland.Application.Tests;

public class CycleLayoutTests
{
    private static readonly Rectangle Area = new(0, 0, 800, 600);

    [Fact]
    public void CycleLayoutIsKnown() => Assert.Contains("cyclelayout", WindowManager.DispatcherNames);

    [Fact]
    public void CycleLayoutWalksDwindleScrollingAndFloating()
    {
        var core = new WindowManager();
        core.Handle(Appeared(1, Area));

        var scrolling = core.Handle(new Dispatch("cyclelayout", ""));
        var floating = core.Handle(new Dispatch("cyclelayout", ""));
        var dwindle = core.Handle(new Dispatch("cyclelayout", ""));

        Assert.Equal("scrolling", Layout(scrolling));
        Assert.False(Client(scrolling, 1).Floating);
        Assert.Equal("floating", Layout(floating));
        Assert.True(Client(floating, 1).Floating);
        Assert.Equal("dwindle", Layout(dwindle));
        Assert.False(Client(dwindle, 1).Floating);
    }

    [Theory]
    [InlineData("")]
    [InlineData("next")]
    [InlineData("prev")]
    public void OtherWordsGoForward(string arguments)
    {
        var core = new WindowManager();

        Assert.Equal("scrolling", Layout(core.Handle(new Dispatch("cyclelayout", arguments))));
    }

    [Theory]
    [InlineData("back")]
    [InlineData("BACK")]
    [InlineData("back now")]
    public void BackGoesTheOtherWay(string arguments)
    {
        var core = new WindowManager();
        core.Handle(Appeared(1, Area));

        var floating = core.Handle(new Dispatch("cyclelayout", arguments));
        var scrolling = core.Handle(new Dispatch("cyclelayout", "back"));

        Assert.Equal("floating", Layout(floating));
        Assert.True(Client(floating, 1).Floating);
        Assert.Equal("scrolling", Layout(scrolling));
        Assert.False(Client(scrolling, 1).Floating);
    }

    [Fact]
    public void AClientThatWasFloatingStaysFloating()
    {
        var core = new WindowManager();
        core.Handle(Appeared(1, Area));
        core.Handle(new Dispatch("togglefloating", ""));

        core.Handle(new Dispatch("cyclelayout", ""));
        core.Handle(new Dispatch("cyclelayout", ""));
        var dwindle = core.Handle(new Dispatch("cyclelayout", ""));

        Assert.Equal("dwindle", Layout(dwindle));
        Assert.True(Client(dwindle, 1).Floating);
    }

    [Fact]
    public void TogglefloatingDoesNotTileInsideTheFloatingLayout()
    {
        var core = new WindowManager();
        core.Handle(Appeared(1, Area));
        core.Handle(new Dispatch("cyclelayout", "back"));

        var tiled = core.Handle(new Dispatch("togglefloating", ""));

        Assert.Equal("floating", Layout(tiled));
        Assert.True(Client(tiled, 1).Floating);
    }

    [Fact]
    public void OnlyTheFocusedWorkspaceChanges()
    {
        var core = new WindowManager();
        core.Handle(Appeared(1, Area));
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(Appeared(2, Area));

        var cycled = core.Handle(new Dispatch("cyclelayout", ""));

        Assert.Equal("scrolling", LayoutOf(cycled, "2"));
        Assert.Equal("dwindle", LayoutOf(cycled, "1"));
    }

    [Fact]
    public void AnOpenSpecialWorkspaceIsTheOneThatCycles()
    {
        var core = new WindowManager();
        core.Handle(Appeared(1, Area));
        core.Handle(new Dispatch("movetoworkspace", "special:scratch"));

        var cycled = core.Handle(new Dispatch("cyclelayout", ""));

        Assert.Equal("scrolling", LayoutOf(cycled, "special:scratch"));
        Assert.Equal("dwindle", LayoutOf(cycled, "1"));
    }

    [Fact]
    public void AReloadReturnsToTheConfigLayout()
    {
        var config = ConfigParser.Parse("general:layout = scrolling\n");
        var core = new WindowManager(config);
        core.Handle(Appeared(1, Area));
        var cycled = core.Handle(new Dispatch("cyclelayout", ""));
        Assert.Equal("floating", Layout(cycled));

        var reloaded = core.Handle(new Configure(config));

        Assert.Equal("scrolling", Layout(reloaded));
        Assert.False(Client(reloaded, 1).Floating);
    }

    private static string Layout(Outcome outcome) =>
        Assert.Single(outcome.State.Workspaces, workspace => workspace.Id == outcome.State.ActiveWorkspace).TiledLayout;

    private static string LayoutOf(Outcome outcome, string name) =>
        Assert.Single(outcome.State.Workspaces, workspace => workspace.Id == new WorkspaceId(name)).TiledLayout;
}
