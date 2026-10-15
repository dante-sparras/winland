using Winland;

namespace Winland.Tests;

public class PassTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void PassSendsTheBoundKeyToTheWindowOnPressAndRelease()
    {
        var core = WithObs("bind = SUPER, F10, pass, class:^(obs)$\n");
        core.Handle(new KeyPressed("Super_L"));

        var pressed = core.Handle(new KeyPressed("F10") { ScanCode = 76 });
        var released = core.Handle(new KeyReleased("F10") { ScanCode = 76 });

        Assert.True(pressed.Consumed);
        Assert.Equal(new KeyRelay(new WindowId(2), "F10", KeyRelayPhase.Press) { Code = 76 }, Assert.Single(pressed.Relays));
        Assert.True(released.Consumed);
        Assert.Equal(new KeyRelay(new WindowId(2), "F10", KeyRelayPhase.Release) { Code = 76 }, Assert.Single(released.Relays));
    }

    [Fact]
    public void PassDoesNotChangeFocus()
    {
        var core = WithObs("bind = SUPER, F10, pass, class:^(obs)$\n");
        core.Handle(new KeyPressed("Super_L"));

        var pressed = core.Handle(new KeyPressed("F10"));

        Assert.Null(pressed.Focus);
        Assert.Empty(pressed.Events);
    }

    [Fact]
    public void PassFromThePipeHasNoKeyToSend()
    {
        var core = WithObs("");

        var reply = core.Execute("dispatch pass class:^(obs)$");

        Assert.Equal("pass: no key to pass", reply.Text);
        Assert.Empty(reply.Effect.Relays);
    }

    [Fact]
    public void PassToAMissingWindowIsAnError()
    {
        var core = WithObs("");

        var outcome = core.Handle(new Dispatch("pass", "class:^(nothing)$"));

        Assert.Equal("pass: window not found", outcome.Error);
        Assert.Empty(outcome.Relays);
    }

    [Fact]
    public void SendShortcutFromThePipeTapsTheKeyWithExactlyThoseModifiers()
    {
        var core = WithObs("");

        var outcome = core.Handle(new Dispatch("sendshortcut", "CTRL SHIFT, c, class:^(obs)$"));

        Assert.Null(outcome.Error);
        var relay = Assert.Single(outcome.Relays);
        Assert.Equal(new KeyRelay(new WindowId(2), "c", KeyRelayPhase.Tap) { Modifiers = Modifier.Ctrl | Modifier.Shift }, relay);
    }

    [Fact]
    public void SendShortcutWithNoWindowGoesToTheFocusedWindow()
    {
        var core = WithObs("");

        var outcome = core.Handle(new Dispatch("sendshortcut", ", Return,"));

        var relay = Assert.Single(outcome.Relays);
        Assert.Null(relay.Target);
        Assert.Equal(Modifier.None, relay.Modifiers);
    }

    [Fact]
    public void SendShortcutOnABindPressesOnPressAndReleasesOnRelease()
    {
        var core = WithObs("bind = SUPER, F9, sendshortcut, CTRL, F5, class:^(obs)$\n");
        core.Handle(new KeyPressed("Super_L"));

        var pressed = core.Handle(new KeyPressed("F9"));
        var released = core.Handle(new KeyReleased("F9"));

        Assert.Equal(KeyRelayPhase.Press, Assert.Single(pressed.Relays).Phase);
        Assert.Equal(KeyRelayPhase.Release, Assert.Single(released.Relays).Phase);
        Assert.Equal(Modifier.Ctrl, released.Relays[0].Modifiers);
    }

    [Theory]
    [InlineData("38", "code:38")]
    [InlineData("code:38", "code:38")]
    [InlineData("mouse:272", "mouse:272")]
    [InlineData("5", "5")]
    [InlineData("F12", "F12")]
    public void SendShortcutReadsKeycodesButtonsAndNames(string key, string sent)
    {
        var core = WithObs("");

        var outcome = core.Handle(new Dispatch("sendshortcut", $"SUPER, {key},"));

        Assert.Null(outcome.Error);
        Assert.Equal(sent, Assert.Single(outcome.Relays).Key);
    }

    [Theory]
    [InlineData("SUPER", "sendshortcut: invalid args")]
    [InlineData("SUPER, mouse:100,", "sendshortcut: invalid mouse button")]
    [InlineData("SUPER, NoSuchKey,", "sendshortcut: key not found")]
    [InlineData("SUPER, , ", "sendshortcut: invalid key")]
    [InlineData("HYPER, c,", "sendshortcut: invalid modifiers")]
    [InlineData("SUPER, c, class:^(nothing)$", "sendshortcut: window not found")]
    public void SendShortcutRejectsBadArguments(string arguments, string error)
    {
        var core = WithObs("");

        var outcome = core.Handle(new Dispatch("sendshortcut", arguments));

        Assert.Equal(error, outcome.Error);
        Assert.Empty(outcome.Relays);
    }

    [Theory]
    [InlineData("down", new[] { KeyRelayPhase.Press })]
    [InlineData("repeat", new[] { KeyRelayPhase.Press, KeyRelayPhase.Press })]
    [InlineData("up", new[] { KeyRelayPhase.Release })]
    public void SendKeyStateSendsTheRequestedEdges(string state, KeyRelayPhase[] phases)
    {
        var core = WithObs("");

        var outcome = core.Handle(new Dispatch("sendkeystate", $"ALT, F4, {state}, class:^(obs)$"));

        Assert.Equal(phases, outcome.Relays.Select(relay => relay.Phase));
        Assert.All(outcome.Relays, relay => Assert.Equal(Modifier.Alt, relay.Modifiers));
    }

    [Fact]
    public void SendKeyStateRejectsAnUnknownState()
    {
        var core = WithObs("");

        var outcome = core.Handle(new Dispatch("sendkeystate", "ALT, F4, sideways, class:^(obs)$"));

        Assert.Equal("sendkeystate: invalid state, must be 'down', 'repeat', or 'up'", outcome.Error);
    }

    [Fact]
    public void APassBindRunsOnTheReleaseWithoutTheReleaseFlag()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = SUPER, F10, pass, class:obs\n")));
        var session = core.KeyState;
        core.Decide(new KeyPressed("Super_L"), session);

        var press = core.Decide(new KeyPressed("F10") { ScanCode = 76 }, session);
        var release = core.Decide(new KeyReleased("F10") { ScanCode = 76 }, session);

        Assert.Equal(new BindTrigger("F10", 76, Pressed: true), press.Trigger);
        Assert.Equal("pass", Assert.Single(release.Dispatches).Dispatcher);
        Assert.Equal(new BindTrigger("F10", 76, Pressed: false), release.Trigger);
        Assert.True(release.Consumed);
    }

    [Fact]
    public void AnOrdinaryBindDoesNotRunOnTheRelease()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = SUPER, F10, exec, notepad\n")));
        var session = core.KeyState;
        core.Decide(new KeyPressed("Super_L"), session);
        core.Decide(new KeyPressed("F10"), session);

        var release = core.Decide(new KeyReleased("F10"), session);

        Assert.Empty(release.Dispatches);
    }

    [Fact]
    public void TheDispatchersAreKnown()
    {
        Assert.True(Dispatchers.IsKnown("pass"));
        Assert.True(Dispatchers.IsKnown("sendshortcut"));
        Assert.True(Dispatchers.IsKnown("sendkeystate"));
    }

    private static Core WithObs(string config)
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(config)));
        core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Class = "kitty" } });
        core.Handle(new WindowAppeared(new WindowId(2), Area) { Traits = WindowTraits.Normal with { Class = "obs" } });
        core.Handle(new WindowFocused(new WindowId(1), FocusCause.User));
        return core;
    }
}
