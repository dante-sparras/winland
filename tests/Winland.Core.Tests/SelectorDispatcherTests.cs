using System.Text.Json;
using Winland;

namespace Winland.Tests;

public class SelectorDispatcherTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);

    [Fact]
    public void FocusWindowSwitchesToTheClientsWorkspace()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = workspace 2 silent, title:^(away)$\n"));
        Appear(core, 1, "app", "here");
        Appear(core, 2, "app", "away");

        var stayed = core.Handle(new Dispatch("focuswindow", "class:app"));

        Assert.Equal("ok", core.Execute("dispatch focuswindow class:app").Text);
        Assert.Equal(new WindowId(1), stayed.Focus);
        Assert.Equal(new WorkspaceId("1"), stayed.State.ActiveWorkspace);

        var followed = core.Handle(new Dispatch("focuswindow", "title:away"));

        Assert.Equal(new WindowId(2), followed.Focus);
        Assert.Equal(new WorkspaceId("2"), followed.State.ActiveWorkspace);
        Assert.Equal("focuswindow", followed.Dispatcher);
    }

    [Fact]
    public void FocusWindowMovesToTheClientsMonitor()
    {
        var core = new Core();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea)
        {
            Traits = WindowTraits.Normal with { Class = "left" },
        });
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea)
        {
            Traits = WindowTraits.Normal with { Class = "right" },
        });

        var outcome = core.Handle(new Dispatch("focuswindow", "class:right"));

        Assert.Equal(new WindowId(2), outcome.Focus);
        Assert.Equal(new MonitorId(1), outcome.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
    }

    [Fact]
    public void FocusWindowReportsAMiss()
    {
        var core = new Core();
        Appear(core, 1, "app", "here");

        var reply = core.Execute("dispatch focuswindow class:missing");

        Assert.Equal("error: no such window", reply.Text);
        Assert.Null(reply.Effect.Focus);
    }

    [Fact]
    public void CloseWindowClosesTheAddressFromTheClientList()
    {
        var core = new Core();
        Appear(core, 1, "keep", "a");
        Appear(core, 42, "go", "b", processId: 7);
        var clients = Json(core.Execute("clients").Text).EnumerateArray().ToArray();
        var address = clients.Single(client => client.GetProperty("class").GetString() == "go").GetProperty("address").GetString();

        var outcome = core.Handle(new Dispatch("closewindow", "address:" + address));

        Assert.Equal(new WindowId(42), outcome.Close);
        Assert.Null(outcome.Kill);
        Assert.Equal("ok", core.Execute("dispatch closewindow").Text);
        Assert.Null(core.Handle(new Dispatch("closewindow")).Close);
    }

    [Fact]
    public void KillWindowRequestsTheProcessAndDoesNotCloseTheWindow()
    {
        var core = new Core();
        Appear(core, 1, "app", "term", processId: 4242);

        var outcome = core.Handle(new Dispatch("killwindow", "pid:4242"));

        Assert.Equal(new KillRequest(4242) { Window = new WindowId(1) }, outcome.Kill);
        Assert.Null(outcome.Close);
        Assert.Equal("killwindow", outcome.Dispatcher);
        Assert.Null(core.Handle(new Dispatch("killwindow", "pid:1")).Kill);
    }

    [Fact]
    public void FloatingDispatchersHonorASelectorAndDoNotRepeatTheSameState()
    {
        var core = Flat();
        Appear(core, 1, "keep", "a");
        Appear(core, 2, "go", "b");

        var floated = core.Handle(new Dispatch("setfloating", "class:go"));

        Assert.True(Client(floated, 2).Floating);
        Assert.False(Client(floated, 1).Floating);

        var again = core.Handle(new Dispatch("setfloating", "class:go"));
        Assert.Empty(again.Placements);
        Assert.True(Client(again, 2).Floating);

        var missed = core.Handle(new Dispatch("settiled", "class:missing"));
        Assert.True(Client(missed, 2).Floating);

        var tiled = core.Handle(new Dispatch("settiled", "class:go"));
        Assert.False(Client(tiled, 2).Floating);

        var toggled = core.Handle(new Dispatch("togglefloating", "class:keep"));
        Assert.True(Client(toggled, 1).Floating);
        Assert.False(Client(toggled, 2).Floating);
    }

    [Fact]
    public void PseudoUsesTheSelector()
    {
        var core = Flat();
        core.Handle(new WindowAppeared(new WindowId(1), Wide)
        {
            Traits = WindowTraits.Normal with { Class = "Left" },
            Bounds = new Rectangle(0, 0, 800, 600),
        });
        Appear(core, 2, "Right", "b");

        var outcome = core.Handle(new Dispatch("pseudo", "class:Left"));

        Assert.Equal("pseudo", outcome.Dispatcher);
        Assert.Equal(new Rectangle(80, 240, 800, 600), BoundsOf(outcome, 1));
    }

    [Fact]
    public void MoveToWorkspaceReadsTheSelectorAfterTheComma()
    {
        var core = new Core();
        Appear(core, 1, "stay", "a");
        Appear(core, 2, "go", "b");

        var silent = core.Handle(new Dispatch("movetoworkspacesilent", "3,class:go"));

        Assert.Equal(new WorkspaceId("1"), silent.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("3"), Client(silent, 2).Workspace);
        Assert.Equal(new WorkspaceId("1"), Client(silent, 1).Workspace);
        Assert.Null(core.Handle(new Dispatch("movetoworkspace", "4,class:missing")).Focus);

        var followed = core.Handle(new Dispatch("movetoworkspace", "4,class:go"));

        Assert.Equal(new WorkspaceId("4"), followed.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("4"), Client(followed, 2).Workspace);
        Assert.Equal(new WindowId(2), followed.Focus);
    }

    [Fact]
    public void SwapWindowExchangesADirectionOrASelector()
    {
        var core = Flat();
        Appear(core, 1, "Left", "a");
        Appear(core, 2, "Right", "b");

        var directed = core.Handle(new Dispatch("swapwindow", "l"));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(directed, 2));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(directed, 1));

        var selected = core.Handle(new Dispatch("swapwindow", "class:Left"));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(selected, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(selected, 2));
        Assert.Equal("error: No window to swap with", core.Execute("dispatch swapwindow class:missing").Text);
    }

    [Fact]
    public void SwapWindowTradesWorkspacesWithAClientOnAnotherOne()
    {
        var core = Flat();
        Appear(core, 1, "Left", "a");
        Appear(core, 2, "Right", "b");
        core.Handle(new Dispatch("movetoworkspacesilent", "2"));

        var swapped = core.Handle(new Dispatch("swapwindow", "class:Right"));

        Assert.Null(swapped.Error);
        Assert.Equal(WorkspaceId.FromNumber(1), Client(swapped, 2).Workspace);
        Assert.Equal(WorkspaceId.FromNumber(2), Client(swapped, 1).Workspace);
        Assert.Contains(new WindowId(2), swapped.Show);
        Assert.Contains(new WindowId(1), swapped.Hide);
        Assert.Equal(new WindowId(2), swapped.Focus);
        Assert.Equal("error: Can't swap a window with itself", core.Execute("dispatch swapwindow class:Right").Text);
    }

    [Fact]
    public void SwapWindowExchangesATileAndAFloatingClient()
    {
        var core = new Core(ConfigParser.Parse(
            "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\nwindowrulev2 = float, class:^(Float)$\n"));
        Appear(core, 1, "Tile", "a");
        Appear(core, 2, "Float", "b");
        var before = core.Handle(new Dispatch("focuswindow", "class:Tile"));
        var tile = Client(before, 1).Bounds;
        var floated = Client(before, 2).Bounds;

        var swapped = core.Handle(new Dispatch("swapwindow", "class:Float"));

        Assert.Null(swapped.Error);
        Assert.Equal(tile, BoundsOf(swapped, 2));
        Assert.False(Client(swapped, 2).Floating);
        Assert.Equal(floated, BoundsOf(swapped, 1));
        Assert.True(Client(swapped, 1).Floating);
    }

    [Fact]
    public void ExitEventNoOpAndRendererReload()
    {
        var core = new Core();
        Appear(core, 1, "app", "term");

        var exit = core.Execute("dispatch exit");
        Assert.Equal("ok", exit.Text);
        Assert.True(exit.Effect.Exit);
        Assert.Single(exit.Effect.State.Clients);

        var custom = core.Execute("dispatch event hello world");
        Assert.Equal("ok", custom.Text);
        Assert.Contains("custom>>hello world", Lines(custom));

        Assert.Equal("ok", core.Execute("dispatch no_op").Text);
        Assert.Null(core.Execute("dispatch no_op").Effect.Close);
        Assert.False(core.Execute("dispatch no_op").Effect.Exit);

        var reload = core.Execute("dispatch forcerendererreload");
        Assert.Equal("ok", reload.Text);
        Assert.Empty(reload.Effect.Placements);
        Assert.False(reload.Effect.Exit);
    }

    // The requests without their fade, which the default animations give every one.
    private static DisplayPowerRequest[] Unfaded(IReadOnlyList<DisplayPowerRequest> requests) =>
        [.. requests.Select(request => request with { Fade = null })];

    [Fact]
    public void DpmsRequestsPowerForTheNamedMonitorOrEveryMonitor()
    {
        var bare = new Core().Handle(new Dispatch("dpms", "off"));
        Assert.Equal([new DisplayPowerRequest("", false)], Unfaded(bare.DisplayPower));

        var core = new Core();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));

        var one = core.Handle(new Dispatch("dpms", @"off \\.\DISPLAY2"));
        Assert.Equal([new DisplayPowerRequest(@"\\.\DISPLAY2", false)], Unfaded(one.DisplayPower));

        var back = core.Handle(new Dispatch("dpms", "toggle Samsung"));
        Assert.Equal([new DisplayPowerRequest(@"\\.\DISPLAY2", true)], Unfaded(back.DisplayPower));

        var all = core.Handle(new Dispatch("dpms", "off nosuch"));
        Assert.Equal(
            [
                new DisplayPowerRequest(@"\\.\DISPLAY1", false),
                new DisplayPowerRequest(@"\\.\DISPLAY2", false),
            ],
            Unfaded(all.DisplayPower));

        Assert.Equal("ok", core.Execute("dispatch dpms on").Text);
    }

    [Fact]
    public void MouseMoveWakesDisplaysOnlyWithTheOption()
    {
        const string Name = @"\\.\DISPLAY1";
        var plain = new Core();
        plain.Handle(new MonitorsChanged([Display(Name, "Dell", 0, 0, 1920, 1080, 1)]));
        plain.Handle(new Dispatch("dpms", "off"));
        Assert.Empty(plain.Handle(new PointerMoved(new Point(5, 5))).DisplayPower);

        var core = new Core(ConfigParser.Parse("misc:mouse_move_enables_dpms = true\n"));
        core.Handle(new MonitorsChanged([Display(Name, "Dell", 0, 0, 1920, 1080, 1)]));
        Assert.Empty(core.Handle(new PointerMoved(new Point(5, 5))).DisplayPower);

        core.Handle(new Dispatch("dpms", "off"));
        Assert.Equal([new DisplayPowerRequest(Name, true)], Unfaded(core.Handle(new PointerMoved(new Point(6, 6))).DisplayPower));
        Assert.Empty(core.Handle(new PointerMoved(new Point(7, 7))).DisplayPower);

        core.Handle(new Dispatch("dpms", "off"));
        Assert.Equal([new DisplayPowerRequest(Name, true)], Unfaded(core.Handle(new SwipeBegan(3)).DisplayPower));
    }

    [Fact]
    public void KeyWakesDisplaysOnlyWithTheOptionAfterDpmsOff()
    {
        var plain = new Core();
        plain.Handle(new Dispatch("dpms", "off"));
        Assert.False(plain.KeyWakesDisplays);

        var core = new Core(ConfigParser.Parse("misc:key_press_enables_dpms = true\n"));
        Assert.False(core.KeyWakesDisplays);
        core.Handle(new Dispatch("dpms", "off"));
        Assert.True(core.KeyWakesDisplays);
        core.Handle(new Dispatch("dpms", "on"));
        Assert.False(core.KeyWakesDisplays);
    }

    private static Core Flat()
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(
            "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n")));
        return core;
    }

    private static void Appear(Core core, long window, string windowClass, string title, long processId = 0) =>
        core.Handle(new WindowAppeared(new WindowId(window), Wide)
        {
            Traits = WindowTraits.Normal with { Class = windowClass, Title = title },
            ProcessId = processId,
        });

    private static Monitor Display(
        string name,
        string description,
        int x,
        int y,
        int width,
        int height,
        double scale) =>
        new(name, description, x, y, width, height, scale, true, new Rectangle(x, y, width, height));

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Client(outcome, window);
        return Assert.Single(outcome.Placements, placement => placement.Client == client.Id).Bounds;
    }

    private static IReadOnlyList<string> Lines(CommandReply reply) =>
        CommandProtocol.FormatStream(reply.Effect.Events, reply.Effect.State);

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
