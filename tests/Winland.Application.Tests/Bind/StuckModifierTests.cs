
namespace Winland.Application.Tests;

// The OS keeps a key logically down until it sees that key's release. A release is
// swallowed only when its press was swallowed too, so no key is ever left down. Super and
// Alt get a mask key while the chord runs, so their release does not open Start or a menu.
public class StuckModifierTests
{
    [Fact]
    public void SuperReleaseAfterAChordPassesAndTheChordMasksSuper()
    {
        var (core, session) = Load("bind = SUPER, T, exec, teams");

        var super = core.Decide(new KeyPressed("Super_L"), session);
        var t = core.Decide(new KeyPressed("T"), session);
        var tUp = core.Decide(new KeyReleased("T"), session);
        var superUp = core.Decide(new KeyReleased("Super_L"), session);

        Assert.False(super.Consumed);
        Assert.False(super.Mask);
        Assert.True(t.Consumed);
        Assert.True(t.Mask);
        Assert.Equal("exec", t.Dispatcher);
        Assert.True(tUp.Consumed);
        Assert.False(tUp.Mask);
        Assert.False(superUp.Consumed);
        Assert.False(superUp.Mask);
    }

    [Fact]
    public void SuperReleasedBeforeTheKeyStillPasses()
    {
        var (core, session) = Load("bind = SUPER, T, exec, teams");
        core.Decide(new KeyPressed("Super_L"), session);
        core.Decide(new KeyPressed("T"), session);

        var superUp = core.Decide(new KeyReleased("Super_L"), session);
        var tUp = core.Decide(new KeyReleased("T"), session);

        Assert.False(superUp.Consumed);
        Assert.True(tUp.Consumed);
        Assert.False(core.Decide(new KeyPressed("T"), session).Consumed);
    }

    [Fact]
    public void EveryHeldModifierReleasePasses()
    {
        var (core, session) = Load("bind = SUPER CTRL ALT SHIFT, T, killactive");
        foreach (var key in new[] { "Super_L", "Control_L", "Alt_L", "Shift_L" })
            Assert.False(core.Decide(new KeyPressed(key), session).Consumed);

        var t = core.Decide(new KeyPressed("T"), session);
        Assert.True(t.Consumed);
        Assert.True(t.Mask);
        Assert.True(core.Decide(new KeyReleased("T"), session).Consumed);

        foreach (var key in new[] { "Super_L", "Control_L", "Alt_L", "Shift_L" })
            Assert.False(core.Decide(new KeyReleased(key), session).Consumed);
    }

    [Theory]
    [InlineData("CTRL", "Control_L")]
    [InlineData("SHIFT", "Shift_R")]
    public void ChordWithoutSuperOrAltIsNotMasked(string modifier, string key)
    {
        var (core, session) = Load($"bind = {modifier}, T, killactive");
        core.Decide(new KeyPressed(key), session);

        var t = core.Decide(new KeyPressed("T"), session);

        Assert.True(t.Consumed);
        Assert.False(t.Mask);
        Assert.False(core.Decide(new KeyReleased(key), session).Consumed);
    }

    [Fact]
    public void AltChordIsMasked()
    {
        var (core, session) = Load("bind = ALT, T, killactive");
        core.Decide(new KeyPressed("Alt_R"), session);

        Assert.True(core.Decide(new KeyPressed("T"), session).Mask);
        Assert.False(core.Decide(new KeyReleased("Alt_R"), session).Consumed);
    }

    [Fact]
    public void SuperIsMaskedOncePerHold()
    {
        var (core, session) = Load("bind = SUPER, T, killactive");
        core.Decide(new KeyPressed("Super_L"), session);

        var first = core.Decide(new KeyPressed("T"), session);
        core.Decide(new KeyReleased("T"), session);
        var second = core.Decide(new KeyPressed("T"), session);
        core.Decide(new KeyReleased("T"), session);
        core.Decide(new KeyReleased("Super_L"), session);
        core.Decide(new KeyPressed("Super_L"), session);
        var third = core.Decide(new KeyPressed("T"), session);

        Assert.True(first.Mask);
        Assert.False(second.Mask);
        Assert.True(third.Mask);
    }

    [Fact]
    public void SuperAloneIsNotMasked()
    {
        var (core, session) = Load("bind = SUPER, T, killactive");

        var down = core.Decide(new KeyPressed("Super_L"), session);
        var up = core.Decide(new KeyReleased("Super_L"), session);

        Assert.False(down.Mask);
        Assert.False(up.Consumed);
        Assert.False(up.Mask);
    }

