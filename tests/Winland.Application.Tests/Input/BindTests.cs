
namespace Winland.Application.Tests;

public class BindTests
{
    [Fact]
    public void UnboundKeyPassesThrough()
    {
        var core = new WindowManager();

        AssertPassed(Press(core, "Q"));
        AssertPassed(Release(core, "Q"));
    }

    [Fact]
    public void SuperQRunsKillActiveAndAsksToCloseTheActiveClient()
    {
        var core = Load("bind = SUPER, Q, killactive");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");

        var outcome = Press(core, "Q");

        AssertRan(outcome, "killactive", 2);
        Assert.Equal(2, outcome.State.Clients.Count);
        Assert.Contains(outcome.State.Clients, client => client.Window == new WindowId(2));
    }

    [Fact]
    public void KillActiveDoesNotRemoveTheClientUntilTheWindowCloses()
    {
        var core = Load("bind = SUPER, Q, killactive");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");
        Press(core, "Q");
        Release(core, "Q");

        var closed = core.Handle(new WindowClosed(new WindowId(2)));

        Assert.DoesNotContain(closed.State.Clients, client => client.Window == new WindowId(2));
        AssertRan(Press(core, "Q"), "killactive", 1);
    }

    [Fact]
    public void FocusedClientIsTheOneKillActiveCloses()
    {
        var core = Load("bind = SUPER, Q, killactive");
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new WindowFocused(new WindowId(1)));
        Press(core, "Super_L");

