namespace Winland.Application.Tests;

public class BindChordTests
{
    [Fact]
    public void LongPressWaitsSixHundredMillisecondsAndIgnoresRepeatDelay()
    {
        var document = ConfigParser.Parse("input {\n    repeat_delay = 250\n}\nbindo = , Q, killactive\n");
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(document));

        var pressed = core.Decide(new KeyPressed("Q"), new BindSession());

        Assert.Equal(new ConfigWarning(2, "Unsupported keyword 'input:repeat_delay'."), Assert.Single(document.Warnings));
        Assert.Equal(600, pressed.ArmLongPressMs);
    }

    [Fact]
    public void LongPressAndNormalBindOnTheSameChordBothRun()
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = , Q, exec, notepad\nbindo = , Q, killactive\n")));
        var session = new BindSession();

        var pressed = core.Decide(new KeyPressed("Q"), session);

        Assert.True(pressed.Consumed);
        Assert.Equal("exec", pressed.Dispatcher);
        Assert.Equal("notepad", pressed.Arguments);
        Assert.Equal(new BindDispatch[] { new("exec", "notepad") }, pressed.Dispatches);
        Assert.Equal(600, pressed.ArmLongPressMs);
        Assert.True(pressed.DisarmLongPress);

        var elapsed = core.Decide(new LongPressElapsed(), session);

        Assert.True(elapsed.Consumed);
        Assert.Equal("killactive", elapsed.Dispatcher);
        Assert.Equal(new BindDispatch[] { new("killactive", "") }, elapsed.Dispatches);
    }

    [Fact]
    public void NonConsumingBindDoesNotConsumeAChordThatAlsoLongPresses()
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bindn = , Q, exec, notepad\nbindo = , Q, killactive\n")));
        var session = new BindSession();

        var pressed = core.Decide(new KeyPressed("Q"), session);
        var released = core.Decide(new KeyReleased("Q"), session);
        var elapsed = core.Decide(new LongPressElapsed(), session);

        Assert.False(pressed.Consumed);
        Assert.Equal(new BindDispatch[] { new("exec", "notepad") }, pressed.Dispatches);
        Assert.Equal(600, pressed.ArmLongPressMs);
        Assert.False(released.Consumed);
        Assert.Null(elapsed.Dispatcher);
        Assert.Empty(elapsed.Dispatches);
    }

    [Fact]
    public void TransparentAndNormalBindsAreBothListedInConfigOrder()
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bindt = , Q, exec, first\nbind = , Q, killactive\n")));
        var session = new BindSession();

        var decision = core.Decide(new KeyPressed("Q"), session);

        Assert.True(decision.Consumed);
        Assert.Equal("exec", decision.Dispatcher);
        Assert.Equal(
            new BindDispatch[]
            {
                new("exec", "first"),
                new("killactive", ""),
            },
            decision.Dispatches);
    }

    [Fact]
    public void ModifierKeyBindLeavesSuperHeldForAMouseBind()
    {
        var core = Load("bind = , Super_L, exec, menu\nbindm = SUPER, mouse:272, movewindow\n");
        Appear(core, 1);

        var super = Press(core, "Super_L");
        var button = core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(500, 400) });

        Assert.Equal("exec", super.Dispatcher);
        Assert.Equal("menu", super.Launch?.CommandLine);
        Assert.True(super.Consumed);
        Assert.Equal("movewindow", button.Dispatcher);
        Assert.True(button.Consumed);
    }

    [Fact]
    public void NonConsumingModifierKeyBindStillCompletesTheNextChord()
    {
        var core = Load("bindn = , Super_L, exec, menu\nbind = SUPER, Q, killactive\nbindm = SUPER, mouse:272, movewindow\n");
        Appear(core, 1);

        var super = Press(core, "Super_L");
        Assert.False(super.Consumed);
        Assert.Equal("menu", super.Launch?.CommandLine);
        Assert.False(Release(core, "Super_L").Consumed);

        Press(core, "Super_L");
        var keyed = Press(core, "Q");
        var button = core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1, 2) });

        Assert.Equal("killactive", keyed.Dispatcher);
        Assert.Equal(new WindowId(1), keyed.Close);
        Assert.Equal("movewindow", button.Dispatcher);
        Assert.False(Release(core, "Super_L").Consumed);
    }

    [Fact]
    public void ModifierKeyLongPressFiresAndLeavesTheModifierHeld()
    {
        var core = Load("bindo = , Super_L, killactive\nbind = SUPER, Q, exec, notepad\n");
        Appear(core, 1);

        var super = Press(core, "Super_L");
        var elapsed = core.Handle(new LongPressElapsed());
        var keyed = Press(core, "Q");

        Assert.False(super.Consumed);
        Assert.Null(super.Dispatcher);
        Assert.Equal("killactive", elapsed.Dispatcher);
        Assert.Equal(new WindowId(1), elapsed.Close);
        Assert.Equal("exec", keyed.Dispatcher);
        Assert.Equal("notepad", keyed.Launch?.CommandLine);
    }

    [Fact]
    public void IgnoreModifiersMatchesAModifierKeyWhileAnotherModifierIsHeld()
    {
        var blocked = Load("bind = , Super_L, killactive");
        Appear(blocked, 1);
        Press(blocked, "Shift_L");
        Assert.Null(Press(blocked, "Super_L").Dispatcher);

        var core = Load("bindi = , Super_L, killactive");
        Appear(core, 1);
        Press(core, "Shift_L");

        var pressed = Press(core, "Super_L");

        Assert.Equal("killactive", pressed.Dispatcher);
        Assert.Equal(new WindowId(1), pressed.Close);
    }

    [Fact]
    public void EmptyDispatcherConsumesBesideANonConsumingBind()
    {
        var core = Load("bindn = , Q, exec, notepad\nbind = , Q,\n");
        Appear(core, 1);

        var pressed = Press(core, "Q");

        Assert.True(pressed.Consumed);
        Assert.Equal("exec", pressed.Dispatcher);
        Assert.Equal("notepad", pressed.Launch?.CommandLine);
        Assert.Null(pressed.Close);
        Assert.True(Release(core, "Q").Consumed);
    }

    [Fact]
    public void PressBindAndClickBindOnTheSameKeyBothRun()
    {
        var core = Load("binds:drag_threshold = 10\nbind = , V, exec, notepad\nbindc = , V, killactive\n");
        Appear(core, 1);
        core.Handle(new PointerMoved(new Point(0, 0)));

        var pressed = Press(core, "V");
        core.Handle(new PointerMoved(new Point(3, 4)));
        var released = Release(core, "V");

        Assert.Equal("exec", pressed.Dispatcher);
        Assert.Equal("notepad", pressed.Launch?.CommandLine);
        Assert.True(pressed.Consumed);
        Assert.Null(pressed.Close);
        Assert.Equal("killactive", released.Dispatcher);
        Assert.Equal(new WindowId(1), released.Close);
    }

    [Fact]
    public void ClickBindOnASharedChordStaysQuietOnceThePointerLeaves()
    {
        var core = Load("binds:drag_threshold = 10\nbind = , V, exec, notepad\nbindc = , V, killactive\n");
        Appear(core, 1);
        core.Handle(new PointerMoved(new Point(0, 0)));
        Press(core, "V");
        core.Handle(new PointerMoved(new Point(40, 0)));

        var released = Release(core, "V");

        Assert.True(released.Consumed);
        Assert.Null(released.Dispatcher);
        Assert.Null(released.Close);
    }

    [Fact]
    public void ClickBindMeasuresFromItsOwnPressNotTheNewestOne()
    {
        var core = Load("binds:drag_threshold = 10\nbindc = SUPER, V, killactive\n");
        Appear(core, 1);
        core.Handle(new PointerMoved(new Point(0, 0)));
        Press(core, "Super_L");
        Press(core, "V");
        core.Handle(new PointerMoved(new Point(40, 0)));
        Press(core, "B");
        Release(core, "B");

        var released = Release(core, "V");

        Assert.Null(released.Dispatcher);
        Assert.Null(released.Close);
    }

    [Fact]
    public void LongPressKeycodeBindFires()
    {
        var core = Load("bindo = SUPER, code:36, killactive\n");
        Appear(core, 1);
        Press(core, "Super_L");
        core.Handle(new KeyPressed("Return") { ScanCode = 36 });

        var elapsed = core.Handle(new LongPressElapsed());

        Assert.Equal("killactive", elapsed.Dispatcher);
        Assert.Equal(new WindowId(1), elapsed.Close);
    }

    [Fact]
    public void RepeatFiresOnlyTheBindMarkedRepeat()
    {
        var core = Load("bind = , Q, exec, notepad\nbinde = , Q, killactive\n");
        Appear(core, 1);

        var first = Press(core, "Q");
        var second = Press(core, "Q");

        Assert.Equal("exec", first.Dispatcher);
        Assert.Equal("notepad", first.Launch?.CommandLine);
        Assert.Equal(new WindowId(1), first.Close);
        Assert.Equal("killactive", second.Dispatcher);
        Assert.Null(second.Launch);
        Assert.Equal(new WindowId(1), second.Close);
    }

    [Fact]
    public void NonConsumingRepeatKeepsPassingTheKey()
    {
        var core = Load("bindne = , Q, exec, notepad");
        Appear(core, 1);

        var first = Press(core, "Q");
        var second = Press(core, "Q");

        Assert.False(first.Consumed);
        Assert.Equal("notepad", first.Launch?.CommandLine);
        Assert.False(second.Consumed);
        Assert.Equal("notepad", second.Launch?.CommandLine);
        Assert.False(Release(core, "Q").Consumed);
    }

    [Fact]
    public void ScrollBindsOnOneNotchAllFireThenWait()
    {
        var core = Load("binds:scroll_event_delay = 300\nbind = , mouse_down, killactive\nbind = , mouse_down, workspace, 2\n");
        Appear(core, 1);
        Appear(core, 2);

        var first = core.Handle(new WheelTurned("mouse_down", 1_000));
        var blocked = core.Handle(new WheelTurned("mouse_down", 1_200));
        var later = core.Handle(new WheelTurned("mouse_down", 1_300));

        Assert.True(first.Consumed);
        Assert.Equal("killactive", first.Dispatcher);
        Assert.Equal(new WindowId(2), first.Close);
        Assert.Equal(new WorkspaceId("2"), first.State.ActiveWorkspace);
        Assert.False(blocked.Consumed);
        Assert.Null(blocked.Dispatcher);
        Assert.Equal("killactive", later.Dispatcher);
        Assert.Equal(new WorkspaceId("2"), later.State.ActiveWorkspace);
    }

    [Fact]
    public void MultiKeyBindHoldsBackASingleKeyBindItStartsWith()
    {
        var core = Load("binds = , A&B, killactive\nbind = , A, exec, notepad\n");
        Appear(core, 1);

        var a = Press(core, "A");
        var b = Press(core, "B");
        Release(core, "B");
        var lifted = Release(core, "A");

        Assert.True(a.Consumed);
        Assert.Null(a.Dispatcher);
        Assert.Equal("killactive", b.Dispatcher);
        Assert.Equal(new WindowId(1), b.Close);
        Assert.Null(lifted.Dispatcher);
    }

    [Fact]
    public void HeldBackSingleKeyBindRunsWhenItsKeyIsReleasedAlone()
    {
        var core = Load("binds = , A&B, killactive\nbind = , A, exec, notepad\n");
        Appear(core, 1);

        var pressed = Press(core, "A");
        var released = Release(core, "A");

        Assert.True(pressed.Consumed);
        Assert.Null(pressed.Launch);
        Assert.True(released.Consumed);
        Assert.Equal("notepad", released.Launch?.CommandLine);
    }

    [Fact]
    public void TransparentMouseBindRunsBesideTheDragBind()
    {
        var core = Load("bindm = , mouse:272, movewindow\nbindmt = , mouse:272, killactive\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));

        var pressed = core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(10, 20) });

        Assert.True(pressed.Consumed);
        Assert.Equal("movewindow", pressed.Dispatcher);
        Assert.Equal(new WindowId(1), pressed.Close);
    }

    [Fact]
    public void EmptyMouseBindConsumesAndTheOtherMouseBindStillRuns()
    {
        var core = Load("bindm = , mouse:272,\nbindm = , mouse:272, movewindow\n");
        Appear(core, 1);

        var pressed = core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(500, 400) });

        Assert.True(pressed.Consumed);
        Assert.Equal("movewindow", pressed.Dispatcher);
    }

    private static WindowManager Load(string text)
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(text)));
        return core;
    }

    private static void Appear(WindowManager core, long window) =>
        core.Handle(new WindowAppeared(new WindowId(window), new Rectangle(0, 0, 1920, 1080)));

    private static Outcome Press(WindowManager core, string key) =>
        core.Handle(new KeyPressed(key));

    private static Outcome Release(WindowManager core, string key) =>
        core.Handle(new KeyReleased(key));
}
