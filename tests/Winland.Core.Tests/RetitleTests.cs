using System.Text.Json;
using Winland;

namespace Winland.Tests;

/// <summary>A window that changes its title is followed, as the pinned release follows a toplevel's title.</summary>
public class RetitleTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void ANewTitleIsKeptAndToldAsAWindowTitleEvent()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(0x2a), Area) { Traits = WindowTraits.Normal with { Class = "kitty", Title = "old" } });
        core.Handle(new WindowAppeared(new WindowId(0x2b), Area) { Traits = WindowTraits.Normal with { Class = "foot", Title = "other" } });

        var outcome = core.Handle(new WindowTitleChanged(new WindowId(0x2a), "new"));

        Assert.Equal("new", Assert.Single(outcome.State.Clients, client => client.Window == new WindowId(0x2a)).Title);
        Assert.Equal(["windowtitle>>2a", "windowtitlev2>>2a,new"], CommandProtocol.FormatStream(outcome.Events, outcome.State));
    }

    [Fact]
    public void TheFocusedWindowsNewTitleIsToldAsTheActiveWindowToo()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(0x2a), Area) { Traits = WindowTraits.Normal with { Class = "kitty", Title = "old" } });

        var outcome = core.Handle(new WindowTitleChanged(new WindowId(0x2a), "new"));

        Assert.Equal(
            ["windowtitle>>2a", "windowtitlev2>>2a,new", "activewindow>>kitty,new", "activewindowv2>>2a"],
            CommandProtocol.FormatStream(outcome.Events, outcome.State));
    }

    [Fact]
    public void TheSameTitleTellsNothing()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Title = "same" } });

        Assert.Empty(core.Handle(new WindowTitleChanged(new WindowId(1), "same")).Events);
    }

    [Fact]
    public void ARuleOnTheTitleAppliesOnceTheTitleMatches()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = opacity 0.5, title:.*Video.*\n"));
        var opened = core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Title = "Browser" } });
        Assert.Equal(255, Assert.Single(opened.Decorations).Alpha);

        var retitled = core.Handle(new WindowTitleChanged(new WindowId(1), "A Video - Browser"));

        Assert.Equal(127, Assert.Single(retitled.Decorations).Alpha, tolerance: 1);
        Assert.Equal("0.5", core.Execute("getprop active opacity").Text);

        var restored = core.Handle(new WindowTitleChanged(new WindowId(1), "Browser"));

        Assert.Equal(255, Assert.Single(restored.Decorations).Alpha);
    }

    [Fact]
    public void TheInitialTitleStaysWhileTheTitleChanges()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(0x2a), Area) { Traits = WindowTraits.Normal with { Class = "kitty", Title = "old" } });

        var outcome = core.Handle(new WindowTitleChanged(new WindowId(0x2a), "new"));

        var client = Assert.Single(outcome.State.Clients);
        Assert.Equal("new", client.Title);
        Assert.Equal("old", client.InitialTitle);
        using var listed = JsonDocument.Parse(core.Execute("j/clients").Text);
        var json = Assert.Single(listed.RootElement.EnumerateArray());
        Assert.Equal("new", json.GetProperty("title").GetString());
        Assert.Equal("old", json.GetProperty("initialTitle").GetString());
        using var active = JsonDocument.Parse(core.Execute("j/activewindow").Text);
        Assert.Equal("new", active.RootElement.GetProperty("title").GetString());
        Assert.Equal("old", active.RootElement.GetProperty("initialTitle").GetString());
    }

    [Fact]
    public void AnInitialTitleRuleKeepsMatchingAfterTheTitleChanges()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = opacity 0.5, initialTitle:^Browser$\n"));
        var opened = core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Title = "Browser" } });
        Assert.Equal(127, Assert.Single(opened.Decorations).Alpha, tolerance: 1);

        var retitled = core.Handle(new WindowTitleChanged(new WindowId(1), "Notes"));

        Assert.Equal(127, Assert.Single(retitled.Decorations).Alpha, tolerance: 1);
        Assert.Equal("Browser", Assert.Single(retitled.State.Clients).InitialTitle);
    }

    [Fact]
    public void AnEmptyInitialTitleDoesNotTakeTheLaterTitle()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = opacity 0.5, initialTitle:^$\n"));
        var opened = core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Title = "" } });
        Assert.Equal(127, Assert.Single(opened.Decorations).Alpha, tolerance: 1);

        var retitled = core.Handle(new WindowTitleChanged(new WindowId(1), "Notes"));

        Assert.Equal("", Assert.Single(retitled.State.Clients).InitialTitle);
        Assert.Equal(127, Assert.Single(retitled.Decorations).Alpha, tolerance: 1);
    }

    [Fact]
    public void AnInitialTitleRuleDoesNotMatchTheNewTitle()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = opacity 0.5, initial_title:.*Video.*\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Title = "Browser" } });

        var retitled = core.Handle(new WindowTitleChanged(new WindowId(1), "A Video - Browser"));

        Assert.Equal(255, Assert.Single(retitled.Decorations).Alpha);
    }

    [Fact]
    public void ARuleOnTheTitleAndTheInitialTitleNeedsBoth()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = opacity 0.5, title:.*Video.*, initialTitle:^Browser$\n"));
        var opened = core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Title = "Browser" } });
        Assert.Equal(255, Assert.Single(opened.Decorations).Alpha);

        var matched = core.Handle(new WindowTitleChanged(new WindowId(1), "A Video"));
        Assert.Equal(127, Assert.Single(matched.Decorations).Alpha, tolerance: 1);

        var missed = core.Handle(new WindowTitleChanged(new WindowId(1), "Notes"));
        Assert.Equal(255, Assert.Single(missed.Decorations).Alpha);
    }

    [Fact]
    public void AGroupTabsTitleFollowsTheWindow()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Title = "one" } });
        core.Execute("dispatch togglegroup");
        core.Handle(new WindowAppeared(new WindowId(2), Area) { Traits = WindowTraits.Normal with { Title = "two" } });

        var retitled = core.Handle(new WindowTitleChanged(new WindowId(1), "renamed"));

        Assert.Equal(["renamed", "two"], Assert.Single(retitled.GroupBars).Tabs.Select(tab => tab.Title));
        var hidden = Assert.Single(retitled.State.Clients, client => client.Window == new WindowId(1));
        Assert.Equal("renamed", hidden.Title);
        Assert.Equal("one", hidden.InitialTitle);
    }

    [Fact]
    public void ATitleForAWindowThatIsNotAClientTellsNothing()
    {
        var core = new Core();

        Assert.Empty(core.Handle(new WindowTitleChanged(new WindowId(1), "title")).Events);
    }
}