    [Fact]
    public void ReleaseOfAKeyPressedWithoutTheBindsModifiersPassesThrough()
    {
        var (core, session) = Load("bindr = SUPER, T, killactive");
        Assert.False(core.Decide(new KeyPressed("T"), session).Consumed);
        core.Decide(new KeyPressed("Super_L"), session);

        var up = core.Decide(new KeyReleased("T"), session);

        Assert.Null(up.Dispatcher);
        Assert.False(up.Consumed);
    }

    [Fact]
    public void RepeatThatStartsConsumingDoesNotSwallowTheReleaseOfAPassedPress()
    {
        var (core, session) = Load("binde = SUPER, T, killactive");
        Assert.False(core.Decide(new KeyPressed("T"), session).Consumed);
        core.Decide(new KeyPressed("Super_L"), session);

        var repeat = core.Decide(new KeyPressed("T"), session);
        var up = core.Decide(new KeyReleased("T"), session);

        Assert.Equal("killactive", repeat.Dispatcher);
        Assert.True(repeat.Mask);
        Assert.False(up.Consumed);
    }

    [Fact]
    public void ReleaseBindOnTheModifierItselfPassesBothEdgesAndMasksTheRelease()
    {
        var (core, session) = Load("bindr = SUPER, Super_L, exec, launcher");

        var down = core.Decide(new KeyPressed("Super_L"), session);
        var up = core.Decide(new KeyReleased("Super_L"), session);

        // The OS saw this Super, so it sees the release too, after a mask so Start does not open.
        Assert.False(down.Consumed);
        Assert.False(down.Mask);
        Assert.False(up.Consumed);
        Assert.True(up.Mask);
        Assert.Equal("exec", up.Dispatcher);
    }

    [Fact]
    public void ReleaseBindOnASuperAlreadyMaskedByAChordSendsNoSecondMask()
    {
        var (core, session) = Load("""
            bind = SUPER, T, exec, teams
            bindr = SUPER, Super_L, exec, launcher
            """);

        core.Decide(new KeyPressed("Super_L"), session);
        var t = core.Decide(new KeyPressed("T"), session);
        core.Decide(new KeyReleased("T"), session);
        var up = core.Decide(new KeyReleased("Super_L"), session);

        Assert.True(t.Mask);
        Assert.False(up.Mask);
    }

    [Fact]
    public void MouseChordWithSuperMasksSuperAndPassesItsRelease()
    {
        var (core, session) = Load("bindm = SUPER, mouse:272, movewindow");
        core.Decide(new KeyPressed("Super_L"), session);

        var down = core.Decide(new PointerButtonPressed("mouse:272") { Position = new Point(0, 0) }, session);
        core.Decide(new PointerMoved(new Point(200, 200)), session);
        var superUp = core.Decide(new KeyReleased("Super_L"), session);
        var buttonUp = core.Decide(new PointerButtonReleased("mouse:272") { Position = new Point(200, 200) }, session);

        Assert.True(down.Consumed);
        Assert.True(down.Mask);
        Assert.False(superUp.Consumed);
        Assert.True(buttonUp.Consumed);
    }

    [Fact]
    public void ButtonReleaseIsNotSwallowedWhenItsPressPassed()
    {
        var (core, session) = Load("bindr = SUPER, mouse:272, killactive");
        Assert.False(core.Decide(new PointerButtonPressed("mouse:272"), session).Consumed);
        core.Decide(new KeyPressed("Super_L"), session);

        var up = core.Decide(new PointerButtonReleased("mouse:272"), session);

        Assert.Null(up.Dispatcher);
        Assert.False(up.Consumed);
    }

    [Fact]
    public void WheelChordWithSuperMasksSuper()
    {
        var (core, session) = Load("bind = SUPER, mouse_down, workspace, e+1");
        core.Decide(new KeyPressed("Super_L"), session);

        var wheel = core.Decide(new WheelTurned("mouse_down", 1000), session);

        Assert.True(wheel.Consumed);
        Assert.True(wheel.Mask);
        Assert.False(core.Decide(new KeyReleased("Super_L"), session).Consumed);
    }

