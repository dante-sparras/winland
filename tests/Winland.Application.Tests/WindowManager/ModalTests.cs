
namespace Winland.Application.Tests;

public class ModalTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);
    private static readonly WindowId Owner = new(1);
    private static readonly WindowId Dialog = new(2);

    [Fact]
    public void AnOwnerShadowedByItsModalTakesNoFocus()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(Owner, Area));
        core.Handle(new WindowEnabledChanged(Owner, Enabled: false));
        core.Handle(new WindowAppeared(Dialog, Area) { Owner = Owner });

        var clicked = core.Handle(new WindowFocused(Owner, FocusCause.User));
        Assert.Equal(Dialog, clicked.Focus);
        Assert.Equal(255, AlphaOf(clicked, Owner));

        core.Handle(new Dispatch("focuswindow", "address:0x1"));
        Assert.Equal(Dialog, core.Handle(new Dispatch("killactive")).Close);

        core.Handle(new WindowEnabledChanged(Owner, Enabled: true));
        core.Handle(new WindowFocused(Owner, FocusCause.User));
        Assert.Equal(Owner, core.Handle(new Dispatch("killactive")).Close);
    }

    [Fact]
    public void ADisabledWindowWithNoOpenModalIsNotShadowed()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(Owner, Area) { Disabled = true });
        core.Handle(new WindowAppeared(new WindowId(3), Area));

        core.Handle(new WindowFocused(Owner, FocusCause.User));
        Assert.Equal(Owner, core.Handle(new Dispatch("killactive")).Close);
    }

    [Fact]
    public void BlockingCanBeTurnedOff()
    {
        var core = new WindowManager(ConfigParser.Parse("general:modal_parent_blocking = false\n"));
        core.Handle(new WindowAppeared(Owner, Area) { Disabled = true });
        core.Handle(new WindowAppeared(Dialog, Area) { Owner = Owner });

        var clicked = core.Handle(new WindowFocused(Owner, FocusCause.User));
        Assert.Equal(Owner, core.Handle(new Dispatch("killactive")).Close);
        Assert.Equal(255, AlphaOf(clicked, Owner));
        Assert.Equal("{\"option\":\"general:modal_parent_blocking\",\"bool\":false,\"set\":true}", core.Execute("getoption general:modal_parent_blocking").Text);
    }

    [Fact]
    public void TheModalRuleMatchesTheDialogOfADisabledOwner()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrulev2 = float, modal:1\n"));
        core.Handle(new WindowAppeared(Owner, Area) { Disabled = true });
        var opened = core.Handle(new WindowAppeared(Dialog, Area) { Owner = Owner });
        var free = core.Handle(new WindowAppeared(new WindowId(3), Area) { Owner = new WindowId(9) });

        Assert.True(Assert.Single(opened.State.Clients, client => client.Window == Dialog).Floating);
        Assert.False(Assert.Single(free.State.Clients, client => client.Window == new WindowId(3)).Floating);
    }

    [Fact]
    public void AFloatingWindowWithAnOwnerOpensOverTheMiddleOfItsOwner()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrulev2 = float, class:^(float)$\n"));
        var traits = WindowTraits.Normal with { Class = "float" };
        core.Handle(new WindowAppeared(Owner, Area) { Traits = traits, Bounds = new Rectangle(0, 0, 400, 300) });
        core.Handle(new Dispatch("movewindowpixel", "exact 100 100,address:0x1"));

        var dialog = core.Handle(new WindowAppeared(Dialog, Area) { Owner = Owner, Traits = traits, Bounds = new Rectangle(0, 0, 200, 100) });
        var lone = core.Handle(new WindowAppeared(new WindowId(3), Area) { Traits = traits, Bounds = new Rectangle(0, 0, 200, 100) });

        Assert.Equal(new Rectangle(200, 200, 200, 100), Assert.Single(dialog.State.Clients, client => client.Window == Dialog).Bounds);
        Assert.Equal(new Rectangle(860, 490, 200, 100), Assert.Single(lone.State.Clients, client => client.Window == new WindowId(3)).Bounds);
    }

    private static byte AlphaOf(Outcome outcome, WindowId window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == window);
        return Assert.Single(outcome.Decorations, decoration => decoration.Client == client.Id).Alpha;
    }
}
