using System.Text.Json;
using Winland;

namespace Winland.Tests;

public class VrrTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);
    private const string Game = @"C:\Games\Game.exe";
    private const string Other = @"C:\Apps\Other.exe";

    [Fact]
    public void ARuleTurnsVariableRefreshOffForThatProgramsPath()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = no_vrr, class:^(game)$\n"));

        var opened = core.Handle(Window(1, Game, "game"));

        var request = opened.Vrr!;
        Assert.Equal([Game], request.Suppressed);
        Assert.Null(request.Global);
        Assert.Equal("true", core.Execute("getprop address:0x1 no_vrr").Text);
    }

    [Fact]
    public void TheSameProgramIsListedOnceAndPathsStaySorted()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = no_vrr, class:^(game)$\n"));
        core.Handle(Window(1, @"C:\Games\z.exe", "game"));
        core.Handle(Window(2, @"c:\games\Z.exe", "game"));
        var opened = core.Handle(Window(3, @"C:\Games\a.exe", "game"));

        Assert.Equal([@"C:\Games\a.exe", @"C:\Games\z.exe"], opened.Vrr!.Suppressed);
    }

    [Fact]
    public void ClosingOneWindowKeepsTheProgramUntilTheLastOneCloses()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = no_vrr, class:^(game)$\n"));
        core.Handle(Window(1, Game, "game"));
        core.Handle(Window(2, Game, "game"));

        var one = core.Handle(new WindowClosed(new WindowId(1)));
        Assert.Equal([Game], one.Vrr!.Suppressed);

        var none = core.Handle(new WindowClosed(new WindowId(2)));
        Assert.Empty(none.Vrr!.Suppressed);
    }

    [Fact]
    public void SetPropTurnsItOffAndAnExplicitFalseWinsOverTheRule()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = no_vrr, class:^(game)$\n"));
        core.Handle(Window(1, Game, "other"));
        Assert.Empty(core.Handle(new Dispatch("workspace", "1")).Vrr!.Suppressed);

        var on = core.Execute("dispatch setprop address:0x1 no_vrr 1");
        Assert.Equal([Game], on.Effect.Vrr!.Suppressed);

        var off = core.Execute("dispatch setprop address:0x1 no_vrr 0");
        Assert.Empty(off.Effect.Vrr!.Suppressed);

        var restored = core.Execute("dispatch setprop address:0x1 no_vrr unset");
        Assert.Empty(restored.Effect.Vrr!.Suppressed);
    }

    [Fact]
    public void UnsetFallsBackToTheRule()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = no_vrr, class:^(game)$\n"));
        core.Handle(Window(1, Game, "game"));
        core.Execute("dispatch setprop address:0x1 no_vrr 0");

        var cleared = core.Execute("dispatch setprop address:0x1 no_vrr unset");

        Assert.Equal([Game], cleared.Effect.Vrr!.Suppressed);
    }

    [Fact]
    public void AWindowWithNoPathIsLeftUnchanged()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = no_vrr, class:^(game)$\n"));

        var opened = core.Handle(Window(1, "", "game"));

        Assert.Empty(opened.Vrr!.Suppressed);
        Assert.Equal("true", core.Execute("getprop address:0x1 no_vrr").Text);
    }

    [Fact]
    public void ASwallowedWindowStillSuppressesItsProgram()
    {
        var core = new Core(ConfigParser.Parse("""
            misc:enable_swallow = true
            misc:swallow_regex = ^(kitty)$
            windowrulev2 = no_vrr, class:^(kitty)$
            """));
        core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            ProcessId = 100,
            Executable = Game,
            Traits = WindowTraits.Normal with { Class = "kitty" },
        });

        var swallowed = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            ProcessId = 202,
            AncestorProcessIds = [150, 100, 4],
            Executable = Other,
            Traits = WindowTraits.Normal with { Class = "mpv" },
        });

        Assert.Equal([Game], swallowed.Vrr!.Suppressed);
        Assert.Contains(new WindowId(1), swallowed.State.Clients.Select(client => client.Window));
    }

    [Fact]
    public void FullscreenRuleFollowsTheWindowThroughACommand()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = no_vrr, fullscreen:1, class:^(game)$\n"));
        var opened = core.Handle(Window(1, Game, "game"));
        Assert.Empty(opened.Vrr!.Suppressed);

        var full = core.Handle(new Dispatch("fullscreen", "0"));
        Assert.Equal([Game], full.Vrr!.Suppressed);

        var left = core.Execute("dispatch fullscreen 0");
        Assert.Empty(left.Effect.Vrr!.Suppressed);
    }

    [Fact]
    public void GlobalOffAndOnFollowTheOptionAndAnAbsentOptionDoesNothing()
    {
        var absent = new Core();
        Assert.Null(absent.Handle(Window(1, Game, "game")).Vrr!.Global);

        var off = new Core(ConfigParser.Parse("misc:vrr = off\n"));
        Assert.False(off.Handle(Window(1, Game)).Vrr!.Global);
        Assert.Equal("{\"option\":\"misc:vrr\",\"int\":0,\"set\":true}", off.Execute("getoption misc:vrr").Text);

        var on = new Core(ConfigParser.Parse("misc:vrr = 1\n"));
        var outcome = on.Handle(Window(1, Game, "game"));
        Assert.True(outcome.Vrr!.Global);
        Assert.Empty(outcome.Vrr.Suppressed);
        Assert.Equal("{\"option\":\"misc:vrr\",\"int\":1,\"set\":true}", on.Execute("getoption misc:vrr").Text);
    }

    [Fact]
    public void DroppingTheOptionStopsAskingForTheAccountWideFlag()
    {
        var core = new Core(ConfigParser.Parse("misc:vrr = on\n"));
        Assert.True(core.Handle(Window(1, Game)).Vrr!.Global);

        var dropped = core.Handle(new Configure(ConfigParser.Parse("general:border_size = 2\n")));

        Assert.Null(dropped.Vrr!.Global);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("fullscreen")]
    [InlineData("fullscreen_game")]
    public void FullscreenModesWarnAndDoNotAskForAFlag(string value)
    {
        var document = ConfigParser.Parse($"misc:vrr = {value}\n");

        Assert.Equal(new ConfigWarning(1, "Fullscreen variable refresh rate is not honored."), Assert.Single(document.Warnings));
        var core = new Core(document);
        Assert.Null(core.Handle(Window(1, Game)).Vrr!.Global);
        Assert.Equal("error: Fullscreen variable refresh rate is not honored.", core.Execute("keyword misc:vrr 3").Text);
    }

    [Fact]
    public void AnInvalidValueWarnsAndIsNotApplied()
    {
        var document = ConfigParser.Parse("misc:vrr = true\n");

        Assert.Equal(new ConfigWarning(1, "Invalid variable refresh rate."), Assert.Single(document.Warnings));
        Assert.Equal(new Setting("misc:vrr", "true"), Assert.Single(document.Settings));
        var core = new Core(document);
        Assert.Null(core.Handle(Window(1, Game)).Vrr!.Global);
        Assert.Equal("{\"option\":\"misc:vrr\",\"int\":0,\"set\":true}", core.Execute("getoption misc:vrr").Text);
        Assert.Equal("error: Invalid variable refresh rate.", core.Execute("keyword misc:vrr maybe").Text);
    }

    [Fact]
    public void ALiveOnIsApplied()
    {
        var core = new Core();

        Assert.Equal("ok", core.Execute("keyword misc:vrr on").Text);

        Assert.True(core.Execute("dispatch workspace 1").Effect.Vrr!.Global);
        Assert.Equal("{\"option\":\"misc:vrr\",\"int\":1,\"set\":true}", core.Execute("getoption misc:vrr").Text);
    }

    [Fact]
    public void DescriptionsNameTheOption()
    {
        var core = new Core();

        using var document = JsonDocument.Parse(core.Execute("descriptions").Text);
        var option = document.RootElement.EnumerateArray().Single(item => item.GetProperty("name").GetString() == "misc:vrr");

        Assert.Equal("controls the VRR (Adaptive Sync) of your monitors", option.GetProperty("description").GetString());
        Assert.Equal(0, option.GetProperty("default").GetInt32());
    }

    [Fact]
    public void SavedPreferencesRoundTripAndAnOldSnapshotHasNone()
    {
        var snapshot = new RecoverySnapshot([])
        {
            Vrr = new VrrOriginals
            {
                Global = true,
                Executables =
                [
                    new VrrExecutableOriginal(Game, "GpuPreference=2;"),
                    new VrrExecutableOriginal(Other, null),
                ],
            },
        };

        var json = snapshot.ToJson();
        Assert.DoesNotContain("\"globalValue\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"value\":null", json, StringComparison.Ordinal);
        Assert.True(RecoverySnapshot.TryParse(json, out var parsed));
        Assert.True(parsed.Vrr!.Global);
        Assert.Null(parsed.Vrr.GlobalValue);
        Assert.Equal(snapshot.Vrr.Executables, parsed.Vrr.Executables);

        Assert.True(RecoverySnapshot.TryParse("{\"clients\":[]}", out var older));
        Assert.Null(older.Vrr);
    }

    private static WindowAppeared Window(long window, string executable, string? klass = null) =>
        new(new WindowId(window), Area)
        {
            Executable = executable,
            Traits = klass is null ? WindowTraits.Normal : WindowTraits.Normal with { Class = klass },
        };
}
