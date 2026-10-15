using Winland;

namespace Winland.Tests;

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
        Assert.Equal("killactive", outcome.Dispatcher);
        Assert.Equal(new WindowId(1), outcome.Close);
        Assert.False(Release(core, "Q").Consumed);
        Assert.False(Release(core, "Super_L").Consumed);
    }

    [Fact]
    public void IgnoreModifiersMatchesWhateverElseIsHeld()
    {
        var core = Load("bindi = , Q, killactive");
        Appear(core, 1);
        Press(core, "Super_L");
        Press(core, "Shift_L");

        Assert.Equal("killactive", Press(core, "Q").Dispatcher);
    }

    [Fact]
    public void IgnoreModifiersDoesNotRequireTheListedModifier()
    {
        var core = Load("bindi = SUPER, Q, killactive");
        Appear(core, 1);

        Assert.Equal("killactive", Press(core, "Q").Dispatcher);
    }

    [Fact]
    public void TransparentBindStillRunsWhenAnotherBindUsesTheSameKey()
    {
        var core = Load("bind = SUPER, Q, killactive\nbindt = SUPER, Q, exec, notepad\n");
        Appear(core, 1);
        Press(core, "Super_L");

        var outcome = Press(core, "Q");

        Assert.True(outcome.Consumed);
        Assert.Equal(new WindowId(1), outcome.Close);
        Assert.Equal("notepad", outcome.Launch?.CommandLine);
    }

    [Fact]
    public void SeparateBindFiresForEachListedKey()
    {
        var core = Load("binds = SUPER, A&B, killactive");
        Appear(core, 1);
        Press(core, "Super_L");

        Assert.Equal("killactive", Press(core, "A").Dispatcher);
        Release(core, "A");
        Assert.Equal("killactive", Press(core, "B").Dispatcher);
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
    public void LongPressFiresOnlyAfterTheKeyStaysDown()
    {
        var core = Load("bindo = SUPER, Q, killactive");
        Appear(core, 1);
        Press(core, "Super_L");

        var pressed = Press(core, "Q");

        Assert.Null(pressed.Dispatcher);
        Assert.False(pressed.Consumed);
        Assert.Equal("killactive", core.Handle(new LongPressElapsed()).Dispatcher);
    }

    [Fact]
    public void LongPressDoesNotFireWhenTheKeyIsReleasedFirst()
    {
        var core = Load("bindo = SUPER, Q, killactive");
        Appear(core, 1);
        Press(core, "Super_L");
        Press(core, "Q");
        Release(core, "Q");

        var elapsed = core.Handle(new LongPressElapsed());

        Assert.Null(elapsed.Dispatcher);
        Assert.Null(elapsed.Close);
    }

    [Fact]
    public void ClickBindFiresOnReleaseInsideTheThreshold()
    {
        var core = Load("binds:drag_threshold = 10\nbindc = SUPER, V, killactive\n");
        Appear(core, 1);
        Press(core, "Super_L");
        core.Handle(new PointerMoved(new Point(0, 0)));
        Press(core, "V");
        core.Handle(new PointerMoved(new Point(3, 4)));

        var released = Release(core, "V");

        Assert.Equal("killactive", released.Dispatcher);
        Assert.Equal(new WindowId(1), released.Close);
    }

    [Fact]
    public void ClickBindDoesNotFireOnceThePointerLeavesTheThreshold()
    {
        var core = Load("binds:drag_threshold = 10\nbindc = SUPER, V, killactive\n");
        Appear(core, 1);
        Press(core, "Super_L");
        core.Handle(new PointerMoved(new Point(0, 0)));
        Press(core, "V");
        core.Handle(new PointerMoved(new Point(40, 0)));

        var released = Release(core, "V");

        Assert.Null(released.Dispatcher);
        Assert.Null(released.Close);
        Assert.True(released.Consumed);
    }

    [Fact]
    public void DragBindFiresOnReleaseOutsideTheThreshold()
    {
        var core = Load("binds:drag_threshold = 10\nbindg = SUPER, G, killactive\n");
        Appear(core, 1);
        Press(core, "Super_L");
        core.Handle(new PointerMoved(new Point(0, 0)));
        Assert.Null(Press(core, "G").Dispatcher);
        core.Handle(new PointerMoved(new Point(30, 0)));

        Assert.Equal("killactive", Release(core, "G").Dispatcher);
    }

    [Fact]
    public void DragBindDoesNotFireWhenThePointerStaysInsideTheThreshold()
    {
        var core = Load("binds:drag_threshold = 10\nbindg = SUPER, G, killactive\n");
        Appear(core, 1);
        Press(core, "Super_L");
        core.Handle(new PointerMoved(new Point(5, 5)));
        Press(core, "G");
        core.Handle(new PointerMoved(new Point(8, 6)));

        var released = Release(core, "G");

        Assert.Null(released.Dispatcher);
        Assert.True(released.Consumed);
    }

    [Fact]
    public void ScrollBindFiresThenWaitsForTheDelay()
    {
        var core = Load("binds:scroll_event_delay = 300\nbind = SUPER, mouse_down, killactive\n");
        Appear(core, 1);
        Press(core, "Super_L");

        Assert.Equal("killactive", core.Handle(new WheelTurned("mouse_down", 1_000)).Dispatcher);

        var blocked = core.Handle(new WheelTurned("mouse_down", 1_200));
        Assert.Null(blocked.Dispatcher);
        Assert.False(blocked.Consumed);

        Assert.Equal("killactive", core.Handle(new WheelTurned("mouse_down", 1_300)).Dispatcher);
    }

    [Fact]
    public void MouseButtonNameRunsANormalBind()
    {
        var core = Load("bind = SUPER, mouse:272, killactive");
        Appear(core, 1);
        Press(core, "Super_L");

        var outcome = core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(4, 5) });

        Assert.Equal("killactive", outcome.Dispatcher);
        Assert.Equal(new WindowId(1), outcome.Close);
    }

    [Fact]
    public void UniversalBindRunsFromInsideASubmap()
    {
        var core = Load("submap = resize\nbindu = SUPER, Q, killactive\nbind = SUPER, H, killactive\nsubmap = reset\n");
        Appear(core, 1);
        Press(core, "Super_L");

        Assert.Equal("killactive", Press(core, "Q").Dispatcher);
        Assert.False(Press(core, "H").Consumed);
    }

    [Fact]
    public void DontInhibitFlagWarnsAndTheBindStillRuns()
    {
        var document = ConfigParser.Parse("bindp = SUPER, Q, killactive\n");

        Assert.Contains(document.Warnings, warning => warning.Message == "Bind flag 'p' is not honored.");
        var bind = Assert.IsType<Bind>(Assert.Single(document.Binds));
        Assert.False(bind.Flags.HasFlag(BindFlag.DontInhibit));

        var core = new Core();
        core.Handle(new ConfigLoaded(document));
        Appear(core, 1);
        Press(core, "Super_L");
        Assert.Equal("killactive", Press(core, "Q").Dispatcher);
    }

    private static Core Load(string text)
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(text)));
        return core;
    }

    private static void Appear(Core core, long window) =>
        core.Handle(new WindowAppeared(new WindowId(window), new Rectangle(0, 0, 1920, 1080)));

    private static Outcome Press(Core core, string key) =>
        core.Handle(new KeyPressed(key));

    private static Outcome Release(Core core, string key) =>
        core.Handle(new KeyReleased(key));
}
