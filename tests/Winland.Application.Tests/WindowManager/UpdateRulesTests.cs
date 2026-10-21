
namespace Winland.Application.Tests;

/// <summary>window.update_rules runs when a window's rules are read again, as Winland emits it.</summary>
public class UpdateRulesTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void ANewWindowHasItsRulesRead()
    {
        var core = new WindowManager();

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Class = "kitty" } });

        Assert.Equal(new WindowId(1), Assert.Single(outcome.Events.OfType<ClientRulesUpdated>()).Window);
        Assert.Contains(Lines(outcome), line => line.StartsWith("openwindow>>", StringComparison.Ordinal));
        Assert.DoesNotContain(Lines(outcome), line => line.Contains("updaterules", StringComparison.Ordinal));
    }

    [Fact]
    public void AWindowThatMapsOnAHiddenWorkspaceIsNotRead()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrulev2 = workspace 2 silent, class:^(kitty)$\n"));

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Class = "kitty" } });

        Assert.Empty(outcome.Events.OfType<ClientRulesUpdated>());
        Assert.Contains(Lines(outcome), line => line.StartsWith("openwindow>>", StringComparison.Ordinal));
    }

    [Fact]
    public void FloatingAWindowReadsItsRulesAgain()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new Dispatch("togglefloating", ""));

        Assert.Equal(new WindowId(1), Assert.Single(outcome.Events.OfType<ClientRulesUpdated>()).Window);
        Assert.Equal(["changefloatingmode>>1,1"], Lines(outcome));
    }

    [Fact]
    public void MovingFocusReadsBothWindows()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var outcome = core.Handle(new Dispatch("focuswindow", "address:0x1"));

        Assert.Equal(
            [new WindowId(1), new WindowId(2)],
            outcome.Events.OfType<ClientRulesUpdated>().Select(updated => updated.Window).OrderBy(window => window.Value));
        Assert.Equal(["activewindow>>,", "activewindowv2>>1"], Lines(outcome));
    }

    [Fact]
    public void ADialogBecomesModalWhenItsOwnerIsDisabled()
    {
        var core = new WindowManager();
        var owner = new WindowId(1);
        var dialog = new WindowId(2);
        core.Handle(new WindowAppeared(owner, Area));
        core.Handle(new WindowAppeared(dialog, Area) { Owner = owner });

        var disabled = core.Handle(new WindowEnabledChanged(owner, Enabled: false));
        Assert.Equal(dialog, Assert.Single(disabled.Events.OfType<ClientRulesUpdated>()).Window);
        Assert.Empty(Lines(disabled));

        var enabled = core.Handle(new WindowEnabledChanged(owner, Enabled: true));
        Assert.Equal(dialog, Assert.Single(enabled.Events.OfType<ClientRulesUpdated>()).Window);
    }

    [Fact]
    public void DisablingAWindowThatOwnsNothingTellsNothing()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        Assert.Empty(core.Handle(new WindowEnabledChanged(new WindowId(1), Enabled: false)).Events);
    }

    [Fact]
    public void StartingAGroupReadsThatWindow()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var made = core.Handle(new Dispatch("togglegroup", ""));

        Assert.Equal(new WindowId(1), Assert.Single(made.Events.OfType<ClientRulesUpdated>()).Window);
        Assert.Equal(["togglegroup>>1,1"], Lines(made));
    }

    [Fact]
    public void LeavingAGroupReadsEveryMember()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Title = "one" } });
        core.Handle(new Dispatch("togglegroup", ""));
        core.Handle(new WindowAppeared(new WindowId(2), Area) { Traits = WindowTraits.Normal with { Title = "two" } });

        var left = core.Handle(new Dispatch("moveoutofgroup", ""));

        Assert.Equal(
            [new WindowId(1), new WindowId(2)],
            left.Events.OfType<ClientRulesUpdated>().Select(updated => updated.Window).OrderBy(window => window.Value));
        Assert.Equal(["moveoutofgroup>>2"], Lines(left));
    }

    private static IReadOnlyList<string> Lines(Outcome outcome) =>
        CommandProtocol.FormatStream(outcome.Events, outcome.State);
}
