namespace Winland.Application.Tests;

public class BindChordTests
{
    [Fact]
    public void ModifierKeyBindLeavesSuperHeldForAMouseBind()
    {
        var core = Load("bind = , Super_L, exec, menu\nbindm = SUPER, mouse:272, movewindow\n");
        Appear(core, 1);

        var super = Press(core, "Super_L");
        var button = core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(500, 400) });

        Assert.Equal("menu", super.Launch?.CommandLine);
        Assert.True(super.Consumed);
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

        Assert.Equal(new WindowId(1), keyed.Close);
        Assert.False(Release(core, "Super_L").Consumed);
    }

    [Fact]
    public void EmptyDispatcherConsumesBesideANonConsumingBind()
    {
        var core = Load("bindn = , Q, exec, notepad\nbind = , Q,\n");
        Appear(core, 1);

        var pressed = Press(core, "Q");

        Assert.True(pressed.Consumed);
        Assert.Equal("notepad", pressed.Launch?.CommandLine);
        Assert.Null(pressed.Close);
        Assert.True(Release(core, "Q").Consumed);
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

        Assert.Null(released.Close);
    }

    [Fact]
    public void RepeatFiresOnlyTheBindMarkedRepeat()
    {
        var core = Load("bind = , Q, exec, notepad\nbinde = , Q, killactive\n");
        Appear(core, 1);

        var first = Press(core, "Q");
        var second = Press(core, "Q");

        Assert.Equal("notepad", first.Launch?.CommandLine);
        Assert.Equal(new WindowId(1), first.Close);
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
        var core = Load("bind = , mouse_down, killactive\nbind = , mouse_down, workspace, 2\n");
        Appear(core, 1);
        Appear(core, 2);

        var first = core.Handle(new WheelTurned("mouse_down", 1_000));
        var blocked = core.Handle(new WheelTurned("mouse_down", 1_200));
        var later = core.Handle(new WheelTurned("mouse_down", 1_300));

        Assert.True(first.Consumed);
        Assert.Equal(new WindowId(2), first.Close);
        Assert.Equal(new WorkspaceId("2"), first.State.ActiveWorkspace);
        Assert.True(blocked.Consumed);
        Assert.Equal(new WorkspaceId("2"), blocked.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("2"), later.State.ActiveWorkspace);
    }

    [Fact]
    public void EmptyMouseBindConsumesAndTheOtherMouseBindStillRuns()
    {
        var core = Load("bindm = , mouse:272,\nbindm = , mouse:272, movewindow\n");
        Appear(core, 1);

        var pressed = core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(500, 400) });

        Assert.True(pressed.Consumed);
    }

    private static void Appear(WindowManager core, long window) =>
        core.Handle(new WindowAppeared(new WindowId(window), new Rectangle(0, 0, 1920, 1080)));

    private static Outcome Press(WindowManager core, string key) =>
        core.Handle(new KeyPressed(key));

    private static Outcome Release(WindowManager core, string key) =>
        core.Handle(new KeyReleased(key));
}
