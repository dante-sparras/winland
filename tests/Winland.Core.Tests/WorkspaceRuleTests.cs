using System.Text.Json;
using Winland;
using Monitor = Winland.Monitor;

namespace Winland.Tests;

public class WorkspaceRuleTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);

    [Fact]
    public void HonoredFieldsAreReadWithoutAWarning()
    {
        var document = ConfigParser.Parse("""
            workspace = name:code, monitor:DP-1, default:true, persistent:false, gapsin:1 2 3 4, gapsout:5, bordersize:6, border:0, rounding:1, decorate:0, defaultName:Code, on-created-empty:notepad, layoutopt:force_split:left
            """);

        Assert.Empty(document.Warnings);
        var rule = Assert.Single(document.WorkspaceRules);
        Assert.Equal("name:code", rule.WorkspaceString);
        Assert.Equal("code", rule.WorkspaceName);
        Assert.Equal("DP-1", rule.Monitor);
        Assert.True(rule.Default);
        Assert.False(rule.Persistent);
        Assert.Equal(new EdgeInsets(1, 2, 3, 4), rule.GapsIn);
        Assert.Equal(EdgeInsets.Uniform(5), rule.GapsOut);
        Assert.Equal(6, rule.BorderSize);
        Assert.False(rule.Border);
        Assert.True(rule.Rounding);
        Assert.False(rule.Decorate);
        Assert.Equal("Code", rule.DefaultName);
        Assert.Equal("[workspace code] notepad", rule.OnCreatedEmpty);
        Assert.Equal("left", Assert.Contains("force_split", rule.LayoutOpts));
    }

    [Fact]
    public void GapFieldsWarnAndOtherFieldsAreSkipped()
    {
        var document = ConfigParser.Parse("""
            workspace = 1, monitor:DP-1, shadow:1, animation:pop, float_gaps:4, layout:master, banana:1, layoutopt:precise_mouse_move:1, layoutopt:smart_resizing:true, layoutopt:force_split:left
            """);

        Assert.Equal(
            [
                new ConfigWarning(1, "A workspace rule's shadow is not honored."),
                new ConfigWarning(1, "Workspace rule animation styles are not honored."),
                new ConfigWarning(1, "Floating window gaps are not honored."),
            ],
            document.Warnings);
        var rule = Assert.Single(document.WorkspaceRules);
        Assert.Equal("DP-1", rule.Monitor);
        Assert.Equal("left", Assert.Contains("force_split", rule.LayoutOpts));
        Assert.Equal("1", Assert.Contains("precise_mouse_move", rule.LayoutOpts));
        Assert.Equal("true", Assert.Contains("smart_resizing", rule.LayoutOpts));
        Assert.Equal(3, rule.LayoutOpts.Count);
    }

    [Fact]
    public void ABrokenFieldDropsTheRule()
    {
        var document = ConfigParser.Parse("""
            workspace = , gapsin:1
            workspace = 1, bordersize:no, monitor:DP-1
            workspace = 2, on-created-empty:
            workspace = 3, layoutopt:force_split
            workspace = 4, gapsin:1 2 3 4 5
            """);

        Assert.Equal(
            [
                new ConfigWarning(1, "Malformed workspace rule."),
                new ConfigWarning(2, "Error parsing workspace rule bordersize: no"),
                new ConfigWarning(3, "Failed parsing a workspace rule"),
                new ConfigWarning(4, "Invalid workspace rule found: layoutopt:force_split"),
                new ConfigWarning(5, "Too many arguments provided for gaps."),
            ],
            document.Warnings);
        var rule = Assert.Single(document.WorkspaceRules);
        Assert.Equal("4", rule.WorkspaceString);
        Assert.Equal(new EdgeInsets(1, 2, 3, 4), rule.GapsIn);
    }

    [Fact]
    public void LaterRulesForTheSameWorkspaceOverride()
    {
        var document = ConfigParser.Parse("""
            workspace = 1, gapsin:5, default:false
            workspace = 1, gapsout:8, default:true, monitor:DP-1
            workspace = 2, persistent:true
            """);

        Assert.Equal(2, document.WorkspaceRules.Count);
        Assert.Equal("DP-1", document.WorkspaceRules[0].Monitor);
        Assert.True(document.WorkspaceRules[0].Default);
        Assert.Equal(EdgeInsets.Uniform(5), document.WorkspaceRules[0].GapsIn);
        Assert.Equal(EdgeInsets.Uniform(8), document.WorkspaceRules[0].GapsOut);
        Assert.Equal("2", document.WorkspaceRules[1].WorkspaceString);
    }

    [Fact]
    public void WorkspaceRulesQueryListsMergedRulesAndOmitsUnsetFields()
    {
        var core = Configured("""
            workspace = name:code, monitor:DP-1, default:true, persistent:false, gapsin:1 2 3 4, gapsout:5, bordersize:50, border:0, rounding:1, decorate:0, defaultName:Code, on-created-empty:notepad
            workspace = 2, persistent:true
            """);

        var rules = Json(core.Execute("workspacerules").Text).EnumerateArray().ToArray();

        Assert.Equal(2, rules.Length);
        Assert.Equal("name:code", rules[0].GetProperty("workspaceString").GetString());
        Assert.True(rules[0].GetProperty("enabled").GetBoolean());
        Assert.Equal("DP-1", rules[0].GetProperty("monitor").GetString());
        Assert.True(rules[0].GetProperty("default").GetBoolean());
        Assert.False(rules[0].GetProperty("persistent").GetBoolean());
        Assert.Equal([1, 2, 3, 4], rules[0].GetProperty("gapsIn").EnumerateArray().Select(item => item.GetInt32()).ToArray());
        Assert.Equal([5, 5, 5, 5], rules[0].GetProperty("gapsOut").EnumerateArray().Select(item => item.GetInt32()).ToArray());
        Assert.Equal(50, rules[0].GetProperty("borderSize").GetInt32());
        Assert.False(rules[0].GetProperty("border").GetBoolean());
        Assert.True(rules[0].GetProperty("rounding").GetBoolean());
        Assert.False(rules[0].GetProperty("decorate").GetBoolean());
        Assert.Equal("Code", rules[0].GetProperty("defaultName").GetString());
        Assert.Equal("[workspace code] notepad", rules[0].GetProperty("onCreatedEmpty").GetString());
        Assert.False(rules[0].TryGetProperty("shadow", out _));
        Assert.False(rules[0].TryGetProperty("layoutopts", out _));
        Assert.Equal("2", rules[1].GetProperty("workspaceString").GetString());
        Assert.True(rules[1].GetProperty("persistent").GetBoolean());
        Assert.False(rules[1].TryGetProperty("monitor", out _));
        Assert.False(rules[1].TryGetProperty("default", out _));
        Assert.False(rules[1].TryGetProperty("gapsIn", out _));
        Assert.Equal("error: unknown request", core.Execute("workspacerules extra").Text);
    }

    [Fact]
    public void InnerAndOuterGapsReplaceTheGeneralValuesOnThatWorkspace()
    {
        var core = Configured(Flat("""
            workspace = 2, gapsin:10, gapsout:20
            """));

        core.Handle(Appeared(1, Wide));
        var home = core.Handle(Appeared(2, Wide));
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(home, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(home, 2));

        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(Appeared(3, Wide));
        var there = core.Handle(Appeared(4, Wide));
        Assert.Equal(new Rectangle(20, 20, 930, 1040), BoundsOf(there, 3));
        Assert.Equal(new Rectangle(970, 20, 930, 1040), BoundsOf(there, 4));
    }

    [Fact]
    public void ARangeAppliesToWorkspacesThatAlreadyExist()
    {
        var core = Configured(Flat("""
            workspace = r[1-2], gapsout:10
            """));

        var first = core.Handle(Appeared(1, Wide));
        Assert.Equal(new Rectangle(10, 10, 1900, 1060), BoundsOf(first, 1));

        core.Handle(new Dispatch("workspace", "2"));
        var second = core.Handle(Appeared(2, Wide));
        Assert.Equal(new Rectangle(10, 10, 1900, 1060), BoundsOf(second, 2));

        core.Handle(new Dispatch("workspace", "3"));
        var third = core.Handle(Appeared(3, Wide));
        Assert.Equal(Wide, BoundsOf(third, 3));
        Assert.DoesNotContain(third.State.Workspaces, workspace => workspace.Id == new WorkspaceId("2") && workspace.Persistent);
    }

    // As the pinned release's source reads it: f[1] has a maximized Client and f[0] a fullscreen
    // one, whatever the client mode. f[2] tests nothing there, so it matches every Workspace.
    [Theory]
    [InlineData("1", "fullscreen", "1", true)]
    [InlineData("1", "fullscreen", "2", false)]
    [InlineData("0", "fullscreen", "2", true)]
    [InlineData("0", "fullscreen", "1", false)]
    [InlineData("0", "fullscreenstate", "2 0", true)]
    [InlineData("0", "fullscreenstate", "2 1", true)]
    [InlineData("1", "fullscreenstate", "1 0", true)]
    [InlineData("2", "fullscreen", "1", true)]
    [InlineData("2", "fullscreenstate", "2 0", true)]
    [InlineData("-1", "fullscreen", "1", false)]
    public void AFullscreenSelectorFollowsTheWorkspaceFullscreenState(string selector, string dispatcher, string mode, bool applies)
    {
        var core = Configured(Flat($"""
            workspace = f[{selector}], defaultName:Covered
            """));
        core.Handle(Appeared(1, Wide));
        core.Handle(Appeared(2, Wide));

        var covered = core.Handle(new Dispatch(dispatcher, mode));

        Assert.Equal(applies ? "Covered" : null, Workspace(covered, "1").DisplayName);
    }

    [Fact]
    public void ANoFullscreenSelectorAppliesOnlyWithoutACoveringClient()
    {
        var core = Configured(Flat("""
            workspace = f[-1], defaultName:Open
            """));
        core.Handle(Appeared(1, Wide));
        var open = core.Handle(Appeared(2, Wide));
        Assert.Equal("Open", Workspace(open, "1").DisplayName);

        var covered = core.Handle(new Dispatch("fullscreen", "1"));
        Assert.Null(Workspace(covered, "1").DisplayName);

        var uncovered = core.Handle(new Dispatch("fullscreen", "1"));
        Assert.Equal("Open", Workspace(uncovered, "1").DisplayName);
    }

    [Fact]
    public void ANoFullscreenSelectorAppliesAgainWhenTheCoveringClientCloses()
    {
        var core = Configured(Flat("""
            workspace = f[-1], defaultName:Open
            """));
        core.Handle(Appeared(1, Wide));
        core.Handle(Appeared(2, Wide));
        core.Handle(new Dispatch("fullscreen", "1"));

        var closed = core.Handle(new WindowClosed(new WindowId(2)));

        Assert.Equal("Open", Workspace(closed, "1").DisplayName);
    }

    [Fact]
    public void AFullscreenSelectorFollowsACoveringClientToAnotherWorkspace()
    {
        var core = Configured(Flat("""
            workspace = f[1], defaultName:Covered
            """));
        core.Handle(Appeared(1, Wide));
        core.Handle(Appeared(2, Wide));
        core.Handle(new Dispatch("fullscreen", "1"));

        var moved = core.Handle(new Dispatch("movetoworkspacesilent", "2"));

        Assert.Null(Workspace(moved, "1").DisplayName);
        Assert.Equal("Covered", Workspace(moved, "2").DisplayName);
    }

    [Fact]
    public void AFullscreenSelectorOfTwoMatchesAWorkspaceWithNothingCovering()
    {
        var core = Configured(Flat("""
            workspace = f[2], defaultName:Any
            """));

        var open = core.Handle(Appeared(1, Wide));

        Assert.Equal("Any", Workspace(open, "1").DisplayName);
    }

    [Fact]
    public void AMaximizedSelectorSquaresTheCornersOfTheMaximizedClient()
    {
        var core = Configured(Flat("""
            decoration:rounding = 8
            workspace = f[1], rounding:0
            """));
        var open = core.Handle(Appeared(1, Wide));
        Assert.Equal(CornerPreference.Round, Corners(open, 1));

        var maximized = core.Handle(new Dispatch("fullscreen", "1"));

        Assert.Equal(CornerPreference.DoNotRound, Corners(maximized, 1));
    }

    [Fact]
    public void BorderSizeBorderAndDecorateChangeOnlyThatWorkspace()
    {
        var core = Configured("""
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 2
            workspace = 1, bordersize:8, border:0
            workspace = 3, decorate:0
            """);

        var hidden = core.Handle(Appeared(1, Wide));
        Assert.Equal(Wide, BoundsOf(hidden, 1));

        core.Handle(new Dispatch("workspace", "2"));
        var bordered = core.Handle(Appeared(2, Wide));
        Assert.Equal(new Rectangle(2, 2, 1916, 1076), BoundsOf(bordered, 2));

        core.Handle(new Dispatch("workspace", "3"));
        var undecorated = core.Handle(Appeared(3, Wide));
        Assert.Equal(Wide, BoundsOf(undecorated, 3));
    }

    [Fact]
    public void BorderSizeIsClampedWhenDrawnAndReportedRaw()
    {
        var core = Configured(Flat("""
            workspace = 1, bordersize:50
            """));

        var outcome = core.Handle(Appeared(1, Wide));
        Assert.Equal(new Rectangle(20, 20, 1880, 1040), BoundsOf(outcome, 1));
        Assert.Equal(50, Json(core.Execute("workspacerules").Text)[0].GetProperty("borderSize").GetInt32());
    }

    [Fact]
    public void ZeroRoundingForcesSquareCornersOnThatWorkspace()
    {
        var core = Configured(Flat("""
            decoration:rounding = 8
            workspace = 1, rounding:0
            """));

        var square = core.Handle(Appeared(1, Wide));
        Assert.Equal(CornerPreference.DoNotRound, Corners(square, 1));

        core.Handle(new Dispatch("workspace", "2"));
        var round = core.Handle(Appeared(2, Wide));
        Assert.Equal(CornerPreference.Round, Corners(round, 2));
    }

    [Fact]
    public void LayoutOptAppliesOnlyToThatWorkspace()
    {
        var core = Configured(Flat("""
            workspace = 1, layoutopt:force_split:left
            """));

        core.Handle(Appeared(1, Wide));
        var forced = core.Handle(Appeared(2, Wide, new Point(1800, 500)));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(forced, 1));
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(forced, 2));

        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(Appeared(3, Wide));
        var followed = core.Handle(Appeared(4, Wide, new Point(1800, 500)));
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(followed, 3));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(followed, 4));
    }

    [Fact]
    public void ABoundWorkspaceAppearsOnThatMonitor()
    {
        var core = Configured(Flat("""
            workspace = 1, monitor:\\.\DISPLAY2
            """));
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        var connected = core.Handle(new MonitorsChanged([left, right]));

        Assert.Equal(new MonitorId(0), connected.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("2"), connected.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(1), Workspace(connected, "1").Monitor);
        Assert.Equal(new MonitorId(0), Workspace(connected, "2").Monitor);

        var focused = core.Handle(new Dispatch("focusworkspaceoncurrentmonitor", "1"));
        Assert.Equal(new MonitorId(1), focused.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("1"), focused.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(1), Workspace(focused, "1").Monitor);
        Assert.Equal(new MonitorId(0), Workspace(focused, "2").Monitor);

        var opened = core.Handle(new WindowAppeared(new WindowId(1), right.WorkArea));
        Assert.Equal(new WorkspaceId("1"), Client(opened, 1).Workspace);
        Assert.Equal(right.WorkArea, BoundsOf(opened, 1));
    }

    [Fact]
    public void ADescriptionSelectorBindsTheWorkspaceToThatMonitor()
    {
        var core = Configured(Flat("""
            workspace = name:code, monitor:desc:Samsung
            """));
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));

        var focused = core.Handle(new Dispatch("workspace", "name:code"));

        Assert.Equal(new WorkspaceId("code"), focused.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(1), focused.State.ActiveMonitor);
        Assert.Equal(new MonitorId(1), Workspace(focused, "code").Monitor);
    }

    [Fact]
    public void DefaultSelectsTheWorkspaceWhenAMonitorHasNone()
    {
        var core = Configured(Flat("""
            workspace = 4, monitor:\\.\DISPLAY2, default:true
            """));
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        var connected = core.Handle(new MonitorsChanged([left, right]));

        var monitors = Json(core.Execute("monitors").Text).EnumerateArray().ToArray();
        Assert.Equal("1", monitors[0].GetProperty("activeWorkspace").GetProperty("name").GetString());
        Assert.Equal("4", monitors[1].GetProperty("activeWorkspace").GetProperty("name").GetString());
        Assert.Equal(new WorkspaceId("1"), connected.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(1), Workspace(connected, "4").Monitor);
    }

    [Fact]
    public void APersistentWorkspaceStaysWhenItIsEmpty()
    {
        var core = Configured(Flat("""
            workspace = 9, persistent:true
            """));

        var listed = Json(core.Execute("workspaces").Text)
            .EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetInt32());
        Assert.False(listed[1].GetProperty("ispersistent").GetBoolean());
        Assert.True(listed[9].GetProperty("ispersistent").GetBoolean());
        Assert.Equal(0, listed[9].GetProperty("windows").GetInt32());

        core.Handle(new Dispatch("workspace", "8"));
        var home = core.Handle(new Dispatch("workspace", "1"));

        Assert.Contains(home.State.Workspaces, workspace => workspace.Id == new WorkspaceId("9") && workspace.Persistent);
        Assert.DoesNotContain(home.State.Workspaces, workspace => workspace.Id == new WorkspaceId("8"));
    }

    [Fact]
    public void ARangeDoesNotCreatePersistentWorkspaces()
    {
        var core = Configured(Flat("""
            workspace = r[1-2], persistent:true
            """));

        Assert.DoesNotContain(Json(core.Execute("workspaces").Text).EnumerateArray(), item => item.GetProperty("id").GetInt32() == 2);
        core.Handle(new Dispatch("workspace", "2"));
        var home = core.Handle(new Dispatch("workspace", "1"));
        Assert.Contains(home.State.Workspaces, workspace => workspace.Id == new WorkspaceId("2") && workspace.Persistent);

        core.Handle(new Dispatch("workspace", "3"));
        var back = core.Handle(new Dispatch("workspace", "1"));
        Assert.DoesNotContain(back.State.Workspaces, workspace => workspace.Id == new WorkspaceId("3"));
    }

    [Fact]
    public void DefaultNameIsTheReportedNameAndStillAddressesTheSameWorkspace()
    {
        var core = Configured(Flat("""
            workspace = 1, defaultName:code
            """));

        var listed = Assert.Single(Json(core.Execute("workspaces").Text).EnumerateArray());
        Assert.Equal(1, listed.GetProperty("id").GetInt32());
        Assert.Equal("code", listed.GetProperty("name").GetString());

        var stayed = core.Handle(new Dispatch("workspace", "code"));
        Assert.Equal(new WorkspaceId("1"), stayed.State.ActiveWorkspace);
        Assert.Single(stayed.State.Workspaces);
        Assert.Equal("code", stayed.State.Workspaces[0].DisplayName);
    }

    [Fact]
    public void OnCreatedEmptyRunsOnceWhenTheWorkspaceIsCreated()
    {
        var core = new Core();
        var created = core.Handle(new Configure(ConfigParser.Parse(Flat("""
            workspace = 3, persistent:true, on-created-empty:notepad
            """))));

        Assert.NotNull(created.Launch);
        Assert.Equal("notepad", created.Launch.CommandLine);
        Assert.Equal(
            [new WindowRule(WindowRuleEffect.Workspace, "3", null, null, null)],
            created.Launch.Rules);
        Assert.Contains(created.State.Workspaces, workspace => workspace.Id == new WorkspaceId("3") && workspace.Persistent);

        var away = core.Handle(new Dispatch("workspace", "1"));
        var back = core.Handle(new Dispatch("workspace", "3"));
        Assert.Null(away.Launch);
        Assert.Null(back.Launch);
    }

    [Fact]
    public void OnCreatedEmptyRunsForEveryWorkspaceCreatedTogether()
    {
        var core = new Core();
        var created = core.Handle(new Configure(ConfigParser.Parse(Flat("""
            workspace = 3, persistent:true, on-created-empty:three
            workspace = 4, persistent:true, on-created-empty:four
            """))));

        Assert.Equal(["three", "four"], created.Launches.Select(launch => launch.CommandLine));
        Assert.Equal(["calc"], core.Execute("dispatch exec calc").Effect.Launches.Select(launch => launch.CommandLine));
    }

    [Fact]
    public void ASpecialSelectorTestsWhetherTheWorkspaceIsSpecial()
    {
        const string Rules = """
            workspace = s[true], gapsin:7
            workspace = s[false], gapsin:3
            """;

        Assert.Equal(7, GapsIn(Rules, new WorkspaceId("special:scratch")));
        Assert.Equal(3, GapsIn(Rules, WorkspaceId.FromNumber(1)));
    }

    [Fact]
    public void ASpecialWorkspaceIsNotNamed()
    {
        Assert.Equal(4, GapsIn("workspace = n[false], gapsin:4", new WorkspaceId("special:scratch")));
        Assert.Equal(4, GapsIn("workspace = n[false], gapsin:4", WorkspaceId.FromNumber(1)));
        Assert.Equal(4, GapsIn("workspace = n[true], gapsin:4", new WorkspaceId("notes")));
        Assert.Null(GapsIn("workspace = n[true], gapsin:4", new WorkspaceId("special:scratch")));
    }

    [Theory]
    [InlineData("special:scratch", "special:scratch", null, true)]
    [InlineData("special:scratch", "scratch", null, false)]
    [InlineData("special", "special:special", null, true)]
    [InlineData("special:", "special:special", null, false)]
    [InlineData("coding", "coding", null, true)]
    [InlineData("coding", "special:coding", null, false)]
    [InlineData("coding", "1", "coding", false)]
    [InlineData("name:coding", "coding", null, true)]
    [InlineData("name:1", "1", null, false)]
    [InlineData("name:code", "1", "code", false)]
    [InlineData("name:scratch", "special:scratch", null, false)]
    public void ABareSelectorMatchesTheAddressableName(string selector, string workspace, string? displayName, bool applies)
    {
        var gaps = GapsIn($"workspace = {selector}, gapsin:9", new WorkspaceId(workspace), displayName);
        Assert.Equal(applies ? 9 : null, gaps);
    }

    // The rules are read again when a tiled Client closes, so a third tile closes before the check.
    [Theory]
    [InlineData("w[g1]", true)]
    [InlineData("w[g0]", false)]
    [InlineData("w[tg1]", true)]
    [InlineData("w[fg1]", false)]
    [InlineData("w[1]", true)]
    [InlineData("w[2]", false)]
    public void AWindowCountSelectorCountsGroups(string selector, bool applies)
    {
        var core = Configured(Flat($"""
            workspace = {selector}, defaultName:Hit
            """));
        core.Handle(Appeared(1, Wide));
        core.Execute("dispatch togglegroup");
        core.Handle(Appeared(2, Wide));
        core.Handle(Appeared(3, Wide));
        core.Execute("dispatch moveoutofgroup");

        var closed = core.Handle(new WindowClosed(new WindowId(3)));

        Assert.Equal([new WindowId(1), new WindowId(2)], Assert.Single(closed.State.Groups));
        Assert.Equal(applies ? "Hit" : null, Workspace(closed, "1").DisplayName);
    }

    [Theory]
    [InlineData("w[p1]", true)]
    [InlineData("w[p0]", false)]
    [InlineData("w[fp1]", true)]
    [InlineData("w[tp1]", false)]
    [InlineData("w[2]", true)]
    public void AWindowCountSelectorCountsPinnedClients(string selector, bool applies)
    {
        var core = Configured(Flat($"""
            windowrulev2 = float, class:^(stick)$
            workspace = {selector}, defaultName:Hit
            """));
        core.Handle(new WindowAppeared(new WindowId(1), Wide) { Traits = WindowTraits.Normal with { Class = "stick" } });
        core.Execute("dispatch pin");
        core.Handle(Appeared(2, Wide));
        core.Handle(Appeared(3, Wide));

        var closed = core.Handle(new WindowClosed(new WindowId(3)));

        Assert.Equal(applies ? "Hit" : null, Workspace(closed, "1").DisplayName);
    }

    [Theory]
    [InlineData("w[v1]", true)]
    [InlineData("w[v2]", false)]
    [InlineData("w[2]", true)]
    public void AWindowCountSelectorCountsVisibleClients(string selector, bool applies)
    {
        var core = Configured(Flat($"""
            workspace = {selector}, defaultName:Hit
            """));
        core.Handle(Appeared(1, Wide));
        core.Handle(Appeared(2, Wide));

        var covered = core.Handle(new Dispatch("fullscreen", "0"));

        Assert.Equal(applies ? "Hit" : null, Workspace(covered, "1").DisplayName);
    }

    private static int? GapsIn(string rules, WorkspaceId id, string? displayName = null)
    {
        var parsed = ConfigParser.Parse(rules).WorkspaceRules;
        return WorkspaceRules.For(parsed, id, displayName, null)?.GapsIn?.Top;
    }

    [Fact]
    public void ASmartGapsRuleHoldsFromTheFirstWindowOn()
    {
        var core = Configured("""
            general:gaps_in = 5
            general:gaps_out = 20
            general:border_size = 0
            workspace = w[tv1], gapsout:0, gapsin:0
            """);

        var first = core.Handle(Appeared(1, Wide));
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), BoundsOf(first, 1));

        var second = core.Handle(Appeared(2, Wide));
        Assert.Equal(new Rectangle(20, 20, 935, 1040), BoundsOf(second, 1));

        var closed = core.Handle(new WindowClosed(new WindowId(2)));
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), BoundsOf(closed, 1));
    }

    private static string Flat(string extra) =>
        "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + extra;

    private static Core Configured(string text)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(text)));
        return core;
    }

    private static WindowAppeared Appeared(long window, Rectangle area, Point? pointer = null) =>
        new WindowAppeared(new WindowId(window), area) { Pointer = pointer };

    private static Monitor Display(
        string name,
        string description,
        int x,
        int y,
        int width,
        int height,
        double scale) =>
        new(name, description, x, y, width, height, scale, true, new Rectangle(x, y, width, height));

    private static Workspace Workspace(Outcome outcome, string name) =>
        Assert.Single(outcome.State.Workspaces, workspace => workspace.Id == new WorkspaceId(name));

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Client(outcome, window);
        return Assert.Single(outcome.Placements, placement => placement.Client == client.Id).Bounds;
    }

    private static CornerPreference Corners(Outcome outcome, long window) =>
        Assert.Single(outcome.Decorations, decoration => decoration.Client == new ClientId(window)).Corners;

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