        AssertRan(Press(core, "Q"), "killactive", 1);
    }

    [Fact]
    public void KillActiveWithNoClientRunsAndClosesNothing()
    {
        var core = Load("bind = SUPER, Q, killactive");
        Press(core, "Super_L");

        var outcome = Press(core, "Q");

        AssertRan(outcome, "killactive", null);
        Assert.Empty(outcome.State.Clients);
    }

    [Fact]
    public void UnmanagedFocusIsNotAClientToClose()
    {
        var core = Load("bind = SUPER, Q, killactive");
        Appear(core, 1);
        core.Handle(new WindowFocused(new WindowId(99)));
        Press(core, "Super_L");

        AssertRan(Press(core, "Q"), "killactive", null);
        Assert.Contains(core.Handle(new KeyReleased("Q")).State.Clients, client => client.Window == new WindowId(1));
    }

    [Fact]
    public void KeyWithoutTheModifierDoesNotMatch()
    {
        var core = Load("bind = SUPER, Q, killactive");
        Appear(core, 1);

        AssertPassed(Press(core, "Q"));
    }

    [Fact]
    public void ExtraModifierDoesNotMatch()
    {
        var core = Load("bind = SUPER, Q, killactive");
        Appear(core, 1);
        Press(core, "Super_L");
        Press(core, "Shift_L");

        AssertPassed(Press(core, "Q"));
    }

    [Theory]
    [InlineData("Super_L", "SUPER")]
    [InlineData("Super_R", "SUPER")]
    [InlineData("Alt_L", "ALT")]
    [InlineData("Alt_R", "ALT")]
    [InlineData("Control_L", "CTRL")]
    [InlineData("Control_R", "CTRL")]
    [InlineData("Shift_L", "SHIFT")]
    [InlineData("Shift_R", "SHIFT")]
    public void EachModifierKeyMatchesItsBind(string key, string modifier)
    {
        var core = Load($"bind = {modifier}, Q, killactive");
        Appear(core, 1);
        Press(core, key);

        AssertRan(Press(core, "Q"), "killactive", 1);
    }

    [Fact]
    public void EveryModifierInTheChordIsRequired()
    {
        var core = Load("bind = ALT CTRL SHIFT, Q, killactive");
        Appear(core, 1);
        Press(core, "Alt_L");
        Press(core, "Control_L");

        AssertPassed(Press(core, "Q"));
        Release(core, "Q");

        Press(core, "Shift_R");
        AssertRan(Press(core, "Q"), "killactive", 1);
    }

    [Fact]
    public void BindWithNoModifiersDoesNotMatchWhileAModifierIsHeld()
    {
        var core = Load("bind = , Q, killactive");
        Appear(core, 1);

        AssertRan(Press(core, "Q"), "killactive", 1);

        Release(core, "Q");
        Press(core, "Super_L");
        AssertPassed(Press(core, "Q"));
    }

    [Fact]
    public void BoundSuperComboConsumesTheKeyAndPassesTheSuperRelease()
    {
        var core = Load("bind = SUPER, L, killactive");
        Appear(core, 1);
        Press(core, "Super_L");

        AssertRan(Press(core, "L"), "killactive", 1);
        AssertCaught(Release(core, "L"));
        AssertPassed(Release(core, "Super_L"));
    }

    [Fact]
    public void UnboundSuperComboPassesTheKeyAndTheSuperKey()
    {
        var core = Load("bind = SUPER, Q, killactive");
        Press(core, "Super_L");

        AssertPassed(Press(core, "E"));
        AssertPassed(Release(core, "E"));
        AssertPassed(Release(core, "Super_L"));
    }

    [Fact]
    public void SuperAlonePassesThrough()
    {
        var core = Load("bind = SUPER, Q, killactive");

        AssertPassed(Press(core, "Super_L"));
        AssertPassed(Release(core, "Super_L"));
    }

    [Fact]
    public void PressBindFiresOnceAndConsumesTheRestOfTheSequence()
    {
        var core = Load("bind = SUPER, Q, killactive");
        Appear(core, 1);
        Press(core, "Super_L");

        AssertRan(Press(core, "Q"), "killactive", 1);
        AssertCaught(Press(core, "Q"));
        AssertCaught(Release(core, "Q"));
        AssertPassed(Release(core, "Super_L"));

        Press(core, "Super_L");
        AssertRan(Press(core, "Q"), "killactive", 1);
    }

    [Fact]
    public void RepeatBindFiresOnEachPressWhileHeld()
    {
        var core = Load("binde = SUPER, Q, killactive");
        Appear(core, 1);
        Press(core, "Super_L");

        AssertRan(Press(core, "Q"), "killactive", 1);
        AssertRan(Press(core, "Q"), "killactive", 1);
        AssertCaught(Release(core, "Q"));
    }

    [Fact]
    public void ReleaseBindFiresWhenTheKeyGoesUp()
    {
        var core = Load("bindr = SUPER, Q, killactive");
        Appear(core, 1);
        Press(core, "Super_L");

        AssertCaught(Press(core, "Q"));
        AssertRan(Release(core, "Q"), "killactive", 1);
        AssertPassed(Release(core, "Super_L"));
    }

    [Fact]
    public void ReleaseBindMatchesTheModifiersItsKeyWentDownUnder()
    {
        var core = Load("bindr = SUPER, Q, killactive");
        Appear(core, 1);
        Press(core, "Super_L");
        AssertCaught(Press(core, "Q"));
        AssertPassed(Press(core, "Shift_L"));

        var outcome = Release(core, "Q");

        Assert.True(outcome.Consumed);
        Assert.Equal(new WindowId(1), outcome.Close);
    }

    [Fact]
    public void ReleaseBindIsShadowedByAnotherKeyReleasedWhileItsKeyIsHeld()
    {
        var core = Load("bindr = SUPER, Q, killactive");
        Appear(core, 1);
        Press(core, "Super_L");
        AssertCaught(Press(core, "Q"));
        AssertPassed(Release(core, "Super_L"));

        var outcome = Release(core, "Q");

        Assert.True(outcome.Consumed);
        Assert.Null(outcome.Close);
    }

    [Fact]
    public void ModifierReleaseBindDoesNotRunAfterABindUsedTheModifier()
    {
        var core = Load("bindr = ALT, Alt_L, exec, notepad\nbind = ALT, P, killactive\n");
        Appear(core, 1);
        Press(core, "Alt_L");
        AssertRan(Press(core, "P"), "killactive", 1);
        Release(core, "P");

        var outcome = Release(core, "Alt_L");

        Assert.Null(outcome.Launch);
    }

    [Fact]
    public void LockedBindRunsWhileInputIsInhibitedAndANormalBindDoesNot()
    {
        var core = Load("bind = SUPER, Q, killactive\nbindl = SUPER, M, killactive\n");
        Appear(core, 1);
        core.Handle(new InputInhibited(true));
        Press(core, "Super_L");

        AssertPassed(Press(core, "Q"));
        Release(core, "Q");
        AssertRan(Press(core, "M"), "killactive", 1);

        Release(core, "M");
        core.Handle(new InputInhibited(false));
        AssertRan(Press(core, "Q"), "killactive", 1);
    }

    [Fact]
    public void LockedBindAlsoRunsWhenInputIsNotInhibited()
    {
        var core = Load("bindl = , M, killactive");
        Appear(core, 1);

        AssertRan(Press(core, "M"), "killactive", 1);
    }

    [Fact]
    public void MouseBindFiresOnTheButtonAndNotOnAKey()
    {
        var core = Load("bindm = SUPER, mouse:272, movewindow\nbind = SUPER, Q, killactive\n");
        Appear(core, 1);
        Press(core, "Super_L");

        var button = core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(500, 400) });
        AssertRan(button, "movewindow", null);
        Assert.Contains(button.State.Clients, client => client.Window == new WindowId(1));

        AssertPassed(Press(core, "mouse:272"));
        AssertRan(Press(core, "Q"), "killactive", 1);
        AssertCaught(core.Handle(new PointerButtonReleased("mouse:272")));
        AssertPassed(Release(core, "Super_L"));
    }

    [Fact]
    public void KeyboardBindDoesNotFireOnAMouseButton()
    {
        var core = Load("bind = SUPER, Q, killactive");
        Appear(core, 1);
        Press(core, "Super_L");

        AssertPassed(core.Handle(new PointerButtonPressed("Q")));
    }

    [Fact]
    public void OtherDispatcherIsReportedAndDoesNotClose()
    {
        var core = Load("bind = SUPER, E, exec, notepad");
        Appear(core, 1);
        Press(core, "Super_L");

        var outcome = Press(core, "E");

        AssertRan(outcome, "exec", null);
        Assert.Contains(outcome.State.Clients, client => client.Window == new WindowId(1));
    }

    [Fact]
    public void TwoBindsOnOneKeyLaunchBothPrograms()
    {
        var core = Load("bind = SUPER, E, exec, a\nbind = SUPER, E, exec, b");
        Press(core, "Super_L");

        var outcome = Press(core, "E");

        Assert.Equal(["a", "b"], outcome.Launches.Select(launch => launch.CommandLine));
    }

    [Fact]
    public void KeyNameMatchIsCaseInsensitive()
    {
        var core = Load("bind = super, q, killactive");
        Appear(core, 1);
        Press(core, "Super_R");

        AssertRan(Press(core, "Q"), "killactive", 1);
    }

    [Fact]
    public void ScanCodeMatchesACodeBind()
    {
        var core = Load("bind = SUPER, code:28, killactive");
        Appear(core, 1);
        Press(core, "Super_L");

        AssertPassed(Press(core, "Return"));
        Release(core, "Return");
        AssertRan(Press(core, "Return", 28), "killactive", 1);
    }

    [Fact]
    public void UnbindRemovesOnlyTheMatchingBind()
    {
        var core = Load("bind = SUPER, Q, killactive\nunbind = SUPER, Q\nbind = SUPER, R, killactive\n");
        Appear(core, 1);
        Press(core, "Super_L");

        AssertPassed(Press(core, "Q"));
        AssertRan(Press(core, "R"), "killactive", 1);
    }

    [Fact]
    public void UnbindAllClearsBinds()
    {
        var core = Load("bind = SUPER, Q, killactive\nunbind = all\n");
        Appear(core, 1);
        Press(core, "Super_L");

        AssertPassed(Press(core, "Q"));
    }

    [Fact]
    public void SubmapBindDoesNotFireOnTheBaseMap()
    {
        var core = Load("submap = resize\nbind = SUPER, H, killactive\nsubmap = reset\nbind = SUPER, Q, killactive\n");
        Appear(core, 1);
        Press(core, "Super_L");

        AssertPassed(Press(core, "H"));
        AssertRan(Press(core, "Q"), "killactive", 1);
    }

    [Fact]
    public void EveryBindOnAChordRunsInConfigOrder()
    {
        var core = Load("bind = , Q, killactive\nbind = , Q, workspace, 2\n");
        Appear(core, 1);
        Appear(core, 2);

        var outcome = Press(core, "Q");

        Assert.True(outcome.Consumed);
        Assert.Equal("killactive", outcome.Dispatcher);
        Assert.Equal(new WindowId(2), outcome.Close);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
    }

    [Fact]
    public void EmptyDispatcherConsumesTheKeyAndClosesNothing()
    {
        var core = Load("bind = , Q,\n");
        Appear(core, 1);

        var outcome = Press(core, "Q");

        AssertCaught(outcome);
        Assert.Contains(outcome.State.Clients, client => client.Window == new WindowId(1));
        AssertCaught(Release(core, "Q"));
    }

    [Fact]
    public void ModifierKeyBindClosesTheActiveClient()
    {
        var core = Load("bind = , Super_L, killactive");
        Appear(core, 1);

        AssertRan(Press(core, "Super_L"), "killactive", 1);
    }

    [Fact]
    public void LoadingConfigReplacesBinds()
    {
        var core = Load("bind = SUPER, Q, killactive");
        Appear(core, 1);
        core.Handle(new ConfigLoaded(ConfigParser.Parse("")));
        Press(core, "Super_L");

        AssertPassed(Press(core, "Q"));
    }

    private static WindowManager Load(string text)
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(text)));
        return core;
    }

    private static void Appear(WindowManager core, long window) =>
        core.Handle(new WindowAppeared(new WindowId(window), new Rectangle(0, 0, 1920, 1080)));

    private static Outcome Press(WindowManager core, string key, int scanCode = 0) =>
        core.Handle(new KeyPressed(key) { ScanCode = scanCode });

    private static Outcome Release(WindowManager core, string key) =>
        core.Handle(new KeyReleased(key));

    private static void AssertPassed(Outcome outcome)
    {
        Assert.False(outcome.Consumed);
        Assert.Null(outcome.Dispatcher);
        Assert.Null(outcome.Close);
        Assert.Empty(outcome.Placements);
    }

    private static void AssertCaught(Outcome outcome)
    {
        Assert.True(outcome.Consumed);
        Assert.Null(outcome.Dispatcher);
        Assert.Null(outcome.Close);
        Assert.Empty(outcome.Placements);
    }

    private static void AssertRan(Outcome outcome, string dispatcher, long? close)
    {
        Assert.True(outcome.Consumed);
        Assert.Equal(dispatcher, outcome.Dispatcher);
        Assert.Empty(outcome.Placements);
        if (close is long id)
            Assert.Equal(new WindowId(id), outcome.Close);
        else
            Assert.Null(outcome.Close);
    }
}
