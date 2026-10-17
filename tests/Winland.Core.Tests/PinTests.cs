using Winland;

namespace Winland.Tests;

public class PinTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);
    private static readonly WindowId Window = new(1);

    [Fact]
    public void PinnedClientStaysVisibleAcrossWorkspaceSwitches()
    {
        var core = Floating();
        core.Execute("dispatch pin");

        var switched = core.Execute("dispatch workspace 2");

        Assert.Equal("ok", switched.Text);
        Assert.DoesNotContain(switched.Effect.Hide, window => window == Window);
        Assert.DoesNotContain(switched.Effect.State.Hidden, client => client.Window == Window);
        var client = ClientOf(switched.Effect, Window);
        Assert.True(client.Pinned);
        Assert.True(client.Floating);
        Assert.Equal(new WorkspaceId("2"), client.Workspace);
        Assert.Equal(new WorkspaceId("2"), switched.Effect.State.ActiveWorkspace);
    }

    [Fact]
    public void UnpinningLeavesTheClientOnTheShownWorkspace()
    {
        var core = Floating();
        core.Execute("dispatch pin");
        core.Execute("dispatch workspace 2");

        var unpinned = core.Execute("dispatch pin");
        var client = ClientOf(unpinned.Effect, Window);
        Assert.False(client.Pinned);
        Assert.Equal(new WorkspaceId("2"), client.Workspace);
        Assert.DoesNotContain(unpinned.Effect.State.Hidden, hidden => hidden.Window == Window);

        var left = core.Execute("dispatch workspace 3");

        Assert.Contains(left.Effect.Hide, window => window == Window);
        Assert.Contains(left.Effect.State.Hidden, hidden => hidden.Window == Window);
        Assert.Equal(new WorkspaceId("2"), ClientOf(left.Effect, Window).Workspace);
    }

    [Fact]
    public void PinDispatcherEmitsThePinEvent()
    {
        var core = Floating();

        var pinned = core.Execute("dispatch pin");
        var again = core.Execute("dispatch pin on");
        var cleared = core.Execute("dispatch pin disable");

        Assert.Equal(["pin>>1,1"], Lines(pinned));
        Assert.Empty(again.Effect.Events);
        Assert.Equal(["pin>>1,0"], Lines(cleared));
        Assert.Equal("ok", pinned.Text);
    }

    [Fact]
    public void TiledClientDoesNotQualify()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(Window, Area));

        var reply = core.Execute("dispatch pin");

        Assert.Equal("error: window does not qualify to be pinned", reply.Text);
        Assert.Empty(reply.Effect.Events);
        Assert.False(ClientOf(reply.Effect, Window).Pinned);
    }

    [Fact]
    public void MissingClientIsLeftAlone()
    {
        var core = new Core();

        var reply = core.Execute("dispatch pin");

        Assert.Equal("ok", reply.Text);
        Assert.Empty(reply.Effect.Events);
    }

    [Fact]
    public void PinRuleKeepsAFloatingClientAndSkipsATiledOne()
    {
        var pinned = new Core(ConfigParser.Parse("""
            windowrulev2 = float, class:^(stick)$
            windowrulev2 = pin, class:^(stick)$
            """));
        var opened = pinned.Handle(new WindowAppeared(Window, Area)
        {
            Traits = WindowTraits.Normal with { Class = "stick" },
        });

        Assert.True(ClientOf(opened, Window).Pinned);
        Assert.DoesNotContain(opened.Events, stream => stream is ClientPinned);
        var stayed = pinned.Execute("dispatch workspace 2");
        Assert.DoesNotContain(stayed.Effect.Hide, window => window == Window);
        Assert.True(ClientOf(stayed.Effect, Window).Pinned);

        var tiled = new Core(ConfigParser.Parse("windowrulev2 = pin, class:^(stick)$\n"));
        var plain = tiled.Handle(new WindowAppeared(Window, Area)
        {
            Traits = WindowTraits.Normal with { Class = "stick" },
        });
        Assert.False(ClientOf(plain, Window).Pinned);
        Assert.Contains(tiled.Execute("dispatch workspace 2").Effect.Hide, window => window == Window);
    }

    [Fact]
    public void TrailingZeroClearsThePinRule()
    {
        var document = ConfigParser.Parse("windowrulev2 = pin 0, class:^(kitty)$\nwindowrulev2 = pin maybe, class:^(kitty)$\n");

        Assert.Equal(WindowRuleEffect.Pin, Assert.Single(document.WindowRules).Effect);
        Assert.False(document.WindowRules[0].Enabled);
        Assert.Equal(new ConfigWarning(2, "Malformed window rule."), Assert.Single(document.Warnings));
    }

    [Fact]
    public void SilentPinnedClientIsShownOnTheCurrentWorkspace()
    {
        var core = new Core(ConfigParser.Parse("""
            windowrulev2 = float, class:^(stick)$
            windowrulev2 = pin, class:^(stick)$
            windowrulev2 = workspace 2 silent, class:^(stick)$
            """));

        var opened = core.Handle(new WindowAppeared(Window, Area)
        {
            Traits = WindowTraits.Normal with { Class = "stick" },
        });

        Assert.Equal(new WorkspaceId("1"), opened.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("2"), ClientOf(opened, Window).Workspace);
        Assert.Contains(opened.Show, window => window == Window);
        Assert.DoesNotContain(opened.Hide, window => window == Window);
        Assert.DoesNotContain(opened.State.Hidden, client => client.Window == Window);
    }

    [Fact]
    public void FocusStaysOnAPinnedClient()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = float, class:^(stick)$\n"));
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        core.Handle(new WindowAppeared(Window, Area)
        {
            Traits = WindowTraits.Normal with { Class = "stick" },
        });
        core.Execute("dispatch pin");

        var switched = core.Execute("dispatch workspace 2");

        Assert.Equal(
            [
                "createworkspace>>2",
                "createworkspacev2>>2,2",
                "movewindow>>1,2",
                "movewindowv2>>1,2,2",
                "workspace>>2",
                "workspacev2>>2,2",
            ],
            Lines(switched));
        Assert.Contains(switched.Effect.Hide, window => window == new WindowId(2));
        Assert.DoesNotContain(switched.Effect.Hide, window => window == Window);
        Assert.Contains("\"pinned\":true", core.Execute("clients").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TilingClearsPinWithoutAnEvent()
    {
        var core = Floating();
        core.Execute("dispatch pin");

        var tiled = core.Execute("dispatch togglefloating");

        Assert.DoesNotContain(tiled.Effect.Events, stream => stream is ClientPinned);
        Assert.False(ClientOf(tiled.Effect, Window).Pinned);
        Assert.Contains(core.Execute("dispatch workspace 2").Effect.Hide, window => window == Window);
    }

    [Fact]
    public void PinIsNotAWindowProperty()
    {
        var core = Floating();
        core.Execute("dispatch pin");

        Assert.Equal("prop not found", core.Execute("getprop active pin").Text);
        Assert.Equal("error: invalid prop name", core.Execute("setprop active pin false").Text);
    }

    [Fact]
    public void SelectorEnablesOneClient()
    {
        var core = Floating();
        core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { Class = "other" },
        });
        core.Execute("dispatch togglefloating class:other");

        var reply = core.Execute("dispatch pin enable class:other");

        Assert.Equal(["pin>>2,1"], Lines(reply));
        Assert.False(ClientOf(reply.Effect, Window).Pinned);
        Assert.True(ClientOf(reply.Effect, new WindowId(2)).Pinned);
    }

    [Theory]
    [InlineData("movetoworkspace")]
    [InlineData("movetoworkspacesilent")]
    public void APinnedClientIsNotMovedOntoASpecialWorkspace(string dispatcher)
    {
        var core = Floating();
        core.Execute("dispatch pin");

        var refused = core.Execute($"dispatch {dispatcher} special:scratch");

        var client = ClientOf(refused.Effect, Window);
        Assert.True(client.Pinned);
        Assert.Equal(new WorkspaceId("1"), client.Workspace);
        Assert.Null(refused.Effect.State.SpecialWorkspace);
        Assert.DoesNotContain(refused.Effect.State.Workspaces, workspace => workspace.Id.IsSpecial);
        Assert.Empty(refused.Effect.Hide);

        var normal = core.Execute($"dispatch {dispatcher} 3");

        Assert.Equal(new WorkspaceId("3"), ClientOf(normal.Effect, Window).Workspace);
    }

    private static Core Floating()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = float, class:^(stick)$\n"));
        core.Handle(new WindowAppeared(Window, Area)
        {
            Traits = WindowTraits.Normal with { Class = "stick" },
        });
        return core;
    }

    private static Client ClientOf(Outcome outcome, WindowId window)
    {
        foreach (var client in outcome.State.Clients)
        {
            if (client.Window == window)
                return client;
        }

        throw new InvalidOperationException("missing client");
    }

    private static IReadOnlyList<string> Lines(CommandReply reply) =>
        CommandProtocol.FormatStream(reply.Effect.Events, reply.Effect.State);
}
