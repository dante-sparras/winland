namespace Winland.Application.Tests;

public class DispatcherRegressionTests
{
    private static readonly Rectangle Area = new(0, 0, 1000, 800);

    [Theory]
    [InlineData("resizeactive", "10 0")]
    [InlineData("killactive", "")]
    public void APreviousLayoutMessageDoesNotOverrideFocusOnAnotherDispatcher(string name, string argument)
    {
        var core = Configured("general:layout = scrolling");
        for (var id = 1; id <= 3; id++)
            core.Handle(new WindowAppeared(new WindowId(id), Area));
        core.Handle(new Dispatch("layoutmsg", "focus l"));
        core.Handle(new Dispatch("focuswindow", "address:0x3"));

        var result = core.Handle(new Dispatch(name, argument));

        Assert.Null(result.Focus);
        Assert.Equal("0x3", Json(core.Execute("j/activewindow").Text).GetProperty("address").GetString());
    }

    [Fact]
    public void MovefocusUsesTheOpenSpecialWorkspace()
    {
        var core = new WindowManager();
        core.Handle(new Dispatch("togglespecialworkspace", "tools"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("movefocus", "l")).Focus);
    }

    [Fact]
    public void FocuswindowRefusesANoFocusClient()
    {
        var core = Configured("windowrule = no_focus, class:quiet");
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area) { Traits = WindowTraits.Normal with { Class = "quiet" } });

        var result = core.Handle(new Dispatch("focuswindow", "address:0x2"));

        Assert.Null(result.Focus);
        Assert.Equal("0x1", Json(core.Execute("j/activewindow").Text).GetProperty("address").GetString());
    }

    [Theory]
    [InlineData("min_size", "400 300")]
    [InlineData("max_size", "600 500")]
    [InlineData("opacity", "0.3")]
    [InlineData("active_border_color", "rgb(ff0000)")]
    public void UnsetRestoresTheDefaultForEveryPropertyKind(string property, string value)
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        var original = core.Execute($"getprop active {property}").Text;
        Assert.Equal("ok", core.Execute($"setprop active {property} {value}").Text);

        Assert.Equal("ok", core.Execute($"setprop active {property} unset").Text);

        Assert.Equal(original, core.Execute($"getprop active {property}").Text);
    }

    [Fact]
    public void SwapAcrossTwoShownWorkspacesKeepsFocusOnTheSourceMonitor()
    {
        var core = new WindowManager();
        core.Handle(new MonitorsChanged([Display("left", 0, 1000, 800), Display("right", 1000, 1000, 800)]));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), new Rectangle(1000, 0, 1000, 800)));
        core.Handle(new Dispatch("focuswindow", "address:0x1"));

        var swapped = core.Handle(new Dispatch("swapwindow", "address:0x2"));

        Assert.Equal(new WindowId(2), swapped.Focus);
        Assert.Equal(new MonitorId(0), swapped.State.ActiveMonitor);
    }

    [Fact]
    public void MoveToWorkspaceKeepsCommasInTheWindowSelector()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Title = "Hello, World" } });

        var moved = core.Handle(new Dispatch("movetoworkspace", "2,title:Hello, World"));

        Assert.Null(moved.Error);
        Assert.Equal("2", Client(moved, 1).Workspace.Name);
    }

    [Fact]
    public void PropertiesCanAddressAHiddenGroupMember()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        Assert.Equal("ok", core.Execute("setprop address:0x1 border_size 9").Text);
        Assert.Equal("9", core.Execute("getprop address:0x1 border_size").Text);
    }

    [Fact]
    public void EveryExecInABatchReportsItsRuleWarnings()
    {
        var core = new WindowManager();

        var result = core.Execute("[[BATCH]]dispatch exec [unknown_one] one; dispatch exec [unknown_two] two");

        Assert.Equal(2, result.Effect.Events.OfType<ConfigWarned>().Count());
    }

    [Fact]
    public void ANativeFloatingMoveJoinsTheMonitorUnderItsMiddle()
    {
        var core = new WindowManager();
        core.Handle(new MonitorsChanged([Display("left", 0, 1000, 800), Display("right", 1000, 1000, 800)]));
        core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Dialog = true } });

        var moved = Place(core, 1, new Rectangle(1200, 100, 300, 200));

        Assert.Equal(1, Client(moved, 1).Workspace.Monitor);
        Assert.Equal(new Rectangle(1200, 100, 300, 200), Client(moved, 1).Bounds);
    }

    [Theory]
    [InlineData(false, "floating", 2)]
    [InlineData(false, "tiled", 3)]
    [InlineData(true, "floating", 2)]
    [InlineData(true, "tiled", 3)]
    public void CyclingWithoutFocusUsesTheCurrentWorkspaceAndItsFilters(bool special, string kind, long expected)
    {
        var core = Configured("windowrule = no_focus, class:quiet");
        if (special)
        {
            core.Handle(new WindowAppeared(new WindowId(99), Area));
            core.Handle(new Dispatch("togglespecialworkspace", "tools"));
        }
        core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Class = "quiet" } });
        core.Handle(new WindowAppeared(new WindowId(2), Area) { Traits = WindowTraits.Normal with { Dialog = true } });
        core.Handle(new WindowAppeared(new WindowId(3), Area));
        core.Handle(new WindowFocused(new WindowId(100), FocusCause.User));

        Assert.Equal(new WindowId(expected), core.Handle(new Dispatch("cyclenext", kind)).Focus);
    }

    [Fact]
    public void LayoutMessagesUseTheLayoutmsgDispatcher()
    {
        var core = new WindowManager();

        Assert.Equal("error: unknown dispatcher 'layout'", core.Handle(new Dispatch("layout", "togglesplit")).Error);
    }
}
