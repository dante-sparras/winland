namespace Winland.Application.Tests;

public class BindFlagTests
{
    [Fact]
    public void NonConsumingBindRunsAndLetsTheKeyThrough()
    {
        var core = Load("bindn = SUPER, Q, killactive");
        Appear(core, 1);
        Press(core, "Super_L");

        var outcome = Press(core, "Q");

        Assert.False(outcome.Consumed);
        Assert.Equal(new WindowId(1), outcome.Close);
        Assert.False(Release(core, "Q").Consumed);
        Assert.False(Release(core, "Super_L").Consumed);
    }

    [Fact]
    public void AmpersandIsASingleKeyWithoutTheSeparateFlag()
    {
        var core = Load("bind = SUPER, A&B, killactive");
        Appear(core, 1);
        Press(core, "Super_L");

        Assert.False(Press(core, "A").Consumed);
        Assert.False(Press(core, "B").Consumed);
    }

    [Fact]
    public void ScrollBindFiresThenWaitsForTheDelay()
    {
        var core = Load("bind = SUPER, mouse_down, killactive\n");
        Appear(core, 1);
        Press(core, "Super_L");

        Assert.Equal(new WindowId(1), core.Handle(new WheelTurned("mouse_down", 1_000)).Close);

        var blocked = core.Handle(new WheelTurned("mouse_down", 1_200));
        Assert.False(blocked.Consumed);

        Assert.Equal(new WindowId(1), core.Handle(new WheelTurned("mouse_down", 1_300)).Close);
    }

    [Fact]
    public void MouseButtonNameRunsANormalBind()
    {
        var core = Load("bind = SUPER, mouse:272, killactive");
        Appear(core, 1);
        Press(core, "Super_L");

        var outcome = core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(4, 5) });

        Assert.Equal(new WindowId(1), outcome.Close);
    }

    private static void Appear(WindowManager core, long window) =>
        core.Handle(new WindowAppeared(new WindowId(window), new Rectangle(0, 0, 1920, 1080)));

    private static Outcome Press(WindowManager core, string key) =>
        core.Handle(new KeyPressed(key));

    private static Outcome Release(WindowManager core, string key) =>
        core.Handle(new KeyReleased(key));
}
