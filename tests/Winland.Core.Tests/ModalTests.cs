using Winland;

namespace Winland.Tests;

public class ModalTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);
    private static readonly WindowId Owner = new(1);
    private static readonly WindowId Dialog = new(2);

    [Fact]
    public void AnOwnerShadowedByItsModalTakesNoFocusAndIsDimmed()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(Owner, Area));
        core.Handle(new WindowEnabledChanged(Owner, Enabled: false));
        core.Handle(new WindowAppeared(Dialog, Area) { Owner = Owner });

        var clicked = core.Handle(new WindowFocused(Owner, FocusCause.User));
        Assert.Equal(Dialog, clicked.Focus);
        Assert.Equal(128, AlphaOf(clicked, Owner));

        core.Handle(new Dispatch("focuswindow", "address:0x1"));
        Assert.Equal(Dialog, core.Handle(new Dispatch("killactive")).Close);

        var enabled = core.Handle(new WindowEnabledChanged(Owner, Enabled: true));
        Assert.Equal(255, AlphaOf(enabled, Owner));
        core.Handle(new WindowFocused(Owner, FocusCause.User));
        Assert.Equal(Owner, core.Handle(new Dispatch("killactive")).Close);
    }

    [Fact]
    public void ADisabledWindowWithNoOpenModalIsNotShadowed()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(Owner, Area) { Disabled = true });
        core.Handle(new WindowAppeared(new WindowId(3), Area));

        core.Handle(new WindowFocused(Owner, FocusCause.User));
        Assert.Equal(Owner, core.Handle(new Dispatch("killactive")).Close);
    }

    [Fact]
    public void BlockingCanBeTurnedOffAndTheDimStays()
    {
        var core = new Core(ConfigParser.Parse("general:modal_parent_blocking = false\n"));
        core.Handle(new WindowAppeared(Owner, Area) { Disabled = true });
        core.Handle(new WindowAppeared(Dialog, Area) { Owner = Owner });

        var clicked = core.Handle(new WindowFocused(Owner, FocusCause.User));
        Assert.Equal(Owner, core.Handle(new Dispatch("killactive")).Close);
        Assert.True(AlphaOf(clicked, Owner) < 255);
        Assert.Equal("{\"option\":\"general:modal_parent_blocking\",\"bool\":false,\"set\":true}", core.Execute("getoption general:modal_parent_blocking").Text);
    }

    [Fact]
    public void TheModalRuleMatchesTheDialogOfADisabledOwner()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = float, modal:1\n"));
        core.Handle(new WindowAppeared(Owner, Area) { Disabled = true });
        var opened = core.Handle(new WindowAppeared(Dialog, Area) { Owner = Owner });
        var free = core.Handle(new WindowAppeared(new WindowId(3), Area) { Owner = new WindowId(9) });

        Assert.True(Assert.Single(opened.State.Clients, client => client.Window == Dialog).Floating);
        Assert.False(Assert.Single(free.State.Clients, client => client.Window == new WindowId(3)).Floating);
    }

    private static byte AlphaOf(Outcome outcome, WindowId window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == window);
        return Assert.Single(outcome.Decorations, decoration => decoration.Client == client.Id).Alpha;
    }
}
