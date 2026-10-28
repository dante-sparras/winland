namespace Winland.Application.Tests;

public class WorkspaceRuleTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);

    [Fact]
    public void HonoredFieldsAreReadWithoutAWarning()
    {
        var document = ConfigParser.Parse("""
            workspace = 3, monitor:DP-1, default:true, persistent:false, gapsin:1 2 3 4, gapsout:5, layout:scrolling
            """);

        Assert.Empty(document.Warnings);
        var rule = Assert.Single(document.WorkspaceRules);
        Assert.Equal("3", rule.WorkspaceString);
        Assert.Equal("DP-1", rule.Monitor);
        Assert.True(rule.Default);
        Assert.False(rule.Persistent);
        Assert.Equal(new EdgeInsets(1, 2, 3, 4), rule.GapsIn);
        Assert.Equal(EdgeInsets.Uniform(5), rule.GapsOut);
        Assert.Equal("scrolling", rule.Layout);
    }

    [Fact]
    public void UnknownFieldsWarnAndTheRestOfTheRuleApplies()
    {
        var document = ConfigParser.Parse("""
            workspace = 1, monitor:DP-1, rounding:0, layout:scrolling, bordersize:2
            """);

        Assert.Equal(
            [
                new ConfigWarning(1, "Unsupported workspace rule field 'rounding'."),
                new ConfigWarning(1, "Unsupported workspace rule field 'bordersize'."),
            ],
            document.Warnings);
        var rule = Assert.Single(document.WorkspaceRules);
        Assert.Equal("DP-1", rule.Monitor);
        Assert.Equal("scrolling", rule.Layout);
    }

    [Theory]
    [InlineData("r[1-2]")]
    [InlineData("w[tv1]")]
    [InlineData("special:scratch")]
    [InlineData("name:code")]
    public void AWorkspaceRuleNamesAWorkspaceByItsNumber(string selector)
    {
        var document = ConfigParser.Parse($"workspace = {selector}, gapsout:10\n");

        Assert.Empty(document.WorkspaceRules);
        Assert.Single(document.Warnings);
    }

    [Fact]
    public void ALayoutThatIsNoLayoutWarnsAndUsesDwindle()
    {
        var document = ConfigParser.Parse("workspace = 2, layout:spiral\n");

        Assert.Equal([new ConfigWarning(1, "Layout 'spiral' is not available; using dwindle.")], document.Warnings);
        Assert.Equal("spiral", Assert.Single(document.WorkspaceRules).Layout);
    }

    [Fact]
    public void AWorkspaceUsesTheLayoutItsRuleNames()
    {
        var core = Configured("general:layout = scrolling\nworkspace = 2, layout:dwindle\nworkspace = 4, layout:scrolling\n");
        core.Handle(new MonitorsChanged([Display("DP-1", "One", 0, 0, 1920, 1080, 1)]));
        core.Handle(Appeared(1, Wide));

        var moved = core.Handle(new Dispatch("movetoworkspace", "2"));
        Assert.Equal("dwindle", Workspace(moved, "2").TiledLayout);

        var named = core.Handle(new Dispatch("workspace", "4"));
        Assert.Equal("scrolling", Workspace(named, "4").TiledLayout);

        var first = core.Handle(new Dispatch("workspace", "1"));
        Assert.Equal("scrolling", Workspace(first, "1").TiledLayout);
    }

    [Fact]
    public void AWorkspaceKeepsItsClientsWhenAReloadChangesItsLayout()
    {
        var core = Configured("workspace = 1, layout:dwindle\n");
        core.Handle(new MonitorsChanged([Display("DP-1", "One", 0, 0, 1920, 1080, 1)]));
        core.Handle(Appeared(1, Wide));
        core.Handle(Appeared(2, Wide));

        var reloaded = core.Handle(new Configure(ConfigParser.Parse("workspace = 1, layout:scrolling\n")));

        Assert.Equal("scrolling", Workspace(reloaded, "1").TiledLayout);
        Assert.Equal(2, reloaded.State.Clients.Count);
    }

    [Fact]
    public void ABrokenFieldDropsTheRule()
    {
        var document = ConfigParser.Parse("""
            workspace = , gapsin:1
            workspace = 1, default:maybe, monitor:DP-1
            workspace = 3, gapsin:1 2 3 4 5
            workspace = 4, gapsin:1 2 3 4
            """);

        Assert.Equal(
            [
                new ConfigWarning(1, "Malformed workspace rule."),
                new ConfigWarning(2, "Failed parsing a workspace rule"),
                new ConfigWarning(3, "Error parsing workspace rule gaps: 1 2 3 4 5"),
            ],
            document.Warnings);
        var rule = Assert.Single(document.WorkspaceRules);
        Assert.Equal("4", rule.WorkspaceString);
        Assert.Equal(new EdgeInsets(1, 2, 3, 4), rule.GapsIn);
    }

    [Fact]
    public void LaterRulesForTheSameWorkspaceOverride()
    {
        var document = ConfigParser.Parse("""
            workspace = 1, gapsin:5, default:false
            workspace = 1, gapsin:6
            workspace = 2, persistent:true
            """);

        Assert.Equal(2, document.WorkspaceRules.Count);
        Assert.False(document.WorkspaceRules[0].Default);
        Assert.Equal(EdgeInsets.Uniform(6), document.WorkspaceRules[0].GapsIn);
        Assert.Equal("2", document.WorkspaceRules[1].WorkspaceString);
    }

    // A rule narrowed to one Monitor does not take away the rule every Monitor has, and both apply on that Monitor.
    [Fact]
    public void ARuleForOneMonitorAddsToTheRuleForEvery()
    {
        var document = ConfigParser.Parse("""
            workspace = 1, gapsin:5, default:false
            workspace = 1, gapsout:8, default:true, monitor:DP-1
            """);
        var dp1 = new Monitor("DP-1", "DP-1", 0, 0, 1920, 1080, 1, true, new Rectangle(0, 0, 1920, 1080));
        var other = new Monitor("DP-2", "DP-2", 1920, 0, 1920, 1080, 1, true, new Rectangle(1920, 0, 1920, 1080));

        Assert.Equal(2, document.WorkspaceRules.Count);
        var narrowed = WorkspaceRules.For(document.WorkspaceRules, new WorkspaceId("1"), dp1);
        Assert.True(narrowed!.Default);
        Assert.Equal(EdgeInsets.Uniform(5), narrowed.GapsIn);
        Assert.Equal(EdgeInsets.Uniform(8), narrowed.GapsOut);
        var general = WorkspaceRules.For(document.WorkspaceRules, new WorkspaceId("1"), other);
        Assert.False(general!.Default);
        Assert.Null(general.GapsOut);
    }

    [Fact]
    public void InnerAndOuterGapsReplaceTheGeneralValuesOnThatWorkspace()
    {
        var core = Configured(FlatConfig("""
            workspace = 2, gapsin:10, gapsout:20
            """));

        core.Handle(Appeared(1, Wide));
        var home = core.Handle(Appeared(2, Wide));
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(home, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(home, 2));

        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(Appeared(3, Wide));
        var there = core.Handle(Appeared(4, Wide));
        Assert.Equal(new Rectangle(20, 20, 930, 1040), BoundsOf(there, 3));
        Assert.Equal(new Rectangle(970, 20, 930, 1040), BoundsOf(there, 4));
    }

    [Fact]
    public void AMonitorFilterDoesNotMoveTheWorkspace()
    {
        var core = Configured(FlatConfig("""
            workspace = 4, monitor:\\.\DISPLAY2, default:true
            """));
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        var connected = core.Handle(new MonitorsChanged([left, right]));

        Assert.Equal(new MonitorId(0), connected.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("1"), connected.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(1), Workspace(connected, "4").Monitor);

        // The number names Workspace 4 on the focused Monitor. It does not jump to DISPLAY2.
        var stayed = core.Handle(new Dispatch("workspace", "4"));
        Assert.Equal(new MonitorId(0), stayed.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("4"), stayed.State.ActiveWorkspace);

        core.Handle(new Dispatch("focusmonitor", right.Name));
        var opened = core.Handle(new WindowAppeared(new WindowId(1), right.WorkArea));
        Assert.Equal(WorkspaceId.FromNumber(4, new MonitorId(1)), Client(opened, 1).Workspace);
        Assert.Equal(right.WorkArea, BoundsOf(opened, 1));
    }

    [Fact]
    public void ADescriptionSelectorFiltersTheWorkspaceRule()
    {
        var core = Configured(FlatConfig("""
            workspace = 4, monitor:desc:Samsung, default:true
            """));
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        var connected = core.Handle(new MonitorsChanged([left, right]));

        Assert.Equal(new WorkspaceId("1"), connected.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), connected.State.ActiveMonitor);
        Assert.Equal(new MonitorId(1), Workspace(connected, "4").Monitor);
    }

    [Fact]
    public void DefaultSelectsTheWorkspaceWhenAMonitorHasNone()
    {
        var core = Configured(FlatConfig("""
            workspace = 4, monitor:\\.\DISPLAY2, default:true
            """));
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        var connected = core.Handle(new MonitorsChanged([left, right]));

        var monitors = Json(core.Execute("j/monitors").Text).EnumerateArray().ToArray();
        Assert.Equal("1", monitors[0].GetProperty("activeWorkspace").GetProperty("name").GetString());
        Assert.Equal("4", monitors[1].GetProperty("activeWorkspace").GetProperty("name").GetString());
        Assert.Equal(new WorkspaceId("1"), connected.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(1), Workspace(connected, "4").Monitor);
    }

    [Fact]
    public void APersistentWorkspaceStaysWhenItIsEmpty()
    {
        var core = Configured(FlatConfig("""
            workspace = 9, persistent:true
            """));
        core.Handle(new MonitorsChanged([Display("DP-1", "One", 0, 0, 1920, 1080, 1)]));

        var listed = Json(core.Execute("j/workspaces").Text)
            .EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetInt32());
        Assert.False(listed[1].GetProperty("ispersistent").GetBoolean());
        Assert.True(listed[9].GetProperty("ispersistent").GetBoolean());
        Assert.Equal(0, listed[9].GetProperty("windows").GetInt32());

        core.Handle(new Dispatch("workspace", "8"));
        var home = core.Handle(new Dispatch("workspace", "1"));

        Assert.Contains(home.State.Workspaces, workspace => workspace.Id == new WorkspaceId("9") && workspace.Persistent);
        Assert.DoesNotContain(home.State.Workspaces, workspace => workspace.Id == new WorkspaceId("8"));
    }


    private static Workspace Workspace(Outcome outcome, string name) =>
        Assert.Single(outcome.State.Workspaces, workspace => workspace.Id.Name == name);

}
