using Winland;

namespace Winland.Tests;

public class PassTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

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
        Assert.True(Dispatchers.IsKnown("sendshortcut"));
        Assert.False(Dispatchers.IsKnown("pass"));
        Assert.False(Dispatchers.IsKnown("sendkeystate"));
        Assert.False(Dispatchers.IsKnown("toggleswallow"));
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