    [Fact]
    public void LongPressWithSuperPassesTheSuperRelease()
    {
        var (core, session) = Load("bindo = SUPER, T, killactive");
        core.Decide(new KeyPressed("Super_L"), session);
        var down = core.Decide(new KeyPressed("T"), session);
        var fired = core.Decide(new LongPressElapsed(), session);
        var tUp = core.Decide(new KeyReleased("T"), session);
        var superUp = core.Decide(new KeyReleased("Super_L"), session);

        // The shell saw T while Super was down, so it does not open Start.
        Assert.False(down.Consumed);
        Assert.Equal("killactive", fired.Dispatcher);
        Assert.False(tUp.Consumed);
        Assert.False(superUp.Consumed);
    }

    [Fact]
    public void NonConsumingChordDoesNotMaskSuper()
    {
        var (core, session) = Load("bindn = SUPER, T, killactive");
        core.Decide(new KeyPressed("Super_L"), session);

        var t = core.Decide(new KeyPressed("T"), session);
        core.Decide(new KeyReleased("T"), session);
        var up = core.Decide(new KeyReleased("Super_L"), session);

        Assert.False(t.Consumed);
        Assert.False(t.Mask);
        Assert.False(up.Consumed);
    }

    [Fact]
    public void InhibitedSessionPassesTheModifierRelease()
    {
        var (core, session) = Load("bind = SUPER, T, killactive");
        core.Decide(new KeyPressed("Super_L"), session);
        core.Decide(new KeyPressed("T"), session);
        session.Inhibit(true);

        var up = core.Decide(new KeyReleased("Super_L"), session);

        Assert.False(up.Consumed);
    }

    [Fact]
    public void ForgetDropsKeysThatAreNoLongerDown()
    {
        var (core, session) = Load("bind = SUPER, T, killactive");
        core.Decide(new KeyPressed("Super_L"), session);
        core.Decide(new KeyPressed("T"), session);

        // The releases happened where the hook could not see them, such as the lock screen.
        BindMatcher.Forget(session, _ => false);

        Assert.False(core.Decide(new KeyPressed("T"), session).Consumed);
        Assert.False(core.Decide(new KeyReleased("T"), session).Consumed);
    }

    [Fact]
    public void ForgetKeepsKeysThatAreStillDown()
    {
        var (core, session) = Load("bind = SUPER, T, killactive");
        core.Decide(new KeyPressed("Super_L"), session);

        BindMatcher.Forget(session, key => key == "Super_L");

        Assert.True(core.Decide(new KeyPressed("T"), session).Consumed);
    }

    [Theory]
    [InlineData("bindt")]
    [InlineData("bindn")]
    public void ChordWithAConsumingAndASecondBindRunsBothMasksOnceAndPassesSuper(string second)
    {
        var (core, session) = Load($"bind = SUPER, F23, exec, first\n{second} = SUPER, F23, exec, second\n");
        core.Decide(new KeyPressed("Super_L"), session);

        var down = core.Decide(new KeyPressed("F23"), session);
        var repeat = core.Decide(new KeyPressed("F23"), session);
        var up = core.Decide(new KeyReleased("F23"), session);
        var superUp = core.Decide(new KeyReleased("Super_L"), session);

        Assert.Equal(new BindDispatch[] { new("exec", "first"), new("exec", "second") }, down.Dispatches);
        Assert.True(down.Consumed);
        Assert.True(down.Mask);
        Assert.True(repeat.Consumed);
        Assert.False(repeat.Mask);
        Assert.True(up.Consumed);
        Assert.False(superUp.Consumed);
    }

    [Fact]
    public void ChordWhoseBindsAllPassLetsEveryEdgeThroughUnmasked()
    {
        var (core, session) = Load("bindn = SUPER, F23, exec, first\nbindn = SUPER, F23, exec, second\n");
        core.Decide(new KeyPressed("Super_L"), session);

        var down = core.Decide(new KeyPressed("F23"), session);
        var up = core.Decide(new KeyReleased("F23"), session);
        var superUp = core.Decide(new KeyReleased("Super_L"), session);

        Assert.Equal(2, down.Dispatches.Count);
        Assert.False(down.Consumed);
        Assert.False(down.Mask);
        Assert.False(up.Consumed);
        Assert.False(superUp.Consumed);
    }

