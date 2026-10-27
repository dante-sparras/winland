namespace Winland.Application.Tests;

public class WindowManagerTests
{
    [Fact]
    public void AppearingWindowIsPlacedOnItsWorkArea()
    {
        var core = new WindowManager();

        var outcome = core.Handle(new WindowAppeared(new WindowId(42), new Rectangle(0, 0, 1920, 1080)));

        var client = Assert.Single(outcome.State.Clients);
        var placement = Assert.Single(outcome.Placements);
        Assert.Equal(new WindowId(42), client.Window);
        Assert.Equal(client.Id, placement.Client);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), placement.Bounds);
    }

    [Fact]
    public void EveryOutcomeIsNumberedInTheOrderTheCoreDecidedIt()
    {
        var core = new WindowManager();
        var other = new WindowManager();

        var first = core.Handle(new WindowAppeared(new WindowId(42), new Rectangle(0, 0, 1920, 1080))).Order!.Value;
        var second = core.Handle(new Dispatch("workspace", "2")).Order!.Value;
        var third = core.Execute("dispatch workspace 1").Effect.Order!.Value;
        var elsewhere = other.Handle(new WindowAppeared(new WindowId(42), new Rectangle(0, 0, 1920, 1080))).Order!.Value;

        Assert.Equal(first.WindowManager, second.WindowManager);
        Assert.Equal(first.WindowManager, third.WindowManager);
        Assert.True(first.Sequence < second.Sequence && second.Sequence < third.Sequence);
        Assert.NotEqual(first.WindowManager, elsewhere.WindowManager);
    }

    [Fact]
    public void SecondWindowSplitsTheWorkAreaSideBySide()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);

        core.Handle(new WindowAppeared(new WindowId(1), area));
        var outcome = core.Handle(new WindowAppeared(new WindowId(2), area));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void ThirdWindowSplitsTheActiveHalfTopAndBottom()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);

        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new WindowAppeared(new WindowId(2), area));
        var outcome = core.Handle(new WindowAppeared(new WindowId(3), area));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 540), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(960, 540, 960, 540), BoundsOf(outcome, 3));
    }

    [Fact]
    public void TallerWorkAreaSplitsTopAndBottom()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1080, 1920);

        core.Handle(new WindowAppeared(new WindowId(1), area));
        var outcome = core.Handle(new WindowAppeared(new WindowId(2), area));

        Assert.Equal(new Rectangle(0, 0, 1080, 960), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 960, 1080, 960), BoundsOf(outcome, 2));
    }

    [Fact]
    public void PointerInTheFirstHalfPlacesTheNewWindowThere()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);

        core.Handle(new WindowAppeared(new WindowId(1), area));
        var outcome = core.Handle(new WindowAppeared(new WindowId(2), area) { Pointer = new Point(10, 540) });

        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void PointerInTheTopHalfOfATallSplitPlacesTheNewWindowThere()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1080, 1920);

        core.Handle(new WindowAppeared(new WindowId(1), area));
        var outcome = core.Handle(new WindowAppeared(new WindowId(2), area) { Pointer = new Point(540, 10) });

        Assert.Equal(new Rectangle(0, 960, 1080, 960), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 0, 1080, 960), BoundsOf(outcome, 2));
    }

    [Fact]
    public void ClosingAWindowGivesItsSpaceToItsNeighbor()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);
        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new WindowAppeared(new WindowId(2), area));

        var outcome = core.Handle(new WindowClosed(new WindowId(2)));

        Assert.Equal(new Rectangle(0, 0, 1920, 1080), BoundsOf(outcome, 1));
        Assert.DoesNotContain(outcome.State.Clients, client => client.Window == new WindowId(2));
    }

    [Fact]
    public void ClosingAWindowReflowsTheSplitToTheNewShape()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);
        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new WindowAppeared(new WindowId(2), area));
        core.Handle(new WindowAppeared(new WindowId(3), area));

        var outcome = core.Handle(new WindowClosed(new WindowId(1)));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(outcome, 3));
    }

    [Fact]
    public void MinimizingAWindowGivesItsSpaceBackAndRestoringReturnsIt()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);
        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new WindowAppeared(new WindowId(2), area));
        core.Handle(new WindowAppeared(new WindowId(3), area));

        var minimized = core.Handle(new WindowMinimized(new WindowId(3)));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(minimized, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(minimized, 2));
        var hidden = Assert.Single(minimized.State.Clients, client => client.Window == new WindowId(3));
        Assert.True(hidden.Minimized);
        Assert.DoesNotContain(minimized.Placements, placement => placement.Client == hidden.Id);

        var restored = core.Handle(new WindowRestored(new WindowId(3)));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(restored, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 540), BoundsOf(restored, 2));
        Assert.Equal(new Rectangle(960, 540, 960, 540), BoundsOf(restored, 3));
        Assert.False(Assert.Single(restored.State.Clients, client => client.Window == new WindowId(3)).Minimized);
    }

    [Fact]
    public void RestoringTheOnlyWindowReturnsItToTheWorkArea()
    {
        var core = new WindowManager();
        var area = new Rectangle(10, 20, 800, 600);
        core.Handle(new WindowAppeared(new WindowId(1), area));

        var minimized = core.Handle(new WindowMinimized(new WindowId(1)));

        Assert.Empty(minimized.Placements);
        Assert.True(Assert.Single(minimized.State.Clients).Minimized);

        var restored = core.Handle(new WindowRestored(new WindowId(1)));

        Assert.Equal(area, BoundsOf(restored, 1));
        Assert.False(Assert.Single(restored.State.Clients).Minimized);
    }

    [Fact]
    public void ClosingAMinimizedWindowRemovesIt()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);
        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new WindowAppeared(new WindowId(2), area));
        core.Handle(new WindowMinimized(new WindowId(2)));

        var outcome = core.Handle(new WindowClosed(new WindowId(2)));

        var client = Assert.Single(outcome.State.Clients);
        Assert.Equal(new WindowId(1), client.Window);
        Assert.Empty(outcome.Placements);
    }

    [Theory]
    [InlineData("tool")]
    [InlineData("tooltip")]
    [InlineData("taskbar")]
    [InlineData("cloaked")]
    [InlineData("invisible")]
    [InlineData("untitled")]
    [InlineData("child")]
    [InlineData("overlay")]
    public void WindowsOutsideTheAdoptionFilterAreLeftAlone(string reason)
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);
        core.Handle(new WindowAppeared(new WindowId(1), area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(99), area) { Traits = Rejected(reason) });

        var client = Assert.Single(outcome.State.Clients);
        Assert.Equal(new WindowId(1), client.Window);
        Assert.Empty(outcome.Placements);
    }

    [Fact]
    public void ElevatedWindowIsTiled()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);

        var outcome = core.Handle(new WindowAppeared(new WindowId(7), area)
        {
            Traits = WindowTraits.Normal with { Elevated = true },
        });

        Assert.Equal(new Rectangle(0, 0, 1920, 1080), BoundsOf(outcome, 7));
        Assert.False(Assert.Single(outcome.State.Clients).Floating);
    }

    [Fact]
    public void DialogFloatsCenteredAndLeavesTheTilesAlone()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);
        var bounds = new Rectangle(400, 300, 320, 200);
        core.Handle(new WindowAppeared(new WindowId(1), area));

        var floated = core.Handle(new WindowAppeared(new WindowId(2), area)
        {
            Traits = WindowTraits.Normal with { Dialog = true },
            Bounds = bounds,
        });

        var dialog = Assert.Single(floated.State.Clients, client => client.Window == new WindowId(2));
        Assert.True(dialog.Floating);
        Assert.Equal(NewFloat.Centered(area, bounds), Assert.Single(floated.Placements, placement => placement.Client == dialog.Id).Bounds);
        Assert.Contains(floated.State.Clients, client => client.Window == new WindowId(1) && !client.Floating);

        var split = core.Handle(new WindowAppeared(new WindowId(3), area));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(split, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(split, 3));
        Assert.True(Assert.Single(split.State.Clients, client => client.Window == new WindowId(2)).Floating);
    }

    [Fact]
    public void WindowIsAdoptedOnceItGainsATitle()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);

        var ignored = core.Handle(new WindowAppeared(new WindowId(5), area)
        {
            Traits = WindowTraits.Normal with { Titled = false },
        });
        Assert.Empty(ignored.State.Clients);

        var outcome = core.Handle(new WindowAppeared(new WindowId(5), area));

        Assert.Equal(new Rectangle(0, 0, 1920, 1080), BoundsOf(outcome, 5));
    }

    [Fact]
    public void ClassRuleFloatsTheWindowAndLeavesTheTilesAlone()
    {
        var core = new WindowManager(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\nwindowrule = float, class:^(kitty)$\n"));
        var area = new Rectangle(0, 0, 1920, 1080);
        var bounds = new Rectangle(40, 50, 320, 200);
        core.Handle(new WindowAppeared(new WindowId(1), area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), area)
        {
            Traits = WindowTraits.Normal with { Class = "kitty" },
            Bounds = bounds,
        });

        var floated = Assert.Single(outcome.State.Clients, client => client.Window == new WindowId(2));
        Assert.True(floated.Floating);
        Assert.Equal(NewFloat.Centered(area, bounds), Assert.Single(outcome.Placements).Bounds);
        Assert.Contains(outcome.State.Clients, client => client.Window == new WindowId(1) && !client.Floating);

        var split = core.Handle(new WindowAppeared(new WindowId(3), area));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(split, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(split, 3));
        Assert.True(Assert.Single(split.State.Clients, client => client.Window == new WindowId(2)).Floating);
    }

    [Theory]
    [InlineData("initialClass:^(kitty)$")]
    [InlineData("initial_class:^(kitty)$")]
    public void InitialClassNamesMatchTheClass(string match)
    {
        var core = new WindowManager(ConfigParser.Parse($"windowrule = float, {match}\n"));
        var bounds = new Rectangle(8, 9, 10, 11);

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 400, 300))
        {
            Traits = WindowTraits.Normal with { Class = "kitty" },
            Bounds = bounds,
        });

        Assert.True(Assert.Single(outcome.State.Clients).Floating);
        Assert.Equal(NewFloat.Centered(new Rectangle(0, 0, 400, 300), bounds), Assert.Single(outcome.Placements).Bounds);
    }

    [Theory]
    [InlineData("title:^Editor$")]
    [InlineData("initialTitle:^Editor$")]
    [InlineData("initial_title:^Editor$")]
    public void TitleRuleFloatsAMatchingWindow(string match)
    {
        var core = new WindowManager(ConfigParser.Parse($"windowrule = float, {match}\n"));
        var bounds = new Rectangle(1, 2, 3, 4);
        var area = new Rectangle(0, 0, 800, 600);

        var missed = core.Handle(new WindowAppeared(new WindowId(1), area)
        {
            Traits = WindowTraits.Normal with { Title = "Release Notes" },
            Bounds = bounds,
        });
        Assert.False(Assert.Single(missed.State.Clients).Floating);

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), area)
        {
            Traits = WindowTraits.Normal with { Title = "Editor" },
            Bounds = bounds,
        });

        var floated = Assert.Single(outcome.State.Clients, client => client.Window == new WindowId(2));
        Assert.True(floated.Floating);
        Assert.Equal(NewFloat.Centered(area, bounds), Assert.Single(outcome.Placements, placement => placement.Client == floated.Id).Bounds);
    }

    [Fact]
    public void AumidRuleFloatsTheMatchingWindow()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = float, aumid:^(Microsoft\\.WindowsCalculator_8wekyb3d8bbwe!App)$\n"));
        var bounds = new Rectangle(12, 16, 240, 180);
        var area = new Rectangle(0, 0, 1920, 1080);

        var missed = core.Handle(new WindowAppeared(new WindowId(1), area)
        {
            Traits = WindowTraits.Normal with { Aumid = "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App" },
            Bounds = bounds,
        });
        Assert.False(Assert.Single(missed.State.Clients).Floating);

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), area)
        {
            Traits = WindowTraits.Normal with
            {
                Class = "Windows.UI.Core.CoreWindow",
                Exe = "CalculatorApp.exe",
                Aumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
            },
            Bounds = bounds,
        });

        var floated = Assert.Single(outcome.State.Clients, client => client.Window == new WindowId(2));
        Assert.True(floated.Floating);
        Assert.Equal(NewFloat.Centered(area, bounds), Assert.Single(outcome.Placements, placement => placement.Client == floated.Id).Bounds);
    }

    [Fact]
    public void AumidRuleRequiresTheOtherFieldsToo()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = workspace 4, class:^(Windows\\.UI\\.Core\\.CoreWindow)$, title:^(Calculator)$, exe:^(CalculatorApp\\.exe)$, aumid:^(Microsoft\\.WindowsCalculator_8wekyb3d8bbwe!App)$\n"));
        var area = new Rectangle(0, 0, 1000, 800);
        var hosted = WindowTraits.Normal with
        {
            Class = "Windows.UI.Core.CoreWindow",
            Title = "Calculator",
            Exe = "CalculatorApp.exe",
            Aumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
        };

        var missed = core.Handle(new WindowAppeared(new WindowId(1), area)
        {
            Traits = hosted with { Aumid = "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App" },
        });
        Assert.Equal(WorkspaceId.Initial, Assert.Single(missed.State.Clients).Workspace);

        var hit = core.Handle(new WindowAppeared(new WindowId(2), area)
        {
            Traits = hosted,
        });
        var client = Assert.Single(hit.State.Clients, candidate => candidate.Window == new WindowId(2));
        Assert.Equal(new WorkspaceId("4"), client.Workspace);
        Assert.False(client.Floating);
    }

    [Fact]
    public void CloakedFrameStaysUnmanagedWithAPackagedIdentity()
    {
        var core = new WindowManager();
        var outcome = core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600))
        {
            Traits = WindowTraits.Normal with
            {
                Cloaked = true,
                Class = "Windows.UI.Core.CoreWindow",
                Title = "Calculator",
                Exe = "CalculatorApp.exe",
                Aumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
            },
        });

        Assert.Empty(outcome.State.Clients);
        Assert.Empty(outcome.Placements);
    }

    [Fact]
    public void ExeRuleIgnoresTheWindow()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = ignore, exe:^(notepad\\.exe)$\n"));
        var area = new Rectangle(0, 0, 1920, 1080);
        core.Handle(new WindowAppeared(new WindowId(1), area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), area)
        {
            Traits = WindowTraits.Normal with { Exe = "notepad.exe" },
        });

        Assert.Single(outcome.State.Clients, client => client.Window == new WindowId(1));
        Assert.DoesNotContain(outcome.State.Clients, client => client.Window == new WindowId(2));
        Assert.Empty(outcome.Placements);
    }

    [Fact]
    public void RuleMatchesOnlyWhenClassTitleAndExeAllMatch()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = workspace 4, class:^(code)$, title:^(Editor)$, exe:^(code\\.exe)$\n"));
        var area = new Rectangle(0, 0, 1000, 800);

        var missed = core.Handle(new WindowAppeared(new WindowId(1), area)
        {
            Traits = WindowTraits.Normal with { Class = "code", Title = "Editor", Exe = "other.exe" },
        });
        Assert.Equal(WorkspaceId.Initial, Assert.Single(missed.State.Clients).Workspace);

        var hit = core.Handle(new WindowAppeared(new WindowId(2), area)
        {
            Traits = WindowTraits.Normal with { Class = "code", Title = "Editor", Exe = "code.exe" },
        });
        var client = Assert.Single(hit.State.Clients, candidate => candidate.Window == new WindowId(2));
        Assert.Equal(new WorkspaceId("4"), client.Workspace);
        Assert.False(client.Floating);
    }

    [Fact]
    public void VersionOneRuleMatchesClassOnly()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = float, class:^(kitty)$\n"));
        var area = new Rectangle(0, 0, 200, 100);
        var bounds = new Rectangle(5, 5, 20, 20);

        var byTitle = core.Handle(new WindowAppeared(new WindowId(1), area)
        {
            Traits = WindowTraits.Normal with { Class = "other", Title = "kitty" },
            Bounds = bounds,
        });
        Assert.False(Assert.Single(byTitle.State.Clients).Floating);

        var byClass = core.Handle(new WindowAppeared(new WindowId(2), area)
        {
            Traits = WindowTraits.Normal with { Class = "kitty" },
            Bounds = bounds,
        });
        var floated = Assert.Single(byClass.State.Clients, client => client.Window == new WindowId(2));
        Assert.True(floated.Floating);
        Assert.Equal(NewFloat.Centered(area, bounds), Assert.Single(byClass.Placements, placement => placement.Client == floated.Id).Bounds);
    }

    [Fact]
    public void TileRuleAdoptsAWindowTheFilterWouldLeaveAlone()
    {
        var core = new WindowManager(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\nwindowrule = tile, class:^(toolwindow)$\n"));
        var area = new Rectangle(0, 0, 1920, 1080);

        var outcome = core.Handle(new WindowAppeared(new WindowId(7), area)
        {
            Traits = WindowTraits.Normal with { Tool = true, Class = "toolwindow" },
        });

        Assert.False(Assert.Single(outcome.State.Clients).Floating);
        Assert.Equal(WorkspaceId.Initial, Assert.Single(outcome.State.Clients).Workspace);
        Assert.Equal(area, BoundsOf(outcome, 7));
    }

    [Fact]
    public void AWorkspaceRuleForAProgramLeavesItsPopupsAlone()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = workspace 2, exe:chrome.exe\n"));
        var area = new Rectangle(0, 0, 1920, 1080);

        var menu = core.Handle(new WindowAppeared(new WindowId(7), area)
        {
            Traits = WindowTraits.Normal with { Tool = true, Titled = false, Title = "", Exe = "chrome.exe" },
        });
        var popup = core.Handle(new WindowAppeared(new WindowId(8), area)
        {
            Traits = WindowTraits.Normal with { Overlay = true, Exe = "chrome.exe" },
        });

        Assert.Empty(menu.State.Clients);
        Assert.Empty(popup.State.Clients);
    }

    [Fact]
    public void TileRuleTilesADialog()
    {
        var core = new WindowManager(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\nwindowrule = tile, class:^(dialog)$\n"));
        var area = new Rectangle(0, 0, 1920, 1080);

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), area)
        {
            Traits = WindowTraits.Normal with { Dialog = true, Class = "dialog" },
            Bounds = new Rectangle(1, 1, 10, 10),
        });

        Assert.False(Assert.Single(outcome.State.Clients).Floating);
        Assert.Equal(area, BoundsOf(outcome, 1));
    }

    [Fact]
    public void FloatAndWorkspaceBothApply()
    {
        var text = "windowrule = workspace 4, class:^(kitty)$\nwindowrule = float, class:^(kitty)$\n";
        var core = new WindowManager(ConfigParser.Parse(text));
        var bounds = new Rectangle(10, 20, 30, 40);

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600))
        {
            Traits = WindowTraits.Normal with { Class = "kitty" },
            Bounds = bounds,
        });

        var client = Assert.Single(outcome.State.Clients);
        Assert.True(client.Floating);
        Assert.Equal(new WorkspaceId("4"), client.Workspace);
        Assert.Equal(new WorkspaceId("4"), outcome.State.ActiveWorkspace);
        Assert.Equal(NewFloat.Centered(new Rectangle(0, 0, 800, 600), bounds), Assert.Single(outcome.Placements).Bounds);
    }

    [Fact]
    public void WorkspaceRuleOnADialogKeepsItFloating()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = workspace 3, class:^(dialog)$\n"));
        var bounds = new Rectangle(4, 5, 6, 7);

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 900, 700))
        {
            Traits = WindowTraits.Normal with { Dialog = true, Class = "dialog" },
            Bounds = bounds,
        });

        var client = Assert.Single(outcome.State.Clients);
        Assert.True(client.Floating);
        Assert.Equal(new WorkspaceId("3"), client.Workspace);
        Assert.Equal(NewFloat.Centered(new Rectangle(0, 0, 900, 700), bounds), Assert.Single(outcome.Placements).Bounds);
    }

    [Fact]
    public void SilentWorkspaceRuleDoesNotSwitch()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = workspace 2 silent, class:^(kitty)$\n"));
        var area = new Rectangle(0, 0, 640, 480);
        core.Handle(new WindowAppeared(new WindowId(1), area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), area)
        {
            Traits = WindowTraits.Normal with { Class = "kitty" },
        });

        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(2));
        Assert.Equal(new WorkspaceId("2"), client.Workspace);
        Assert.False(client.Floating);
        Assert.Equal(WorkspaceId.Initial, outcome.State.ActiveWorkspace);
        Assert.Contains(outcome.State.Clients, candidate => candidate.Window == new WindowId(1) && candidate.Workspace == WorkspaceId.Initial);
        Assert.DoesNotContain(outcome.State.Hidden, hidden => hidden.Window == new WindowId(1));
        Assert.DoesNotContain(outcome.Placements, placement => placement.Client == client.Id);
        Assert.Contains(outcome.State.Hidden, hidden => hidden.Window == new WindowId(2));
    }

    [Fact]
    public void LaterRuleWinsAndDifferentEffectsCombine()
    {
        var text =
            "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\nwindowrule = float, class:^(kitty)$\n" +
            "windowrule = workspace 9, class:^(kitty)$\n" +
            "windowrule = tile, class:^(kitty)$\n";
        var core = new WindowManager(ConfigParser.Parse(text));
        var area = new Rectangle(0, 0, 1920, 1080);
        core.Handle(new WindowAppeared(new WindowId(1), area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), area)
        {
            Traits = WindowTraits.Normal with { Class = "kitty" },
        });

        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(2));
        Assert.False(client.Floating);
        Assert.Equal(new WorkspaceId("9"), client.Workspace);
        Assert.Equal(new WorkspaceId("9"), outcome.State.ActiveWorkspace);
        Assert.Equal(area, BoundsOf(outcome, 2));
        Assert.Contains(outcome.State.Hidden, hidden => hidden.Window == new WindowId(1));
    }

    [Fact]
    public void LaterIgnoreLeavesTheWindowUnmanaged()
    {
        var text = "windowrule = workspace 2, class:^(kitty)$\nwindowrule = ignore, class:^(kitty)$\n";
        var core = new WindowManager(ConfigParser.Parse(text));

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 100, 100))
        {
            Traits = WindowTraits.Normal with { Class = "kitty" },
        });

        Assert.Empty(outcome.State.Clients);
        Assert.Empty(outcome.Placements);
    }

    [Fact]
    public void XwaylandMatchIsNotApplied()
    {
        var document = ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\nwindowrule = float, class:^(kitty)$, xwayland:1\n");
        var core = new WindowManager(document);

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 100, 100))
        {
            Traits = WindowTraits.Normal with { Class = "kitty" },
            Bounds = new Rectangle(1, 2, 3, 4),
        });

        Assert.False(Assert.Single(outcome.State.Clients).Floating);
        Assert.Equal(new Rectangle(0, 0, 100, 100), BoundsOf(outcome, 1));
    }

    [Fact]
    public void PatternMustMatchTheWholeField()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = float, title:Notes\n"));

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 300, 200))
        {
            Traits = WindowTraits.Normal with { Title = "Release Notes" },
            Bounds = new Rectangle(4, 5, 6, 7),
        });

        Assert.False(Assert.Single(outcome.State.Clients).Floating);
    }

    [Theory]
    [InlineData("windowrule = float, title:Notes|Release Notes\n")]
    [InlineData("windowrule = float, title:Release.*\n")]
    [InlineData("windowrule = float, class:Release.*\n")]
    public void PatternThatMatchesTheWholeFieldApplies(string config)
    {
        var core = new WindowManager(ConfigParser.Parse(config));
        var traits = config.Contains("class:", StringComparison.Ordinal)
            ? WindowTraits.Normal with { Class = "Release Notes" }
            : WindowTraits.Normal with { Title = "Release Notes" };

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 300, 200))
        {
            Traits = traits,
            Bounds = new Rectangle(4, 5, 6, 7),
        });

        Assert.True(Assert.Single(outcome.State.Clients).Floating);
    }

    [Theory]
    [InlineData("kitty", false)]
    [InlineData("firefox", true)]
    [InlineData("kitty2", true)]
    public void NegativePatternAppliesWhenThePatternDoesNotMatch(string windowClass, bool floats)
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = float, class:negative:kitty\n"));

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 300, 200))
        {
            Traits = WindowTraits.Normal with { Class = windowClass },
            Bounds = new Rectangle(4, 5, 6, 7),
        });

        Assert.Equal(floats, Assert.Single(outcome.State.Clients).Floating);
    }

    [Fact]
    public void NegativePrefixWithAnInvalidPatternWarns()
    {
        var document = ConfigParser.Parse("windowrule = float, class:negative:(kitty\n");

        Assert.Empty(document.WindowRules);
        Assert.Contains(document.Warnings, warning => warning.Message == "Invalid window rule pattern.");
    }

    [Fact]
    public void AppearingAgainDoesNotSplitAgain()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);
        core.Handle(new WindowAppeared(new WindowId(1), area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), area));

        Assert.Single(outcome.State.Clients);
        Assert.Empty(outcome.Placements);
    }

    [Fact]
    public void AClientShownAgainLeavesTheFocusWhereItWas()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);
        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new WindowAppeared(new WindowId(2), area));
        core.Handle(new WindowFocused(new WindowId(1)));

        var echo = Echo(core, 2, area);

        Assert.All(echo, outcome => Assert.Null(outcome.Focus));
        Assert.Equal("0x1", Json(core.Execute("j/activewindow").Text).GetProperty("address").GetString());
    }

    [Fact]
    public void AClientShownAgainChangesNoPlacement()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);
        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new WindowAppeared(new WindowId(2), area));

        var echo = Echo(core, 1, area);

        Assert.All(echo, outcome =>
        {
            Assert.Empty(outcome.Placements);
            Assert.Empty(outcome.Show);
            Assert.Empty(outcome.Hide);
        });
        Assert.Equal(2, echo[^1].State.Clients.Count);
    }

    [Fact]
    public void AClientThatShowsItselfOnAHiddenWorkspaceIsHiddenAgain()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);
        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new Dispatch("workspace", "2"));

        var echo = Echo(core, 1, area);

        Assert.Equal([new WindowId(1)], echo[^1].Hide);
    }

    // What the adapter sends when the system shows a window Winland hid, which it asks the core for as three steps.
    private static Outcome[] Echo(WindowManager core, long window, Rectangle area)
    {
        var id = new WindowId(window);
        return
        [
            core.Handle(new WindowRestored(id)),
            core.Handle(new WindowClassChanged(id, WindowTraits.Normal.Class)),
            core.Handle(new WindowAppeared(id, area)),
        ];
    }

    private static WindowTraits Rejected(string reason) => reason switch
    {
        "tool" => WindowTraits.Normal with { Tool = true },
        "tooltip" => WindowTraits.Normal with { Tooltip = true },
        "taskbar" => WindowTraits.Normal with { Taskbar = true },
        "cloaked" => WindowTraits.Normal with { Cloaked = true },
        "invisible" => WindowTraits.Normal with { Visible = false },
        "untitled" => WindowTraits.Normal with { Titled = false },
        "child" => WindowTraits.Normal with { TopLevel = false },
        "overlay" => WindowTraits.Normal with { Overlay = true },
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown rejection."),
    };
}
