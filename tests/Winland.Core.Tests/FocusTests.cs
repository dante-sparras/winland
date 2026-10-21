using Winland;

namespace Winland.Tests;

public class FocusTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void AnApplicationCannotStealFocus()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var stolen = core.Handle(new WindowFocused(new WindowId(1), FocusCause.Activated));

        Assert.Equal(new WindowId(2), stolen.Focus);
        Assert.Equal(["urgent>>1"], Lines(stolen));
        Assert.Equal(new WindowId(2), Active(core));
    }

    [Fact]
    public void FocusOnActivateAcceptsTheApplication()
    {
        var core = new Core(ConfigParser.Parse("misc:focus_on_activate = true\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var stolen = core.Handle(new WindowFocused(new WindowId(1), FocusCause.User) { Clicked = new WindowId(2) });

        Assert.Null(stolen.Focus);
        Assert.Equal(["activewindow>>,", "activewindowv2>>1"], Lines(stolen));
        Assert.Equal(new WindowId(1), Active(core));
    }

    [Fact]
    public void AWindowTakingTheForegroundWithoutInputIsRefusedEvenWithFocusOnActivate()
    {
        var core = new Core(ConfigParser.Parse("misc:focus_on_activate = true\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var stolen = core.Handle(new WindowFocused(new WindowId(1), FocusCause.Activated));

        Assert.Equal(new WindowId(2), stolen.Focus);
        Assert.Equal(new WindowId(2), Active(core));
    }

    [Fact]
    public void AWindowTakingTheForegroundAfterAClickInAnotherProgramNeedsFocusOnActivate()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area) { ProcessId = 10 });
        core.Handle(new WindowAppeared(new WindowId(2), Area) { ProcessId = 20 });

        var refused = core.Handle(new WindowFocused(new WindowId(1), FocusCause.User) { Clicked = new WindowId(2) });

        Assert.Equal(new WindowId(2), refused.Focus);
        Assert.Equal(["urgent>>1"], Lines(refused));
    }

    [Fact]
    public void AClickOnTheWindowItsOwnProgramOrAnUnmanagedWindowIsTheUsersChange()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area) { ProcessId = 10 });
        core.Handle(new WindowAppeared(new WindowId(2), Area) { ProcessId = 10 });
        core.Handle(new WindowAppeared(new WindowId(3), Area) { ProcessId = 30 });

        core.Handle(new WindowFocused(new WindowId(1), FocusCause.User) { Clicked = new WindowId(1) });
        Assert.Equal(new WindowId(1), Active(core));

        core.Handle(new WindowFocused(new WindowId(2), FocusCause.User) { Clicked = new WindowId(1) });
        Assert.Equal(new WindowId(2), Active(core));

        core.Handle(new WindowFocused(new WindowId(3), FocusCause.User) { Clicked = new WindowId(99) });
        Assert.Equal(new WindowId(3), Active(core));

        core.Handle(new WindowFocused(new WindowId(1), FocusCause.User));
        Assert.Equal(new WindowId(1), Active(core));
    }

    [Fact]
    public void AClickStillChangesFocusWhenActivationIsRefused()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var clicked = core.Handle(new WindowFocused(new WindowId(1), FocusCause.User));

        Assert.Null(clicked.Focus);
        Assert.Equal(["activewindow>>,", "activewindowv2>>1"], Lines(clicked));
    }

    [Theory]
    [InlineData("noinitialfocus")]
    [InlineData("no_initial_focus")]
    public void ANewClientWithNoInitialFocusDoesNotTakeFocus(string effect)
    {
        var core = new Core(ConfigParser.Parse($"windowrulev2 = {effect}, class:^(popup)$\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var opened = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { Class = "popup" },
        });

        Assert.Contains(opened.State.Clients, client => client.Window == new WindowId(2));
        Assert.Equal(new WindowId(1), opened.Focus);
        Assert.Equal(["openwindow>>2,1,,popup,"], Lines(opened));

        var mapped = core.Handle(new WindowFocused(new WindowId(2), FocusCause.User));
        Assert.Equal(new WindowId(1), mapped.Focus);
        Assert.Equal(new WindowId(1), Active(core));

        var clicked = core.Handle(new WindowFocused(new WindowId(2), FocusCause.User));
        Assert.Null(clicked.Focus);
        Assert.Equal(new WindowId(2), Active(core));
    }

    [Theory]
    [InlineData("nofocus")]
    [InlineData("no_focus")]
    public void NoFocusRefusesTheClient(string effect)
    {
        var core = new Core(ConfigParser.Parse($"windowrulev2 = {effect}, class:^(bar)$\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        var opened = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { Class = "bar" },
        });
        Assert.Equal(new WindowId(1), opened.Focus);

        core.Handle(new WindowFocused(new WindowId(2), FocusCause.User));
        var clicked = core.Handle(new WindowFocused(new WindowId(2), FocusCause.User));

        Assert.Equal(new WindowId(1), clicked.Focus);
        Assert.Equal(new WindowId(1), Active(core));
    }

    [Fact]
    public void ALaterRuleCanAllowFocusAgain()
    {
        var core = new Core(ConfigParser.Parse(
            "windowrulev2 = nofocus, class:^(bar)$\n" +
            "windowrulev2 = nofocus 0, class:^(bar)$\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var opened = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { Class = "bar" },
        });

        Assert.Null(opened.Focus);
        Assert.Equal(new WindowId(2), Active(core));
    }

    [Theory]
    [InlineData("stayfocused")]
    [InlineData("stay_focused")]
    public void StayFocusedKeepsTheClient(string effect)
    {
        var core = new Core(ConfigParser.Parse($"windowrulev2 = {effect}, class:^(pin)$\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Class = "pin" },
        });

        var opened = core.Handle(new WindowAppeared(new WindowId(2), Area));
        Assert.Equal(new WindowId(1), opened.Focus);
        Assert.Contains(opened.State.Clients, client => client.Window == new WindowId(2));

        var clicked = core.Handle(new WindowFocused(new WindowId(2), FocusCause.User));
        Assert.Equal(new WindowId(1), clicked.Focus);
        Assert.Equal(new WindowId(1), Active(core));

        var moved = core.Handle(new Dispatch("movefocus", "r"));
        Assert.Equal(new WindowId(2), moved.Focus);
        Assert.Equal(new WindowId(2), Active(core));
    }

    [Fact]
    public void OurOwnForegroundRequestIsAccepted()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = stayfocused, class:^(pin)$\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Class = "pin" },
        });
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var requested = core.Handle(new WindowFocused(new WindowId(2), FocusCause.Requested));

        Assert.Null(requested.Focus);
        Assert.Equal(new WindowId(2), Active(core));
    }

    [Fact]
    public void MoveFocusSkipsAClientThatRefusesFocus()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = nofocus, class:^(bar)$\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { Class = "bar" },
        });
        core.Handle(new WindowFocused(new WindowId(2), FocusCause.User));

        var moved = core.Handle(new Dispatch("movefocus", "r"));

        Assert.Null(moved.Focus);
        Assert.Equal(new WindowId(1), Active(core));
    }

    [Fact]
    public void FocusOnActivateIsHonoredInABlock()
    {
        var document = ConfigParser.Parse("misc {\n    focus_on_activate = true\n    middle_click_paste = true\n}\n");

        Assert.Equal(new Setting("misc:focus_on_activate", "true"), Assert.Single(document.Settings));
        Assert.Equal(new ConfigWarning(3, "Unsupported keyword 'misc:middle_click_paste'."), Assert.Single(document.Warnings));
    }

    [Fact]
    public void AnUnknownWindowStealingFocusIsSentBack()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var stolen = core.Handle(new WindowFocused(new WindowId(99), FocusCause.Activated));

        Assert.Equal(new WindowId(1), stolen.Focus);
        Assert.Empty(stolen.Events);
        Assert.Equal(new WindowId(1), Active(core));
    }

    private static WindowId? Active(Core core)
    {
        var outcome = core.Handle(new Dispatch("killactive"));
        return outcome.Close;
    }

    private static IReadOnlyList<string> Lines(Outcome outcome) =>
        CommandProtocol.FormatStream(outcome.Events, outcome.State);
}
