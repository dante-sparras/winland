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
    public void AFloatingWindowWithAnOwnerOpensOverTheMiddleOfItsOwner()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = float, class:^(float)$\n"));
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
