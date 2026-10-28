namespace Winland.Application.Tests;

public class CoreRegressionTests
{
    private static readonly Rectangle Area = new(0, 0, 1000, 800);

    [Fact]
    public void ClosingAFloatingClientSkipsTheHiddenGroupMemberInFocusHistory()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        core.Handle(new WindowAppeared(new WindowId(3), Area) { Traits = WindowTraits.Normal with { Dialog = true } });

        var closed = core.Handle(new WindowClosed(new WindowId(3)));

        Assert.Equal(new WindowId(2), closed.Focus);
        Assert.DoesNotContain(closed.State.Hidden, client => client.Window == closed.Focus);
    }

    [Fact]
    public void EmptyInitialNamesSurviveAMoveAfterRenaming()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowTitleChanged(new WindowId(1), "renamed"));
        core.Handle(new WindowClassChanged(new WindowId(1), "renamed"));

        var moved = core.Handle(new Dispatch("movetoworkspace", "2"));

        Assert.Equal("", Client(moved, 1).InitialTitle);
        Assert.Equal("", Client(moved, 1).InitialClass);
    }

    [Fact]
    public void RecoveryJsonRestoresTheShownWorkspaceOfEachMonitor()
    {
        var screens = new[] { Display("left", 0, 1000, 800), Display("right", 1000, 1000, 800) };
        var prior = new WindowManager();
        prior.Handle(new MonitorsChanged(screens));
        prior.Handle(new Dispatch("workspace", "3"));
        prior.Handle(new WindowAppeared(new WindowId(1), Area));
        prior.Handle(new Dispatch("focusmonitor", "right"));
        prior.Handle(new Dispatch("workspace", "4"));
        prior.Handle(new WindowAppeared(new WindowId(2), screens[1].WorkArea));
        Assert.True(RecoverySnapshot.TryParse(prior.Recovery().ToJson(), out var saved));
        var core = new WindowManager();
        core.Handle(new MonitorsChanged(screens));
        core.Restore(saved);
        foreach (var client in saved.Clients)
            core.Handle(new WindowAppeared(client.Window, Area));

        var restored = core.Handle(new RestoredAdopted());

        Assert.Equal("3", Assert.Single(restored.State.Screens, screen => screen.Monitor.Name == "left").ActiveWorkspace.Name);
        Assert.Equal("4", Assert.Single(restored.State.Screens, screen => screen.Monitor.Name == "right").ActiveWorkspace.Name);
        Assert.Empty(restored.State.Hidden);
    }

    [Fact]
    public void AZeroMonitorReportUsesFallbackAndReturnsTheFloatingClient()
    {
        var core = new WindowManager();
        var screen = Display("right", 2000, 1000, 800);
        core.Handle(new MonitorsChanged([screen]));
        var opened = core.Handle(new WindowAppeared(new WindowId(1), screen.WorkArea)
        {
            Traits = WindowTraits.Normal with { Dialog = true },
            Bounds = new Rectangle(2200, 200, 300, 200),
        });

        var gone = core.Handle(new MonitorsChanged([]));

        Assert.Equal("FALLBACK", Assert.Single(gone.State.Screens).Monitor.Name);
        Assert.InRange(Client(gone, 1).Bounds.X, 0, 1920 - 300);
        var back = core.Handle(new MonitorsChanged([screen]));
        Assert.Equal(Client(opened, 1).Bounds, Client(back, 1).Bounds);
        Assert.Empty(back.State.Hidden);
    }

    [Fact]
    public void ReloadRematchesOpenClientsAndKeepsSetpropOverrides()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        core.Execute("setprop address:0x2 border_size 7");

        core.Handle(new ConfigLoaded(ConfigParser.Parse("windowrule = border_size 12, class:.*\nwindowrule = no_focus, class:.*")));

        Assert.Equal("12", core.Execute("getprop address:0x1 border_size").Text);
        Assert.Equal("7", core.Execute("getprop address:0x2 border_size").Text);
        Assert.Equal("true", core.Execute("getprop address:0x1 no_focus").Text);
    }

    [Fact]
    public void ClosingTheLastSpecialClientOnAnotherMonitorLeavesFocusHere()
    {
        var core = new WindowManager();
        core.Handle(new MonitorsChanged([Display("left", 0, 1000, 800), Display("right", 1000, 1000, 800)]));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new Dispatch("focusmonitor", "right"));
        core.Handle(new WindowAppeared(new WindowId(2), new Rectangle(1000, 0, 1000, 800)));
        core.Handle(new Dispatch("togglespecialworkspace", "tools"));
        core.Handle(new WindowAppeared(new WindowId(3), new Rectangle(1000, 0, 1000, 800)));
        core.Handle(new Dispatch("focusmonitor", "left"));

        var closed = core.Handle(new WindowClosed(new WindowId(3)));

        Assert.Null(closed.Focus);
        Assert.Equal(new MonitorId(0), closed.State.ActiveMonitor);
        Assert.Equal("0x1", Json(core.Execute("j/activewindow").Text).GetProperty("address").GetString());
    }

    [Fact]
    public void APinnedGuestThatShowsItselfStaysVisible()
    {
        var core = new WindowManager();
        var left = Display("left", 0, 1000, 800);
        var right = Display("right", 1000, 1000, 800);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new Dispatch("focusmonitor", "right"));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea) { Traits = WindowTraits.Normal with { Dialog = true } });
        core.Handle(new Dispatch("pin"));
        core.Handle(new MonitorsChanged([left]));

        var shown = core.Handle(new WindowAppeared(new WindowId(2), Area));

        Assert.DoesNotContain(new WindowId(2), shown.Hide);
        Assert.DoesNotContain(shown.State.Hidden, client => client.Window == new WindowId(2));
    }

    [Fact]
    public void AnOldRecoveryFileWithBothFullscreenFieldsStillRestoresItsMode()
    {
        Assert.True(RecoverySnapshot.TryParse("""
            {"clients":[{"window":1,"workspace":"1","fullscreen":2,"fullscreenClient":2}]}
            """, out var saved));

        Assert.Equal(FullscreenMode.Fullscreen, Assert.Single(saved.Clients).Fullscreen);
        Assert.DoesNotContain("fullscreenClient", saved.ToJson(), StringComparison.Ordinal);
    }
}