    [Fact]
    public void SubmapChordUnderSuperPassesTheSuperReleaseInsideTheSubmap()
    {
        var core = Submaps(
            "bind = SUPER, R, submap, resize\n" +
            "submap = resize\n" +
            "bind = , L, killactive\n" +
            "bind = , catchall, submap, reset\n" +
            "submap = reset\n");
        var session = core.KeyState;

        core.Decide(new KeyPressed("Super_L"), session);
        var entered = Run(core, core.Decide(new KeyPressed("R"), session));
        var rUp = core.Decide(new KeyReleased("R"), session);
        var superUp = core.Decide(new KeyReleased("Super_L"), session);

        Assert.Equal("resize", session.Submap);
        Assert.True(entered.Consumed);
        Assert.True(entered.Mask);
        Assert.True(rUp.Consumed);
        Assert.False(superUp.Consumed);

        // The catchall swallows the next Super press, so the OS sees neither edge.
        var again = Run(core, core.Decide(new KeyPressed("Super_L"), session));
        var againUp = core.Decide(new KeyReleased("Super_L"), session);

        Assert.True(again.Consumed);
        Assert.False(again.Mask);
        Assert.True(againUp.Consumed);
        Assert.Equal("", session.Submap);
    }

    [Fact]
    public void ModifiersPressedBeforeASubmapPassTheirReleasesInsideIt()
    {
        var core = Submaps(
            "bind = SUPER SHIFT, R, submap, resize\n" +
            "submap = resize\n" +
            "bindr = , catchall, submap, reset\n" +
            "submap = reset\n");
        var session = core.KeyState;

        core.Decide(new KeyPressed("Super_L"), session);
        core.Decide(new KeyPressed("Shift_L"), session);
        var entered = Run(core, core.Decide(new KeyPressed("R"), session));

        Assert.Equal("resize", session.Submap);
        Assert.True(entered.Mask);
        Assert.True(core.Decide(new KeyReleased("R"), session).Consumed);
        Assert.False(core.Decide(new KeyReleased("Shift_L"), session).Consumed);
        Assert.False(core.Decide(new KeyReleased("Super_L"), session).Consumed);
    }

    [Fact]
    public void LongPressAndTapOnOneSuperChordMaskOnceAndPassSuper()
    {
        var (core, session) = Load("bind = SUPER, F23, exec, tap\nbindo = SUPER, F23, killactive\n");
        core.Decide(new KeyPressed("Super_L"), session);

        var down = core.Decide(new KeyPressed("F23"), session);
        var fired = core.Decide(new LongPressElapsed(), session);
        var up = core.Decide(new KeyReleased("F23"), session);
        var superUp = core.Decide(new KeyReleased("Super_L"), session);

        Assert.Equal("exec", down.Dispatcher);
        Assert.True(down.Consumed);
        Assert.True(down.Mask);
        Assert.NotNull(down.ArmLongPressMs);
        Assert.Equal("killactive", fired.Dispatcher);
        Assert.False(fired.Mask);
        Assert.True(up.Consumed);
        Assert.False(superUp.Consumed);
    }

    [Fact]
    public void LongPressOnlySuperChordMasksWhenItFires()
    {
        var (core, session) = Load("bindo = SUPER, F23, killactive");
        core.Decide(new KeyPressed("Super_L"), session);

        var down = core.Decide(new KeyPressed("F23"), session);
        var fired = core.Decide(new LongPressElapsed(), session);

        Assert.False(down.Consumed);
        Assert.False(down.Mask);
        Assert.True(fired.Mask);
        Assert.False(core.Decide(new KeyReleased("F23"), session).Consumed);
        Assert.False(core.Decide(new KeyReleased("Super_L"), session).Consumed);
    }

    [Fact]
    public void NonConsumingSuperDragPassesTheButtonAndSuper()
    {
        var (core, session) = Load("bindmn = SUPER, mouse:272, movewindow");
        core.Decide(new KeyPressed("Super_L"), session);

        var down = core.Decide(new PointerButtonPressed("mouse:272") { Position = new Point(0, 0) }, session);
        core.Decide(new PointerMoved(new Point(200, 200)), session);
        var up = core.Decide(new PointerButtonReleased("mouse:272") { Position = new Point(200, 200) }, session);
        var superUp = core.Decide(new KeyReleased("Super_L"), session);

        Assert.Equal("movewindow", down.Dispatcher);
        Assert.False(down.Consumed);
        Assert.False(down.Mask);
        Assert.False(up.Consumed);
        Assert.False(superUp.Consumed);
    }

