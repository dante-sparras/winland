using Winland;

namespace Winland.Tests;

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
        var document = ConfigParser.Parse("cursor:zoom_disable_aa = true\n");

        Assert.Empty(document.Settings);
        Assert.Equal(new ConfigWarning(1, "Unsupported keyword 'cursor:zoom_disable_aa'."), Assert.Single(document.Warnings));
    }

    [Fact]
    public void UnsetInputDoesNotRequestASystemChange()
    {
        var request = InputOptions.From(ConfigParser.Parse("")).Request();

        Assert.Null(request);
    }

    [Fact]
    public void InputSettingsMapOntoSystemRanges()
    {
        var document = ConfigParser.Parse("""
            input {
                repeat_rate = 25
                repeat_delay = 600
                numlock_by_default = true
                left_handed = true
                natural_scroll = true
                accel_profile = flat
                sensitivity = 0
                scroll_factor = 1
            }
            """);

        Assert.Empty(document.Warnings);
        var options = InputOptions.From(document);
        Assert.True(options.InvertsScroll);
        var request = options.Request();
        Assert.NotNull(request);
        Assert.Equal(25, request.KeyboardSpeed);
        Assert.Equal(1, request.KeyboardDelay);
        Assert.True(request.NumLock);
        Assert.True(request.SwapButtons);
        Assert.Equal(PointerAcceleration.Flat, request.Acceleration);
        Assert.Equal(10, request.MouseSpeed);
        Assert.Equal(3, request.WheelLines);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(250, 0)]
    [InlineData(600, 1)]
    [InlineData(625, 2)]
    [InlineData(1000, 3)]
    [InlineData(2000, 3)]
    public void KeyboardDelayPicksTheNearestStep(int milliseconds, int delay) =>
        Assert.Equal(delay, InputMapping.KeyboardDelay(milliseconds));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(25, 25)]
    [InlineData(30, 31)]
    [InlineData(200, 31)]
    public void KeyboardSpeedFollowsTheSystemScale(int repeatsPerSecond, int speed) =>
        Assert.Equal(speed, InputMapping.KeyboardSpeed(repeatsPerSecond));

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(-0.5, 5)]
    [InlineData(0, 10)]
    [InlineData(0.5, 15)]
    [InlineData(1, 20)]
    public void SensitivityMapsOntoPointerSpeed(double sensitivity, int speed) =>
        Assert.Equal(speed, InputMapping.MouseSpeed(sensitivity));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, 2)]
    [InlineData(1, 3)]
    [InlineData(2, 6)]
    public void ScrollFactorMapsOntoWheelLines(double factor, int lines) =>
        Assert.Equal(lines, InputMapping.WheelLines(factor));

    [Fact]
    public void AdaptiveAccelerationAndPartialSettingsStayPartial()
    {
        var request = InputOptions.From(ConfigParser.Parse("input:accel_profile = adaptive\ninput:sensitivity = 1\n")).Request();

        Assert.NotNull(request);
        Assert.Equal(PointerAcceleration.Adaptive, request.Acceleration);
        Assert.Equal(20, request.MouseSpeed);
        Assert.Null(request.KeyboardDelay);
        Assert.Null(request.KeyboardSpeed);
        Assert.Null(request.NumLock);
        Assert.Null(request.SwapButtons);
        Assert.Null(request.WheelLines);
    }

    [Fact]
    public void CustomAccelerationWarnsAndDoesNotRequestACurve()
    {
        var document = ConfigParser.Parse("input:accel_profile = custom\n");

        Assert.Equal(new ConfigWarning(1, "Custom acceleration profiles are not honored."), Assert.Single(document.Warnings));
        Assert.Null(InputOptions.From(document).Request());
    }

    [Fact]
    public void FollowMouseIsReadAndTabletsStayAGap()
    {
        var document = ConfigParser.Parse("input:follow_mouse = detached\ninput:tablet:enabled = true\n");

        Assert.Equal(new Setting("input:follow_mouse", "detached"), Assert.Single(document.Settings));
        Assert.Equal(new ConfigWarning(2, "Unsupported keyword 'input:tablet:enabled'."), Assert.Single(document.Warnings));
        Assert.Equal(2, InputOptions.From(document).FollowMouse);
    }

    [Fact]
    public void NaturalScrollReversesTheWheelBeforeTheBind()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("input:natural_scroll = true\nbind = , mouse_down, workspace, 2\n")));
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600)));

        var outcome = core.Handle(new WheelTurned("mouse_up", 1_000));

        Assert.Equal("workspace", outcome.Dispatcher);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
    }

    [Fact]
    public void WheelDirectionIsUnchangedWhenNaturalScrollIsUnset()
    {
        var core = new Core();
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
        var core = new Core(ConfigParser.Parse("cursor:warp_on_change_workspace = 1\ncursor:persistent_warps = true\nwindowrulev2 = workspace 1 silent, class:^(again)$\n"));
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

        var outcome = core.Handle(new Dispatch("workspace", "2"));

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
    public void ConfigurePublishesTheInputRequest()
    {
        var core = new Core();
        var outcome = core.Handle(new Configure(ConfigParser.Parse("input:repeat_rate = 30\ninput:scroll_factor = 2\n")));

        Assert.NotNull(outcome.InputSettings);
        Assert.Equal(31, outcome.InputSettings.KeyboardSpeed);
        Assert.Equal(6, outcome.InputSettings.WheelLines);
        Assert.Null(outcome.InputSettings.KeyboardDelay);
        Assert.Equal(new CursorBehavior(false, 0) { Zoom = CursorZoom.None with { Animation = new Timing(Bezier.Default, 800) } }, outcome.Cursor);
    }

    [Fact]
    public void SavedInputRoundTripsBesideTheClients()
    {
        var snapshot = new RecoverySnapshot(
            [new RecoveredClient(new WindowId(7), WorkspaceId.Initial, MonitorId.Primary, false, "CabinetWClass", "Home", 9)])
        {
            Input = new InputOriginals
            {
                KeyboardDelay = 1,
                KeyboardSpeed = 10,
                NumLock = false,
                SwappedButtons = true,
                MouseCurve = new MouseCurve(6, 10, 1),
                MouseSpeed = 10,
                WheelLines = 3,
            },
        };

        Assert.True(RecoverySnapshot.TryParse(snapshot.ToJson(), out var parsed));
        Assert.Equal(snapshot.Clients, parsed.Clients);
        Assert.Equal(snapshot.Input, parsed.Input);
    }

    private static Core Load(string text)
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(text)));
        return core;
    }

    private static Monitor Display(string name, string description, int x, int y, int width, int height) =>
        new(name, description, x, y, width, height, 1, true, new Rectangle(x, y, width, height));
}
