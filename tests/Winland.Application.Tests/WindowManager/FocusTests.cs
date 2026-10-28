namespace Winland.Application.Tests;

public class FocusTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void AnApplicationCannotStealFocus()
    {
        var core = new WindowManager();
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
        var core = new WindowManager(ConfigParser.Parse("misc:focus_on_activate = true\n"));
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
        var core = new WindowManager(ConfigParser.Parse("misc:focus_on_activate = true\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var stolen = core.Handle(new WindowFocused(new WindowId(1), FocusCause.Activated));

        Assert.Equal(new WindowId(2), stolen.Focus);
        Assert.Equal(new WindowId(2), Active(core));
    }

    [Fact]
    public void AWindowTakingTheForegroundAfterAClickInAnotherProgramNeedsFocusOnActivate()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area) { ProcessId = 10 });
        core.Handle(new WindowAppeared(new WindowId(2), Area) { ProcessId = 20 });

        var refused = core.Handle(new WindowFocused(new WindowId(1), FocusCause.User) { Clicked = new WindowId(2) });

        Assert.Equal(new WindowId(2), refused.Focus);
        Assert.Equal(["urgent>>1"], Lines(refused));
    }

    [Fact]
    public void AClickOnTheWindowItsOwnProgramOrAnUnmanagedWindowIsTheUsersChange()
    {
        var core = new WindowManager();
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
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var clicked = core.Handle(new WindowFocused(new WindowId(1), FocusCause.User));

        Assert.Null(clicked.Focus);
        Assert.Equal(["activewindow>>,", "activewindowv2>>1"], Lines(clicked));
    }

    [Theory]
    [InlineData("no_initial_focus")]
    public void ANewClientWithNoInitialFocusDoesNotTakeFocus(string effect)
    {
        var core = new WindowManager(ConfigParser.Parse($"windowrule = {effect}, class:^(popup)$\n"));
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
    [InlineData("no_focus")]
    public void NoFocusRefusesTheClient(string effect)
    {
        var core = new WindowManager(ConfigParser.Parse($"windowrule = {effect}, class:^(bar)$\n"));
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
        var core = new WindowManager(ConfigParser.Parse(
            "windowrule = no_focus, class:^(bar)$\n" +
            "windowrule = no_focus 0, class:^(bar)$\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var opened = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { Class = "bar" },
        });

        Assert.Null(opened.Focus);
        Assert.Equal(new WindowId(2), Active(core));
    }

    [Fact]
    public void OurOwnForegroundRequestIsAccepted()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = stayfocused, class:^(pin)$\n"));
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
        var core = new WindowManager(ConfigParser.Parse("windowrule = no_focus, class:^(bar)$\n"));
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
    public void AnUnknownWindowTakingFocusIsLeftToWindows()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var stolen = core.Handle(new WindowFocused(new WindowId(99), FocusCause.Activated));

        Assert.Null(stolen.Focus);
        Assert.Null(Active(core));
    }

    [Fact]
    public void AWorkspaceShownAgainDoesNotFocusAWindowThatTakesNoFocus()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Execute("dispatch workspace 2");
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        core.Execute("setprop address:0x2 no_focus true");
        core.Execute("dispatch workspace 1");

        core.Execute("dispatch workspace 2");

        Assert.False(System.Text.Json.JsonDocument.Parse(core.Execute("j/activewindow").Text).RootElement.TryGetProperty("address", out _));
    }

    [Fact]
    public void ADwindleSplitGoesNextToTheClientTheUserClicked()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        core.Handle(new WindowFocused(new WindowId(1), FocusCause.User));

        var opened = core.Handle(new WindowAppeared(new WindowId(3), Area));

        Assert.Equal(new Rectangle(0, 0, 960, 540), BoundsOf(opened, 1));
        Assert.Equal(new Rectangle(0, 540, 960, 540), BoundsOf(opened, 3));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(opened, 2));
    }

    [Fact]
    public void AWindowOpenedOnAMonitorWithoutFocusIsNotFirstInTheFocusHistory()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 800, 600);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 800, 0, 800, 600);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(3), left.WorkArea));

        var outcome = core.Handle(new Dispatch("focusurgentorlast"));

        // Client 2 was never focused. The history is 3 then 1, and Winland takes its second oldest end: the focused Client itself.
        Assert.Equal(new WindowId(3), outcome.Focus);
    }

    [Fact]
    public void TheForegroundReportOfTheFocusedClientChangesNothing()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var reported = core.Handle(new WindowFocused(new WindowId(2), FocusCause.Requested));

        Assert.Empty(reported.Placements);
        Assert.Empty(reported.Show);
        Assert.Empty(reported.Hide);
    }

    [Fact]
    public void TheForegroundReportAfterAFocusDispatcherDoesNotScrollTheStripAgain()
    {
        var core = new WindowManager(ConfigParser.Parse("general:layout = scrolling\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        core.Handle(new WindowAppeared(new WindowId(3), Area));
        var focused = core.Handle(new Dispatch("focuswindow", "address:0x1"));
        Assert.NotEmpty(focused.Placements);

        var reported = core.Handle(new WindowFocused(new WindowId(1), FocusCause.Requested));

        Assert.Empty(reported.Placements);
    }

    private static WindowId? Active(WindowManager core)
    {
        var outcome = core.Handle(new Dispatch("killactive"));
        return outcome.Close;
    }

    private static IReadOnlyList<string> Lines(Outcome outcome) =>
        CommandProtocol.FormatStream(outcome.Events, outcome.State);
}
