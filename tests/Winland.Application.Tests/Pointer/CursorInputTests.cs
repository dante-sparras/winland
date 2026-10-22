namespace Winland.Application.Tests;

public class CursorInputTests
{
    [Fact]
    public void DocumentedCursorDoesNotWarpOrHide()
    {
        var options = CursorOptions.From(ConfigParser.Parse(""));

        Assert.Equal(0, options.WarpOnWorkspace);
        Assert.Equal(-1, options.WarpOnMonitor);
        Assert.Equal(0, options.WarpOnSpecial);
        Assert.False(options.NoWarps);
        Assert.False(options.PersistentWarps);
        Assert.Equal(0, options.InactiveTimeoutSeconds);
        Assert.False(options.HideOnKeyPress);
        Assert.Equal("", options.DefaultMonitor);
        Assert.Equal(0, options.MonitorMode());
    }

    [Fact]
    public void CursorBlockIsHonored()
    {
        var document = ConfigParser.Parse("""
            cursor {
                warp_on_change_workspace = enable
                warp_on_monitor_change = force
                warp_on_toggle_special = 1
                no_warps = true
                persistent_warps = yes
                inactive_timeout = 4.5
                hide_on_key_press = on
                default_monitor = desc:Dell
            }
            """);

        Assert.Empty(document.Warnings);
        var options = CursorOptions.From(document);
        Assert.Equal(1, options.WarpOnWorkspace);
        Assert.Equal(2, options.WarpOnMonitor);
        Assert.Equal(1, options.WarpOnSpecial);
        Assert.True(options.NoWarps);
        Assert.True(options.PersistentWarps);
        Assert.Equal(4.5, options.InactiveTimeoutSeconds);
        Assert.True(options.HideOnKeyPress);
        Assert.Equal("desc:Dell", options.DefaultMonitor);
    }

    [Fact]
    public void InactiveTimeoutClampsAndABadModeIsIgnored()
    {
        var options = CursorOptions.From(ConfigParser.Parse("""
            cursor:inactive_timeout = 40
            cursor:warp_on_change_workspace = later
            cursor:warp_on_monitor_change = same_as_warp_on_change_workspace
            """));

        Assert.Equal(20, options.InactiveTimeoutSeconds);
        Assert.Equal(0, options.WarpOnWorkspace);
        Assert.Equal(-1, options.WarpOnMonitor);
    }

    [Fact]
    public void UnhonoredCursorKeyStillWarns()
    {
        var document = ConfigParser.Parse("cursor:min_refresh_rate = 30\n");

        Assert.Empty(document.Settings);
        Assert.Equal(new ConfigWarning(1, "Unsupported keyword 'cursor:min_refresh_rate'."), Assert.Single(document.Warnings));
    }

    [Fact]
    public void ASystemInputKeyWarnsAndIsNotApplied()
    {
        var document = ConfigParser.Parse("""
            input:natural_scroll = true
            input:repeat_rate = 25
            input:sensitivity = 0
            input:left_handed = true
            """);

        Assert.Equal(
            [
                new ConfigWarning(2, "Unsupported keyword 'input:repeat_rate'."),
                new ConfigWarning(3, "Unsupported keyword 'input:sensitivity'."),
                new ConfigWarning(4, "Unsupported keyword 'input:left_handed'."),
            ],
            document.Warnings);
        Assert.True(InputOptions.From(document).InvertsScroll);
        Assert.Equal([new Setting("input:natural_scroll", "true")], document.Settings);
    }

    [Fact]
    public void FollowMouseIsReadAndATabletKeyWarns()
    {
        var document = ConfigParser.Parse("input:follow_mouse = detached\ninput:tablet:enabled = true\n");

        Assert.Equal([new Setting("input:follow_mouse", "detached")], document.Settings);
        Assert.Equal(new ConfigWarning(2, "Unsupported keyword 'input:tablet:enabled'."), Assert.Single(document.Warnings));
        Assert.Equal(2, InputOptions.From(document).FollowMouse);
    }

