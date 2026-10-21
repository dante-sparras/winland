using Winland;

namespace Winland.Tests;

public class FocusReasonTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void FocusReasonBitsMatchThePinnedSet()
    {
        Assert.Equal(0u, (uint)FocusReason.Unknown);
        Assert.Equal(1u, (uint)FocusReason.FollowMouse);
        Assert.Equal(2u, (uint)FocusReason.Keybind);
        Assert.Equal(4u, (uint)FocusReason.FocusWindow);
        Assert.Equal(8u, (uint)FocusReason.MoveIntoGroup);
        Assert.Equal(16u, (uint)FocusReason.Click);
        Assert.Equal(48u, (uint)FocusReason.ClickDown);
        Assert.Equal(80u, (uint)FocusReason.ClickUp);
        Assert.Equal(128u, (uint)FocusReason.DesktopState);
        Assert.Equal(256u, (uint)FocusReason.SwitchSoft);
        Assert.Equal(512u, (uint)FocusReason.SwitchHard);
        Assert.Equal(1024u, (uint)FocusReason.GroupCurrent);
        Assert.Equal(2048u, (uint)FocusReason.Workspace);
        Assert.Equal(4096u, (uint)FocusReason.ToggleSpecial);
        Assert.Equal(8192u, (uint)FocusReason.UnmapTiled);
        Assert.Equal(16384u, (uint)FocusReason.UnmapFloating);
        Assert.Equal(32768u, (uint)FocusReason.UnmapGrouped);
        Assert.Equal(65536u, (uint)FocusReason.NewWindow);
        Assert.Equal(131072u, (uint)FocusReason.Ghosts);
        Assert.Equal(262144u, (uint)FocusReason.Other);
    }

    [Fact]
    public void OpeningAWindowReportsNewWindow()
    {
        var core = new Core();

        var first = core.Handle(new WindowAppeared(new WindowId(1), Area));
        var second = core.Handle(new WindowAppeared(new WindowId(2), Area));

        Assert.Equal(new WindowId(1), Focus(first).Window);
        Assert.Equal(FocusReason.NewWindow, Focus(first).Reason);
        Assert.Equal(new WindowId(2), Focus(second).Window);
        Assert.Equal(FocusReason.NewWindow, Focus(second).Reason);
    }

    [Fact]
    public void FocusWindowReportsItsDispatcher()
    {
        var core = Open(new Core());

        var focused = core.Handle(new Dispatch("focuswindow", "address:0x1"));

        Assert.Equal(new WindowId(1), focused.Focus);
        Assert.Equal(FocusReason.FocusWindow, Focus(focused).Reason);
    }

    [Fact]
    public void MoveFocusOnOneWorkspaceIsASoftSwitch()
    {
        var core = Open(Flat());

        var moved = core.Handle(new Dispatch("movefocus", "l"));

        Assert.Equal(new WindowId(1), moved.Focus);
        Assert.Equal(FocusReason.SwitchSoft, Focus(moved).Reason);
    }

    [Fact]
    public void MoveFocusFromFullscreenIsAHardSwitch()
    {
        var core = Open(Flat("binds:movefocus_cycles_fullscreen = true\n"));
        core.Handle(new Dispatch("fullscreen", "0"));

        var moved = core.Handle(new Dispatch("movefocus", "l"));

        Assert.Equal(new WindowId(1), moved.Focus);
        Assert.Equal(FocusReason.SwitchHard, Focus(moved).Reason);
    }

    [Fact]
    public void MoveFocusFromAMaximizedWindowIsASoftSwitch()
    {
        var core = Open(Flat("binds:movefocus_cycles_fullscreen = true\n"));
        core.Handle(new Dispatch("fullscreen", "1"));

        var moved = core.Handle(new Dispatch("movefocus", "l"));

        Assert.Equal(new WindowId(1), moved.Focus);
        Assert.Equal(FocusReason.SwitchSoft, Focus(moved).Reason);
    }

    [Fact]
    public void MoveFocusInsideAGroupStaysOnTheGroup()
    {
        var core = Grouped("binds:movefocus_cycles_groupfirst = true\n");

        var moved = core.Handle(new Dispatch("movefocus", "l"));

        Assert.Equal(new WindowId(1), moved.Focus);
        Assert.Equal(FocusReason.GroupCurrent, Focus(moved).Reason);
    }

    [Fact]
    public void MoveFocusOntoAnotherMonitorIsAKeybind()
    {
        var core = TwoMonitors();

        var moved = core.Handle(new Dispatch("movefocus", "r"));

        Assert.Equal(new WindowId(2), moved.Focus);
        Assert.Equal(FocusReason.Keybind, Focus(moved).Reason);
    }

    [Fact]
    public void CycleNextOnDwindleIsASoftSwitch()
    {
        var core = Open(Flat());

        var cycled = core.Handle(new Dispatch("cyclenext"));

        Assert.Equal(new WindowId(1), cycled.Focus);
        Assert.Equal(FocusReason.SwitchSoft, Focus(cycled).Reason);
    }

    [Fact]
    public void CycleNextOnMasterIsAKeybind()
    {
        var core = Open(Flat("general:layout = master\n"));

        var cycled = core.Handle(new Dispatch("cyclenext", "tiled"));

        Assert.Equal(new WindowId(1), cycled.Focus);
        Assert.Equal(FocusReason.Keybind, Focus(cycled).Reason);
    }

    [Fact]
    public void CycleNextOnMonocleIsADesktopStateChange()
    {
        var core = Open(Flat("general:layout = monocle\n"));

        var cycled = core.Handle(new Dispatch("cyclenext", "tiled"));

        Assert.Equal(new WindowId(1), cycled.Focus);
        Assert.Equal(FocusReason.DesktopState, Focus(cycled).Reason);
    }

    [Fact]
    public void FocusCurrentOrLastIsASoftSwitch()
    {
        var core = Open(new Core());

        var focused = core.Handle(new Dispatch("focuscurrentorlast"));

        Assert.Equal(new WindowId(1), focused.Focus);
        Assert.Equal(FocusReason.SwitchSoft, Focus(focused).Reason);
    }

    [Fact]
    public void FocusCurrentOrLastOnAnotherWorkspaceReportsWorkspace()
    {
        var core = Open(new Core());
        core.Handle(new Dispatch("movetoworkspacesilent", "2,address:0x1"));

        var focused = core.Handle(new Dispatch("focuscurrentorlast"));

        Assert.Equal(new WindowId(1), focused.Focus);
        Assert.Equal(new WorkspaceId("2"), focused.State.ActiveWorkspace);
        Assert.Equal(FocusReason.Workspace, Focus(focused).Reason);
    }

    [Fact]
    public void FocusUrgentOrLastIsASoftSwitch()
    {
        var core = Open(new Core());
        core.Handle(new WindowFocused(new WindowId(1), FocusCause.Activated));

        var focused = core.Handle(new Dispatch("focusurgentorlast"));

        Assert.Equal(new WindowId(1), focused.Focus);
        Assert.Equal(FocusReason.SwitchSoft, Focus(focused).Reason);
    }

    [Fact]
    public void WorkspaceOnTheSameMonitorReportsWorkspace()
    {
        var core = Open(new Core());

        var away = core.Handle(new Dispatch("workspace", "2"));
        var back = core.Handle(new Dispatch("workspace", "1"));

        Assert.Null(Focus(away).Window);
        Assert.Equal(FocusReason.Workspace, Focus(away).Reason);
        Assert.Equal(new WindowId(2), Focus(back).Window);
        Assert.Equal(FocusReason.Workspace, Focus(back).Reason);
    }

    [Fact]
    public void AWorkspaceNumberStaysOnTheFocusedMonitor()
    {
        var core = TwoMonitors();

        var moved = core.Handle(new Dispatch("workspace", "2"));

        Assert.Equal(new MonitorId(0), moved.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("2"), moved.State.ActiveWorkspace);
        Assert.Null(Focus(moved).Window);
        Assert.Equal(FocusReason.Workspace, Focus(moved).Reason);
    }

    [Fact]
    public void FocusMonitorIsAKeybind()
    {
        var core = TwoMonitors();

        var moved = core.Handle(new Dispatch("focusmonitor", "r"));

        Assert.Equal(new WindowId(2), moved.Focus);
        Assert.Equal(FocusReason.Keybind, Focus(moved).Reason);
    }

    [Fact]
    public void ToggleSpecialWorkspaceReportsItsReason()
    {
        var core = Open(new Core());
        core.Handle(new Dispatch("movetoworkspace", "special:scratch"));
        core.Handle(new Dispatch("togglespecialworkspace", "scratch"));

        var opened = core.Handle(new Dispatch("togglespecialworkspace", "scratch"));

        Assert.Equal(new WindowId(2), opened.Focus);
        Assert.Equal(FocusReason.ToggleSpecial, Focus(opened).Reason);
    }

    [Fact]
    public void ChangeGroupActiveReportsTheGroup()
    {
        var core = Grouped();

        var changed = core.Handle(new Dispatch("changegroupactive"));

        Assert.Equal(new WindowId(1), changed.Focus);
        Assert.Equal(FocusReason.GroupCurrent, Focus(changed).Reason);
    }

    [Fact]
    public void MoveIntoOrCreateGroupReportsTheMove()
    {
        var core = Open(Flat("group:auto_group = false\n"));

        var moved = core.Handle(new Dispatch("moveintoorcreategroup", "r,address:0x1"));

        Assert.Equal(new WindowId(1), moved.Focus);
        Assert.Equal(FocusReason.MoveIntoGroup, Focus(moved).Reason);
    }

    [Fact]
    public void ClosingATiledWindowReportsUnmapTiled()
    {
        var core = Open(new Core());

        var closed = core.Handle(new WindowClosed(new WindowId(2)));

        Assert.Equal(new WindowId(1), closed.Focus);
        Assert.Equal(FocusReason.UnmapTiled, Focus(closed).Reason);
    }

    [Fact]
    public void ClosingAFloatingWindowReportsUnmapFloating()
    {
        var core = Open(new Core());
        core.Handle(new Dispatch("setfloating"));

        var closed = core.Handle(new WindowClosed(new WindowId(2)));

        Assert.Equal(new WindowId(1), closed.Focus);
        Assert.Equal(FocusReason.UnmapFloating, Focus(closed).Reason);
    }

    [Fact]
    public void ClosingAGroupedWindowReportsUnmapGrouped()
    {
        var core = Grouped();

        var closed = core.Handle(new WindowClosed(new WindowId(2)));

        Assert.Equal(new WindowId(1), closed.Focus);
        Assert.Equal(FocusReason.UnmapGrouped, Focus(closed).Reason);
    }

    [Fact]
    public void ClosingTheLastWindowReportsOther()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var closed = core.Handle(new WindowClosed(new WindowId(1)));

        Assert.Null(Focus(closed).Window);
        Assert.Equal(FocusReason.Other, Focus(closed).Reason);
    }

    [Fact]
    public void MovingThePointerOntoAWindowFollowsIt()
    {
        var core = Flat();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        var laid = core.Handle(new WindowAppeared(new WindowId(2), Area));

        var seeded = Move(core, Center(laid, 2), 1_000);
        var followed = Move(core, Center(laid, 1), 1_100);

        Assert.Null(seeded.Focus);
        Assert.Equal(new WindowId(1), followed.Focus);
        Assert.Equal(FocusReason.FollowMouse, Focus(followed).Reason);
    }

    [Fact]
    public void AClickOnTheWindowReportsClick()
    {
        var core = Open(new Core());

        var clicked = core.Handle(new WindowFocused(new WindowId(1), FocusCause.User) { Clicked = new WindowId(1) });

        Assert.Equal(new WindowId(1), Focus(clicked).Window);
        Assert.Equal(FocusReason.Click, Focus(clicked).Reason);
    }

    [Fact]
    public void AUserFocusWithNoClickIsUnknown()
    {
        var core = Open(new Core());

        var focused = core.Handle(new WindowFocused(new WindowId(1), FocusCause.User));

        Assert.Equal(new WindowId(1), Focus(focused).Window);
        Assert.Equal(FocusReason.Unknown, Focus(focused).Reason);
    }

    [Fact]
    public void AnAcceptedActivationIsADesktopStateChange()
    {
        var core = Open(new Core(ConfigParser.Parse("misc:focus_on_activate = true\n")));

        var activated = core.Handle(new WindowFocused(new WindowId(1), FocusCause.User) { Clicked = new WindowId(2) });

        Assert.Equal(new WindowId(1), Focus(activated).Window);
        Assert.Equal(FocusReason.DesktopState, Focus(activated).Reason);
    }

    [Fact]
    public void ASilentMoveOfTheFocusedWindowIsAKeybind()
    {
        var core = Open(new Core());

        var moved = core.Handle(new Dispatch("movetoworkspacesilent", "3"));

        Assert.Equal(new WindowId(1), Focus(moved).Window);
        Assert.Equal(FocusReason.Keybind, Focus(moved).Reason);
    }

    private static FocusChanged Focus(Outcome outcome) =>
        Assert.Single(outcome.Events.OfType<FocusChanged>());

    private static Core Flat(string extra = "") =>
        new(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + extra));

    private static Core Open(Core core)
    {
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        return core;
    }

    private static Core Grouped(string extra = "")
    {
        var core = Flat("group:auto_group = true\n" + extra);
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        return core;
    }

    private static Core TwoMonitors()
    {
        var core = new Core();
        var left = new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, new Rectangle(0, 0, 1920, 1080));
        var right = new Monitor(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1920, 1080, 1, true, new Rectangle(1920, 0, 1920, 1080));
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        return core;
    }

    private static Outcome Move(Core core, Point point, long time) =>
        core.Handle(new PointerMoved(point) { TimeMs = time });

    private static Point Center(Outcome outcome, long window)
    {
        var bounds = Assert.Single(outcome.State.Clients, client => client.Window == new WindowId(window)).Bounds;
        return new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }
}
