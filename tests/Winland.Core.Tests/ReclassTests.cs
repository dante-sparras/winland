using System.Text.Json;
using Winland;

namespace Winland.Tests;

/// <summary>A window that changes its class is followed, as the pinned release follows a toplevel's class.</summary>
public class ReclassTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void ANewClassIsKeptAndTellsNoClassEvent()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(0x2a), Area) { Traits = WindowTraits.Normal with { Class = "kitty", Title = "old" } });
        core.Handle(new WindowAppeared(new WindowId(0x2b), Area) { Traits = WindowTraits.Normal with { Class = "foot", Title = "other" } });

        var outcome = core.Handle(new WindowClassChanged(new WindowId(0x2a), "new"));

        Assert.Equal("new", Assert.Single(outcome.State.Clients, client => client.Window == new WindowId(0x2a)).Class);
        var reclassed = Assert.IsType<ClientReclassed>(Assert.Single(outcome.Events));
        Assert.Equal(new WindowId(0x2a), reclassed.Window);
        Assert.Equal("new", reclassed.Class);
        Assert.False(reclassed.Active);
        Assert.Empty(CommandProtocol.FormatStream(outcome.Events, outcome.State));
    }

    [Fact]
    public void TheFocusedWindowsNewClassIsToldAsTheActiveWindow()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(0x2a), Area) { Traits = WindowTraits.Normal with { Class = "kitty", Title = "old" } });

        var outcome = core.Handle(new WindowClassChanged(new WindowId(0x2a), "new"));

        Assert.Equal(
            ["activewindow>>new,old", "activewindowv2>>2a"],
            CommandProtocol.FormatStream(outcome.Events, outcome.State));
    }

    [Fact]
    public void TheSameClassTellsNothing()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Class = "same" } });

        Assert.Empty(core.Handle(new WindowClassChanged(new WindowId(1), "same")).Events);
    }

    [Fact]
    public void ARuleOnTheClassAppliesOnceTheClassMatches()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = opacity 0.5, class:.*Video.*\n"));
        var opened = core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Class = "Browser" } });
        Assert.Equal(255, Assert.Single(opened.Decorations).Alpha);

        var reclassed = core.Handle(new WindowClassChanged(new WindowId(1), "A Video - Browser"));

        Assert.Equal(127, Assert.Single(reclassed.Decorations).Alpha, tolerance: 1);
        Assert.Equal("0.5", core.Execute("getprop active opacity").Text);

        var restored = core.Handle(new WindowClassChanged(new WindowId(1), "Browser"));

        Assert.Equal(255, Assert.Single(restored.Decorations).Alpha);
    }

    [Fact]
    public void TheInitialClassStaysWhileTheClassChanges()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(0x2a), Area) { Traits = WindowTraits.Normal with { Class = "kitty", Title = "old" } });

        var outcome = core.Handle(new WindowClassChanged(new WindowId(0x2a), "new"));

        var client = Assert.Single(outcome.State.Clients);
        Assert.Equal("new", client.Class);
        Assert.Equal("kitty", client.InitialClass);
        using var listed = JsonDocument.Parse(core.Execute("j/clients").Text);
        var json = Assert.Single(listed.RootElement.EnumerateArray());
        Assert.Equal("new", json.GetProperty("class").GetString());
        Assert.Equal("kitty", json.GetProperty("initialClass").GetString());
        using var active = JsonDocument.Parse(core.Execute("j/activewindow").Text);
        Assert.Equal("new", active.RootElement.GetProperty("class").GetString());
        Assert.Equal("kitty", active.RootElement.GetProperty("initialClass").GetString());
    }

    [Fact]
    public void AnInitialClassRuleKeepsMatchingAfterTheClassChanges()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = opacity 0.5, initialClass:^Browser$\n"));
        var opened = core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Class = "Browser" } });
        Assert.Equal(127, Assert.Single(opened.Decorations).Alpha, tolerance: 1);

        var reclassed = core.Handle(new WindowClassChanged(new WindowId(1), "Notes"));

        Assert.Equal(127, Assert.Single(reclassed.Decorations).Alpha, tolerance: 1);
        Assert.Equal("Browser", Assert.Single(reclassed.State.Clients).InitialClass);
    }

    [Fact]
    public void AnEmptyInitialClassDoesNotTakeTheLaterClass()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = opacity 0.5, initialClass:^$\n"));
        var opened = core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Class = "" } });
        Assert.Equal(127, Assert.Single(opened.Decorations).Alpha, tolerance: 1);

        var reclassed = core.Handle(new WindowClassChanged(new WindowId(1), "Notes"));

        Assert.Equal("", Assert.Single(reclassed.State.Clients).InitialClass);
        Assert.Equal(127, Assert.Single(reclassed.Decorations).Alpha, tolerance: 1);
    }

    [Fact]
    public void AnInitialClassRuleDoesNotMatchTheNewClass()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = opacity 0.5, initial_class:.*Video.*\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Class = "Browser" } });

        var reclassed = core.Handle(new WindowClassChanged(new WindowId(1), "A Video - Browser"));

        Assert.Equal(255, Assert.Single(reclassed.Decorations).Alpha);
    }

    [Fact]
    public void ARuleOnTheClassAndTheInitialClassNeedsBoth()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = opacity 0.5, class:.*Video.*, initialClass:^Browser$\n"));
        var opened = core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Class = "Browser" } });
        Assert.Equal(255, Assert.Single(opened.Decorations).Alpha);

        var matched = core.Handle(new WindowClassChanged(new WindowId(1), "A Video"));
        Assert.Equal(127, Assert.Single(matched.Decorations).Alpha, tolerance: 1);

        var missed = core.Handle(new WindowClassChanged(new WindowId(1), "Notes"));
        Assert.Equal(255, Assert.Single(missed.Decorations).Alpha);
    }

    [Fact]
    public void AGroupedWindowsClassFollowsTheWindow()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Class = "one" } });
        core.Execute("dispatch togglegroup");
        core.Handle(new WindowAppeared(new WindowId(2), Area) { Traits = WindowTraits.Normal with { Class = "two" } });

        var reclassed = core.Handle(new WindowClassChanged(new WindowId(1), "renamed"));

        var hidden = Assert.Single(reclassed.State.Clients, client => client.Window == new WindowId(1));
        Assert.Equal("renamed", hidden.Class);
        Assert.Equal("one", hidden.InitialClass);
    }

    [Fact]
    public void AClassForAWindowThatIsNotAClientTellsNothing()
    {
        var core = new Core();

        Assert.Empty(core.Handle(new WindowClassChanged(new WindowId(1), "class")).Events);
    }
}
