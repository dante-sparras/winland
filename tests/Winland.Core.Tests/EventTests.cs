using Winland;

namespace Winland.Tests;

public class EventTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void OpeningAClientEmitsOpenThenFocus()
    {
        var core = new Core();

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Class = "kitty", Title = "terminal" },
        });

        Assert.Equal(
            ["openwindow>>1,1,kitty,terminal", "activewindow>>kitty,terminal", "activewindowv2>>1"],
            Lines(outcome));
    }

    [Fact]
    public void OpeningKeepsACommaInTheTitle()
    {
        var core = new Core();

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Class = "app", Title = "a,b" },
        });

        Assert.Equal("openwindow>>1,1,app,a,b", Lines(outcome)[0]);
    }

    [Fact]
    public void OpeningFlattensANewlineInTheTitle()
    {
        var core = new Core();

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Title = "a\nb" },
        });

        Assert.Equal("openwindow>>1,1,,a b", Lines(outcome)[0]);
    }

    [Fact]
    public void RejectedWindowEmitsNothing()
    {
        var core = new Core();

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Tool = true },
        });

        Assert.Empty(outcome.Events);
    }

    [Fact]
    public void SilentWorkspaceOpenDoesNotChangeFocus()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = workspace 2 silent, class:^(kitty)$\n"));

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Class = "kitty", Title = "term" },
        });

        Assert.Equal(
            ["createworkspace>>2", "createworkspacev2>>2,2", "openwindow>>1,2,kitty,term"],
            Lines(outcome));
    }

    [Fact]
    public void WorkspaceRuleOpenSwitchesThenFocuses()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = workspace 2, class:^(kitty)$\n"));

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Class = "kitty", Title = "term" },
        });

        Assert.Equal(
            [
                "createworkspace>>2",
                "createworkspacev2>>2,2",
                "destroyworkspace>>1",
                "destroyworkspacev2>>1,1",
                "openwindow>>1,2,kitty,term",
                "workspace>>2",
                "workspacev2>>2,2",
                "activewindow>>kitty,term",
                "activewindowv2>>1",
            ],
            Lines(outcome));
    }

    [Fact]
    public void ClosingTheFocusedClientEmitsCloseThenTheNextFocus()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var outcome = core.Handle(new WindowClosed(new WindowId(2)));

        Assert.Equal(["closewindow>>2", "activewindow>>,", "activewindowv2>>1"], Lines(outcome));
    }

    [Fact]
    public void ClosingAnUnfocusedClientEmitsOnlyClose()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var outcome = core.Handle(new WindowClosed(new WindowId(1)));

        Assert.Equal(["closewindow>>1"], Lines(outcome));
    }

    [Fact]
    public void LeavingAnEmptyNamedWorkspaceReportsTheIdItHad()
    {
        var core = new Core();
        core.Execute("dispatch workspace name:alpha");

        var back = core.Execute("dispatch workspace 1");

        Assert.Equal(
            [
                "createworkspace>>1",
                "createworkspacev2>>1,1",
                "destroyworkspace>>alpha",
                "destroyworkspacev2>>-1,alpha",
                "workspace>>1",
                "workspacev2>>1,1",
            ],
            Lines(back));
    }

    [Fact]
    public void ClosingTheLastClientOnAHiddenWorkspaceDestroysIt()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(7), Area));
        core.Execute("dispatch workspace 2");

        var closed = core.Handle(new WindowClosed(new WindowId(7)));

        Assert.Equal(["destroyworkspace>>1", "destroyworkspacev2>>1,1", "closewindow>>7"], Lines(closed));
    }

    [Fact]
    public void ADestroyedWorkspaceIsReportedByItsDefaultName()
    {
        var core = new Core(ConfigParser.Parse("workspace = 1, defaultName:code"));
        core.Handle(new WindowAppeared(new WindowId(7), Area));
        core.Execute("dispatch workspace 2");

        var closed = core.Handle(new WindowClosed(new WindowId(7)));

        Assert.Equal(["destroyworkspace>>code", "destroyworkspacev2>>1,code", "closewindow>>7"], Lines(closed));
    }

    [Fact]
    public void ClosingTheLastClientClearsFocus()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new WindowClosed(new WindowId(1)));

        Assert.Equal(["closewindow>>1", "activewindow>>,", "activewindowv2>>"], Lines(outcome));
    }

    [Fact]
    public void KillActiveWaitsForTheWindowToClose()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var killed = core.Execute("dispatch killactive");
        var closed = core.Handle(new WindowClosed(new WindowId(1)));

        Assert.Empty(killed.Effect.Events);
        Assert.Equal(["closewindow>>1", "activewindow>>,", "activewindowv2>>"], Lines(closed));
    }

    [Fact]
    public void FocusDispatcherEmitsTheNewAddress()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var outcome = core.Handle(new Dispatch("movefocus", "l"));

        Assert.Equal(new WindowId(1), outcome.Focus);
        Assert.Equal(["activewindow>>,", "activewindowv2>>1"], Lines(outcome));
    }

    [Fact]
    public void ReportedFocusEmitsActiveWindow()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var outcome = core.Handle(new WindowFocused(new WindowId(1)));

        Assert.Equal(["activewindow>>,", "activewindowv2>>1"], Lines(outcome));
    }

    [Fact]
    public void FocusingTheSameClientEmitsNothing()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new WindowFocused(new WindowId(1)));

        Assert.Empty(outcome.Events);
    }

    [Fact]
    public void WorkspaceSwitchEmitsTheDeskThenClearsFocus()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Execute("dispatch workspace 2");

        Assert.Equal(
            [
                "createworkspace>>2",
                "createworkspacev2>>2,2",
                "workspace>>2",
                "workspacev2>>2,2",
                "activewindow>>,",
                "activewindowv2>>",
            ],
            Lines(outcome));
    }

    [Fact]
    public void SwitchingBackRestoresFocusAfterTheDeskEvent()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Execute("dispatch workspace 2");

        var outcome = core.Execute("dispatch workspace 1");

        Assert.Equal(
            [
                "destroyworkspace>>2",
                "destroyworkspacev2>>2,2",
                "workspace>>1",
                "workspacev2>>1,1",
                "activewindow>>,",
                "activewindowv2>>1",
            ],
            Lines(outcome));
    }

    [Fact]
    public void StayingOnTheSameWorkspaceEmitsNothing()
    {
        var core = new Core();

        var outcome = core.Execute("dispatch workspace 1");

        Assert.Empty(outcome.Effect.Events);
    }

    [Fact]
    public void SilentMoveEmitsTheDestinationThenTheReplacementFocus()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var outcome = core.Execute("dispatch movetoworkspacesilent 3");

        Assert.Equal(
            [
                "createworkspace>>3",
                "createworkspacev2>>3,3",
                "movewindow>>2,3",
                "movewindowv2>>2,3,3",
                "activewindow>>,",
                "activewindowv2>>1",
            ],
            Lines(outcome));
    }

    [Fact]
    public void FollowingAMoveEmitsTheMoveThenTheDesk()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var outcome = core.Execute("dispatch movetoworkspace 4");

        Assert.Equal(
            [
                "createworkspace>>4",
                "createworkspacev2>>4,4",
                "movewindow>>2,4",
                "movewindowv2>>2,4,4",
                "workspace>>4",
                "workspacev2>>4,4",
            ],
            Lines(outcome));
    }

    [Fact]
    public void NamedWorkspaceMoveUsesTheSameNegativeIdAsTheReadReply()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Execute("dispatch movetoworkspacesilent name:alpha");

        Assert.Equal(
            [
                "createworkspace>>alpha",
                "createworkspacev2>>-1,alpha",
                "movewindow>>1,alpha",
                "movewindowv2>>1,-1,alpha",
                "activewindow>>,",
                "activewindowv2>>",
            ],
            Lines(outcome));
    }

    [Fact]
    public void MinimizeAndRestoreEmitTheFlag()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var minimized = core.Handle(new WindowMinimized(new WindowId(1)));
        var restored = core.Handle(new WindowRestored(new WindowId(1)));

        Assert.Equal(["minimized>>1,1"], Lines(minimized));
        Assert.Equal(["minimized>>1,0"], Lines(restored));
    }

    [Fact]
    public void TogglingFloatEmitsTheMode()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(0xFF), Area));

        var lifted = core.Execute("dispatch togglefloating address:0x1");
        var dropped = core.Execute("dispatch settiled address:0x1");

        Assert.Equal(["changefloatingmode>>1,1"], Lines(lifted));
        Assert.Equal(["changefloatingmode>>1,0"], Lines(dropped));
    }

    [Fact]
    public void EventAddressesAreBareHexAndTheReadKeepsThePrefix()
    {
        var core = new Core();
        var outcome = core.Handle(new WindowAppeared(new WindowId(0x2A), Area)
        {
            Traits = WindowTraits.Normal with { Class = "kitty", Title = "term" },
        });

        Assert.Equal(
            ["openwindow>>2a,1,kitty,term", "activewindow>>kitty,term", "activewindowv2>>2a"],
            Lines(outcome));
        using var active = System.Text.Json.JsonDocument.Parse(core.Execute("j/activewindow").Text);
        Assert.Equal("0x2a", active.RootElement.GetProperty("address").GetString());
    }

    [Fact]
    public void NamedWorkspaceOpenUsesTheNameAndTheDescribedId()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = workspace name:alpha, class:^(kitty)$\n"));

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Class = "kitty", Title = "term" },
        });

        Assert.Equal(
            [
                "createworkspace>>alpha",
                "createworkspacev2>>-1,alpha",
                "destroyworkspace>>1",
                "destroyworkspacev2>>1,1",
                "openwindow>>1,alpha,kitty,term",
                "workspace>>alpha",
                "workspacev2>>-1,alpha",
                "activewindow>>kitty,term",
                "activewindowv2>>1",
            ],
            Lines(outcome));
    }

    [Fact]
    public void ConfigWarningsEmitInOrder()
    {
        var outcome = new Core().Handle(new ConfigLoaded(ConfigParser.Parse("ghostkeyword = path\nnope = 1\n")));

        Assert.Equal(
            [
                "warning>>1,,Unsupported keyword 'ghostkeyword'.",
                "warning>>2,,Unsupported keyword 'nope'.",
                "configreloaded>>",
            ],
            Lines(outcome));
    }

    [Fact]
    public void SourcedWarningKeepsTheFileAndFlattensNewlines()
    {
        var document = new ConfigDocument(
            [],
            [],
            [new ConfigWarning(4, "bad\r\nline, still", "bad.conf")]);

        var outcome = new Core().Handle(new ConfigLoaded(document));

        Assert.Equal(["warning>>4,bad.conf,bad line, still", "configreloaded>>"], Lines(outcome));
    }

    [Fact]
    public void ConnectingMonitorsEmitsAddedThenTheDescribedForm()
    {
        var core = new Core();

        var outcome = core.Handle(new MonitorsChanged([
            new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, Area),
            new Monitor(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1, true, new Rectangle(1920, 0, 1280, 1024)),
        ]));

        Assert.Equal(
            [
                "createworkspace>>2",
                "createworkspacev2>>2,2",
                @"monitoradded>>\\.\DISPLAY1",
                @"monitoraddedv2>>0,\\.\DISPLAY1,Dell",
                @"monitoradded>>\\.\DISPLAY2",
                @"monitoraddedv2>>1,\\.\DISPLAY2,Samsung",
            ],
            Lines(outcome));
    }

    [Fact]
    public void UnpluggingAMonitorEmitsRemovedThenTheDescribedForm()
    {
        var core = new Core();
        var left = new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, Area);
        var right = new Monitor(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1, true, new Rectangle(1920, 0, 1280, 1024));
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var outcome = core.Handle(new MonitorsChanged([left]));

        Assert.Equal(
            [
                @"moveworkspace>>2,\\.\DISPLAY1",
                @"moveworkspacev2>>2,2,\\.\DISPLAY1",
                @"monitorremoved>>\\.\DISPLAY2",
                @"monitorremovedv2>>1,\\.\DISPLAY2,Samsung",
            ],
            Lines(outcome));
    }

    [Fact]
    public void UnpluggingAMonitorDestroysItsEmptyWorkspace()
    {
        var core = new Core();
        var left = new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, Area);
        var right = new Monitor(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1, true, new Rectangle(1920, 0, 1280, 1024));
        core.Handle(new MonitorsChanged([left, right]));

        var outcome = core.Handle(new MonitorsChanged([left]));

        Assert.Equal(
            [
                "destroyworkspace>>2",
                "destroyworkspacev2>>2,2",
                @"monitorremoved>>\\.\DISPLAY2",
                @"monitorremovedv2>>1,\\.\DISPLAY2,Samsung",
            ],
            Lines(outcome));
        Assert.DoesNotContain(outcome.State.Workspaces, workspace => workspace.Id == WorkspaceId.FromNumber(2));
    }

    [Fact]
    public void FocusingAWorkspaceOnAnotherMonitorEmitsTheMonitorThenTheDesk()
    {
        var core = new Core();
        var left = new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, Area);
        var right = new Monitor(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1, true, new Rectangle(1920, 0, 1280, 1024));
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea)
        {
            Traits = WindowTraits.Normal with { Class = "left", Title = "one" },
        });
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea)
        {
            Traits = WindowTraits.Normal with { Class = "right", Title = "two" },
        });

        var outcome = core.Execute("dispatch workspace 2");

        Assert.Equal(
            [
                @"focusedmon>>\\.\DISPLAY2,2",
                @"focusedmonv2>>\\.\DISPLAY2,2",
                "workspace>>2",
                "workspacev2>>2,2",
                "activewindow>>right,two",
                "activewindowv2>>2",
            ],
            Lines(outcome));
    }

    [Fact]
    public void FocusingANamedWorkspaceUsesItsIdOnTheMonitorEvent()
    {
        var core = new Core();
        var left = new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, Area);
        var right = new Monitor(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1, true, new Rectangle(1920, 0, 1280, 1024));
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea)
        {
            Traits = WindowTraits.Normal with { Class = "right", Title = "two" },
        });
        core.Execute("dispatch workspace 2");
        core.Execute("dispatch movetoworkspace name:beta");
        core.Execute("dispatch workspace 1");

        var outcome = core.Execute("dispatch workspace name:beta");

        Assert.Equal(
            [
                @"focusedmon>>\\.\DISPLAY2,beta",
                @"focusedmonv2>>\\.\DISPLAY2,-1",
                "workspace>>beta",
                "workspacev2>>-1,beta",
                "activewindow>>right,two",
                "activewindowv2>>2",
            ],
            Lines(outcome));
    }

    [Fact]
    public void PullingANamedWorkspaceReportsItsIdAndTheMonitor()
    {
        var core = new Core();
        var left = new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, Area);
        var right = new Monitor(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1, true, new Rectangle(1920, 0, 1280, 1024));
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea)
        {
            Traits = WindowTraits.Normal with { Class = "left", Title = "one" },
        });
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea)
        {
            Traits = WindowTraits.Normal with { Class = "right", Title = "two" },
        });
        core.Execute("dispatch workspace 2");
        core.Execute("dispatch movetoworkspacesilent name:beta");
        core.Execute("dispatch workspace 1");

        var outcome = core.Execute("dispatch workspace name:beta");

        Assert.Equal(
            [
                @"moveworkspace>>beta,\\.\DISPLAY1",
                @"moveworkspacev2>>-1,beta,\\.\DISPLAY1",
                "workspace>>beta",
                "workspacev2>>-1,beta",
                "activewindow>>right,two",
                "activewindowv2>>2",
            ],
            Lines(outcome));
    }

    [Fact]
    public void ReportingTheSameMonitorsEmitsNothing()
    {
        var core = new Core();
        var display = new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, Area);
        core.Handle(new MonitorsChanged([display]));

        var outcome = core.Handle(new MonitorsChanged([display]));

        Assert.Empty(outcome.Events);
    }

    [Fact]
    public void EventsCommandIsAStreamAndLeavesTheDeskAlone()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var reply = core.Execute("events");

        Assert.True(reply.Stream);
        Assert.Equal("", reply.Text);
        Assert.Empty(reply.Effect.Events);
        Assert.Single(reply.Effect.State.Clients);
        Assert.Equal("error: unknown request", core.Execute("events extra").Text);
    }

    private static IReadOnlyList<string> Lines(Outcome outcome) =>
        CommandProtocol.FormatStream(outcome.Events, outcome.State);

    private static IReadOnlyList<string> Lines(CommandReply reply) =>
        Lines(reply.Effect);
}
