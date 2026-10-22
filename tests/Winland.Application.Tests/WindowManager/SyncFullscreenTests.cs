namespace Winland.Application.Tests;

/// <summary>
/// <c>sync_fullscreen</c> decides whether a fullscreen request copies the internal mode onto the client mode.
/// The client mode is stored and is not sent to the window.
/// </summary>
public class SyncFullscreenTests
{
    private static readonly Rectangle Work = new(0, 40, 1920, 1040);

    [Fact]
    public void TheDefaultCopiesTheClientMode()
    {
        var core = Desk();
        Appear(core);
        Assert.Equal("true", Prop(core));

        var fullscreen = core.Handle(new Dispatch("fullscreen"));
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(fullscreen).Fullscreen);
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(fullscreen).FullscreenClient);

        var maximized = core.Handle(new Dispatch("maximize"));
        Assert.Equal(FullscreenMode.Maximized, ClientOf(maximized).Fullscreen);
        Assert.Equal(FullscreenMode.Maximized, ClientOf(maximized).FullscreenClient);
    }

    [Fact]
    public void AFalseRuleLeavesTheClientModeAndUnsetRestoresIt()
    {
        var core = Desk("windowrulev2 = sync_fullscreen 0, class:^(app)$\n");
        Appear(core, "app");
        Assert.Equal("false", Prop(core));

        var entered = core.Handle(new Dispatch("fullscreen"));
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(entered).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(entered).FullscreenClient);

        var left = core.Handle(new Dispatch("fullscreen"));
        Assert.Equal(FullscreenMode.None, ClientOf(left).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(left).FullscreenClient);

        var maximized = core.Handle(new Dispatch("maximize"));
        Assert.Equal(FullscreenMode.Maximized, ClientOf(maximized).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(maximized).FullscreenClient);

        core.Execute("dispatch setprop address:0x1 sync_fullscreen 1");
        Assert.Equal("true", Prop(core));
        var copied = core.Handle(new Dispatch("fullscreen"));
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(copied).Fullscreen);
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(copied).FullscreenClient);

        core.Execute("dispatch setprop address:0x1 sync_fullscreen unset");
        Assert.Equal("false", Prop(core));
        var again = core.Handle(new Dispatch("fullscreen"));
        Assert.Equal(FullscreenMode.None, ClientOf(again).Fullscreen);
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(again).FullscreenClient);
    }

    [Fact]
    public void AMaximizeRequestFollowsTheProperty()
    {
        var core = Desk("windowrulev2 = sync_fullscreen 0, class:^(app)$\n");
        Appear(core, "app");

        var maximized = core.Handle(new WindowMaximizeRequested(new WindowId(1)));

        Assert.Equal(FullscreenMode.Maximized, ClientOf(maximized).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(maximized).FullscreenClient);
    }

    [Fact]
    public void FullscreenStateSetsThePropertyFromTheModesItLeft()
    {
        var core = Desk("windowrulev2 = sync_fullscreen 0, class:^(app)$\n");
        Appear(core, "app");

        var split = core.Handle(new Dispatch("fullscreenstate", "2 0"));
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(split).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(split).FullscreenClient);
        Assert.Equal("false", Prop(core));

        var internalOnly = core.Handle(new Dispatch("fullscreen"));
        Assert.Equal(FullscreenMode.None, ClientOf(internalOnly).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(internalOnly).FullscreenClient);

        var back = core.Handle(new Dispatch("fullscreen"));
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(back).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(back).FullscreenClient);

        var equal = core.Handle(new Dispatch("fullscreenstate", "1 1"));
        Assert.Equal(FullscreenMode.Maximized, ClientOf(equal).Fullscreen);
        Assert.Equal(FullscreenMode.Maximized, ClientOf(equal).FullscreenClient);
        Assert.Equal("true", Prop(core));

        var copied = core.Handle(new Dispatch("fullscreen"));
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(copied).Fullscreen);
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(copied).FullscreenClient);
    }

    [Fact]
    public void FullscreenStateOnTheCurrentModesStillSetsTheProperty()
    {
        var core = Desk("windowrulev2 = sync_fullscreen 0, class:^(app)$\n");
        Appear(core, "app");

        var same = core.Handle(new Dispatch("fullscreenstate", "0 0 set"));

        Assert.Equal(FullscreenMode.None, ClientOf(same).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(same).FullscreenClient);
        Assert.Equal("true", Prop(core));
    }

    [Fact]
    public void AFullscreenRuleWithThePropertyFalseLeavesTheClientModeNone()
    {
        var core = Desk("""
            windowrulev2 = fullscreen, class:^(app)$
            windowrulev2 = sync_fullscreen 0, class:^(app)$
            """);
        var opened = Appear(core, "app");

        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(opened).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(opened).FullscreenClient);
        Assert.Equal("false", Prop(core));
    }

    [Fact]
    public void AFullscreenStateRuleTurnsThePropertyOffEvenWhenTheModesMatch()
    {
        var core = Desk("""
            windowrulev2 = sync_fullscreen 1, class:^(app)$
            windowrulev2 = fullscreen_state 2 2, class:^(app)$
            """);
        var opened = Appear(core, "app");

        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(opened).Fullscreen);
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(opened).FullscreenClient);
        Assert.Equal("false", Prop(core));

        var left = core.Handle(new Dispatch("fullscreen"));
        Assert.Equal(FullscreenMode.None, ClientOf(left).Fullscreen);
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(left).FullscreenClient);

        var returned = core.Handle(new Dispatch("fullscreen"));
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(returned).Fullscreen);
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(returned).FullscreenClient);
    }

    [Fact]
    public void AFullscreenStateRuleKeepsASplitAndALaterFullscreenLeavesTheClientMode()
    {
        var core = Desk("windowrulev2 = fullscreen_state 2 0, class:^(app)$\n");
        var opened = Appear(core, "app");

        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(opened).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(opened).FullscreenClient);
        Assert.Equal("false", Prop(core));

        var left = core.Handle(new Dispatch("fullscreen"));
        Assert.Equal(FullscreenMode.None, ClientOf(left).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(left).FullscreenClient);

        var returned = core.Handle(new Dispatch("fullscreen"));
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(returned).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(returned).FullscreenClient);
    }

    [Fact]
    public void APinnedClientWithThePropertyFalseIsNotLeftFullscreen()
    {
        var core = Desk("""
            windowrulev2 = float, class:^(stick)$
            windowrulev2 = pin, class:^(stick)$
            windowrulev2 = fullscreen_state 2 0, class:^(stick)$
            """);
        var opened = Appear(core, "stick");

        Assert.True(ClientOf(opened).Pinned);
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(opened).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(opened).FullscreenClient);
        Assert.Equal("false", Prop(core));

        var left = core.Handle(new Dispatch("fullscreen"));

        Assert.True(ClientOf(left).Pinned);
        Assert.Equal(FullscreenMode.None, ClientOf(left).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(left).FullscreenClient);
    }

    [Fact]
    public void ARecoveredClientKeepsItsClientMode()
    {
        var core = Desk("""
            windowrulev2 = fullscreen, class:^(app)$
            windowrulev2 = sync_fullscreen 0, class:^(app)$
            """);
        core.Restore(new RecoverySnapshot(
        [
            new RecoveredClient(
                new WindowId(1),
                WorkspaceId.Initial,
                MonitorId.Primary,
                false,
                "app",
                "App",
                10,
                Fullscreen: FullscreenMode.Fullscreen,
                FullscreenClient: FullscreenMode.Fullscreen),
        ]));

        var opened = Appear(core, "app");

        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(opened).Fullscreen);
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(opened).FullscreenClient);
        Assert.Equal("false", Prop(core));

        var left = core.Handle(new Dispatch("fullscreen"));
        Assert.Equal(FullscreenMode.None, ClientOf(left).Fullscreen);
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(left).FullscreenClient);
    }

    [Fact]
    public void ARecoveredFullscreenStateRuleStillTurnsThePropertyOff()
    {
        var core = Desk("windowrulev2 = fullscreen_state 2 0, class:^(app)$\n");
        core.Restore(new RecoverySnapshot(
        [
            new RecoveredClient(
                new WindowId(1),
                WorkspaceId.Initial,
                MonitorId.Primary,
                false,
                "app",
                "App",
                10,
                Fullscreen: FullscreenMode.Fullscreen,
                FullscreenClient: FullscreenMode.Fullscreen),
        ]));

        var opened = Appear(core, "app");

        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(opened).Fullscreen);
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(opened).FullscreenClient);
        Assert.Equal("false", Prop(core));

        var left = core.Handle(new Dispatch("fullscreen"));
        Assert.Equal(FullscreenMode.None, ClientOf(left).Fullscreen);
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(left).FullscreenClient);
    }

    private static WindowManager Desk(string? config = null)
    {
        var core = config is null ? new WindowManager() : new WindowManager(ConfigParser.Parse(config));
        core.Handle(new MonitorsChanged([new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, Work)]));
        return core;
    }

    private static Outcome Appear(WindowManager core, string? className = null) =>
        core.Handle(new WindowAppeared(new WindowId(1), Work)
        {
            Traits = className is null ? WindowTraits.Normal : WindowTraits.Normal with { Class = className },
        });

    private static string Prop(WindowManager core) => core.Execute("getprop address:0x1 sync_fullscreen").Text;

    private static Client ClientOf(Outcome outcome) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(1));
}