    [Theory]
    [InlineData(0, "killactive")]
    [InlineData(200, "togglefloating")]
    public void SuperClickAndDragBindsOnOneButtonMaskSuperAndPassItsRelease(int moved, string expected)
    {
        var (core, session) = Load("bindc = SUPER, mouse:272, killactive\nbindg = SUPER, mouse:272, togglefloating\n");
        core.Decide(new KeyPressed("Super_L"), session);

        var down = core.Decide(new PointerButtonPressed("mouse:272") { Position = new Point(0, 0) }, session);
        core.Decide(new PointerMoved(new Point(moved, moved)), session);
        var up = core.Decide(new PointerButtonReleased("mouse:272") { Position = new Point(moved, moved) }, session);
        var superUp = core.Decide(new KeyReleased("Super_L"), session);

        Assert.True(down.Consumed);
        Assert.True(down.Mask);
        Assert.Equal(expected, up.Dispatcher);
        Assert.True(up.Consumed);
        Assert.False(superUp.Consumed);
    }

    [Fact]
    public void SuperMouseChordAfterASuperKeyChordIsNotMaskedTwice()
    {
        var (core, session) = Load("bind = SUPER, F23, killactive\nbindm = SUPER, mouse:272, movewindow\n");
        core.Decide(new KeyPressed("Super_L"), session);

        var key = core.Decide(new KeyPressed("F23"), session);
        core.Decide(new KeyReleased("F23"), session);
        var button = core.Decide(new PointerButtonPressed("mouse:272"), session);

        Assert.True(key.Mask);
        Assert.True(button.Consumed);
        Assert.False(button.Mask);
        Assert.True(core.Decide(new PointerButtonReleased("mouse:272"), session).Consumed);
        Assert.False(core.Decide(new KeyReleased("Super_L"), session).Consumed);
    }

    [Fact]
    public void ModifierOnlyBindSwallowsBothEdgesAndTheChordsUnderIt()
    {
        var (core, session) = Load("bind = , Super_L, exec, menu\nbind = SUPER, F23, killactive\n");

        var down = core.Decide(new KeyPressed("Super_L"), session);
        var chord = core.Decide(new KeyPressed("F23"), session);
        core.Decide(new KeyReleased("F23"), session);
        var up = core.Decide(new KeyReleased("Super_L"), session);

        // The OS never saw this Super, so it is never masked and never owed a release.
        Assert.Equal("exec", down.Dispatcher);
        Assert.True(down.Consumed);
        Assert.False(down.Mask);
        Assert.True(chord.Consumed);
        Assert.False(chord.Mask);
        Assert.True(up.Consumed);
        Assert.False(core.Decide(new KeyPressed("F23"), session).Consumed);
    }

    [Fact]
    public void NonConsumingModifierOnlyBindPassesBothEdges()
    {
        var (core, session) = Load("bindn = , Super_L, exec, menu");

        var down = core.Decide(new KeyPressed("Super_L"), session);
        var up = core.Decide(new KeyReleased("Super_L"), session);

        Assert.Equal("exec", down.Dispatcher);
        Assert.False(down.Consumed);
        Assert.False(up.Consumed);
    }

    [Fact]
    public void LongPressModifierOnlyBindMasksSuperAndPassesItsRelease()
    {
        var (core, session) = Load("bindo = , Super_L, exec, menu");

        var down = core.Decide(new KeyPressed("Super_L"), session);
        var fired = core.Decide(new LongPressElapsed(), session);
        var up = core.Decide(new KeyReleased("Super_L"), session);

        // The OS holds this Super, so the fire masks it and Start stays closed on release.
        Assert.False(down.Consumed);
        Assert.Equal("exec", fired.Dispatcher);
        Assert.True(fired.Mask);
        Assert.False(up.Consumed);
    }

    // The worker runs a decision's dispatchers; submap changes land in the core's session.
    private static BindDecision Run(WindowManager core, BindDecision decision)
    {
        foreach (var dispatch in decision.Dispatches)
            core.Handle(new Dispatch(dispatch.Dispatcher, dispatch.Arguments));

        return decision;
    }

    private static WindowManager Submaps(string config)
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(config)));
        return core;
    }

    private static (WindowManager WindowManager, BindSession Session) Load(string config)
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(config)));
        return (core, new BindSession());
    }
}