    [Fact]
    public void NaturalScrollReversesTheWheelBeforeTheBind()
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("input:natural_scroll = true\nbind = , mouse_down, workspace, 2\n")));
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600)));

        var outcome = core.Handle(new WheelTurned("mouse_up", 1_000));

        Assert.Equal("workspace", outcome.Dispatcher);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
    }

    [Fact]
    public void WheelDirectionIsUnchangedWhenNaturalScrollIsUnset()
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = , mouse_down, workspace, 2\n")));
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600)));

        var outcome = core.Handle(new WheelTurned("mouse_up", 1_000));

        Assert.NotEqual("workspace", outcome.Dispatcher);
        Assert.Equal(WorkspaceId.Initial, outcome.State.ActiveWorkspace);
    }

    [Fact]
    public void ChangingWorkspaceWarpsToTheMiddleOfTheLastWindow()
    {
        var core = Load("cursor:warp_on_change_workspace = 1");
        var monitor = Display(@"\\.\DISPLAY1", "Main", 0, 0, 800, 600);
        core.Handle(new MonitorsChanged([monitor]));
        core.Handle(new WindowAppeared(new WindowId(1), monitor.WorkArea));

        var away = core.Handle(new Dispatch("workspace", "2"));
        Assert.Null(away.Warp);

        var back = core.Handle(new Dispatch("workspace", "1"));

        Assert.Equal(new WarpTo(new Point(400, 300)), back.Warp);
    }

    [Fact]
    public void NoWarpsBlocksANormalWarpAndNotAForcedOne()
    {
        var blocked = Load("cursor:warp_on_change_workspace = 1\ncursor:no_warps = true");
        var monitor = Display(@"\\.\DISPLAY1", "Main", 0, 0, 800, 600);
        blocked.Handle(new MonitorsChanged([monitor]));
        blocked.Handle(new WindowAppeared(new WindowId(1), monitor.WorkArea));
        blocked.Handle(new Dispatch("workspace", "2"));

        Assert.Null(blocked.Handle(new Dispatch("workspace", "1")).Warp);

        var forced = Load("cursor:warp_on_change_workspace = 2\ncursor:no_warps = true");
        forced.Handle(new MonitorsChanged([monitor]));
        forced.Handle(new WindowAppeared(new WindowId(1), monitor.WorkArea));
        forced.Handle(new Dispatch("workspace", "2"));

        Assert.Equal(new WarpTo(new Point(400, 300)), forced.Handle(new Dispatch("workspace", "1")).Warp);
    }

    [Fact]
    public void PersistentWarpsReturnToThePointLeftInsideTheWindow()
    {
        var core = Load("cursor:warp_on_change_workspace = 1\ncursor:persistent_warps = true");
        var monitor = Display(@"\\.\DISPLAY1", "Main", 0, 0, 800, 600);
        core.Handle(new MonitorsChanged([monitor]));
        core.Handle(new WindowAppeared(new WindowId(1), monitor.WorkArea));
        core.Handle(new PointerMoved(new Point(40, 50)));
        core.Handle(new Dispatch("workspace", "2"));

        var back = core.Handle(new Dispatch("workspace", "1"));

        Assert.Equal(new WarpTo(new Point(40, 50)), back.Warp);
    }

    [Fact]
    public void PersistentWarpsReturnToThePointLeftWhenASpecialWorkspaceCloses()
    {
        var core = Load("cursor:warp_on_toggle_special = 1\ncursor:persistent_warps = true");
        var monitor = Display(@"\\.\DISPLAY1", "Main", 0, 0, 800, 600);
        core.Handle(new MonitorsChanged([monitor]));
        core.Handle(new WindowAppeared(new WindowId(1), monitor.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), monitor.WorkArea));
        core.Handle(new Dispatch("movetoworkspacesilent", "special"));
        var focused = core.Handle(new Dispatch("focuswindow", "address:0x1"));
        var bounds = Assert.Single(focused.State.Clients, client => client.Window == new WindowId(1)).Bounds;
        Assert.Equal(new WindowId(1), focused.Focus);
        core.Handle(new PointerMoved(new Point(bounds.X + 40, bounds.Y + 50)));

        core.Handle(new Dispatch("togglespecialworkspace"));
        var back = core.Handle(new Dispatch("togglespecialworkspace"));

        Assert.Equal(new WarpTo(new Point(bounds.X + 40, bounds.Y + 50)), back.Warp);
    }

    [Fact]
    public void AWindowThatReusesAClosedHandleDoesNotInheritItsWarpPoint()
    {
        var core = new WindowManager(ConfigParser.Parse("cursor:warp_on_change_workspace = 1\ncursor:persistent_warps = true\nwindowrulev2 = workspace 1 silent, class:^(again)$\n"));
        var monitor = Display(@"\\.\DISPLAY1", "Main", 0, 0, 800, 600);
        core.Handle(new MonitorsChanged([monitor]));
        core.Handle(new WindowAppeared(new WindowId(1), monitor.WorkArea));
        core.Handle(new PointerMoved(new Point(40, 50)));
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(new WindowClosed(new WindowId(1)));
        core.Handle(new WindowAppeared(new WindowId(1), monitor.WorkArea) { Traits = WindowTraits.Normal with { Class = "again" } });

        var back = core.Handle(new Dispatch("workspace", "1"));

        Assert.Equal(new WarpTo(new Point(400, 300)), back.Warp);
    }

    [Fact]
    public void MonitorChangeUsesItsOwnMode()
    {
        var core = Load("cursor:warp_on_change_workspace = 0\ncursor:warp_on_monitor_change = 1");
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 800, 600);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var outcome = core.Handle(new Dispatch("focusmonitor", @"\\.\DISPLAY2"));

        Assert.Equal(new MonitorId(1), outcome.State.ActiveMonitor);
        Assert.Equal(new WarpTo(new Point(2560, 512)), outcome.Warp);
    }

    [Fact]
    public void DefaultMonitorWarpsOnceToThatMonitor()
    {
        var core = Load("""
            cursor:default_monitor = \\.\DISPLAY2
            """);
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 800, 600);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024);

        var first = core.Handle(new MonitorsChanged([left, right]));
        var second = core.Handle(new MonitorsChanged([left, right]));

        Assert.Equal(new WarpTo(new Point(2560, 512)), first.Warp);
        Assert.Null(second.Warp);
    }

    [Fact]
    public void SpecialToggleWarpUsesTheSpecialMode()
    {
        var core = Load("cursor:warp_on_toggle_special = 1\ncursor:no_warps = true");
        Assert.Null(core.SpecialWarp(new Rectangle(10, 20, 100, 80), null));

        var forced = Load("cursor:warp_on_toggle_special = force\ncursor:persistent_warps = true");
        Assert.Equal(new WarpTo(new Point(40, 60)), forced.SpecialWarp(new Rectangle(10, 20, 100, 80), new Point(30, 40)));
        Assert.Equal(new WarpTo(new Point(60, 60)), forced.SpecialWarp(new Rectangle(10, 20, 100, 80), new Point(0, 10)));
    }

    [Fact]
    public void ConfigurePublishesTheCursorAndSkipsASystemInputKey()
    {
        var document = ConfigParser.Parse("input:repeat_rate = 30\ninput:scroll_factor = 2\n");
        var outcome = new WindowManager().Handle(new Configure(document));

        Assert.Equal(
            [
                new ConfigWarning(1, "Unsupported keyword 'input:repeat_rate'."),
                new ConfigWarning(2, "Unsupported keyword 'input:scroll_factor'."),
            ],
            document.Warnings);
        Assert.Equal(new CursorBehavior(false, 0), outcome.Cursor);
    }

    [Fact]
    public void ClientsRoundTripAndAnOldInputObjectIsIgnored()
    {
        var snapshot = new RecoverySnapshot(
        [
            new RecoveredClient(new WindowId(7), WorkspaceId.Initial, MonitorId.Primary, false, "CabinetWClass", "Home", 9)
            {
                MonitorIdentity = @"\\.\DISPLAY1",
            },
        ]);

        Assert.True(RecoverySnapshot.TryParse(snapshot.ToJson(), out var parsed));
        Assert.Equal(snapshot.Clients, parsed.Clients);
        Assert.DoesNotContain("input", snapshot.ToJson(), StringComparison.Ordinal);

        const string Older = """
            {"clients":[{"window":7,"workspace":"1","monitor":"\\\\.\\DISPLAY1","hidden":false,"class":"CabinetWClass","title":"Home","process":9}],"input":{"keyboardDelay":1},"mirrors":[],"vrr":{},"colors":[]}
            """;
        Assert.True(RecoverySnapshot.TryParse(Older, out var legacy));
        Assert.Equal(snapshot.Clients, legacy.Clients);
    }

    private static WindowManager Load(string text)
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(text)));
        return core;
    }

    private static Monitor Display(string name, string description, int x, int y, int width, int height) =>
        new(name, description, x, y, width, height, 1, true, new Rectangle(x, y, width, height));
}
