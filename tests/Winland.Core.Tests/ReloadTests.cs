using Winland;

namespace Winland.Tests;

public class ReloadTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void ReloadSwapsSettingsKeepsClientsAndLaysOut()
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("""
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 0
            bind = SUPER, Q, killactive
            """)));
        core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Class = "kitty" },
        });
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var reloaded = core.Handle(new Configure(ConfigParser.Parse("""
            general:gaps_in = 0
            general:gaps_out = 10
            general:border_size = 0
            bind = SUPER, W, killactive
            windowrulev2 = float, class:^(kitty)$
            monitor = \\.\DISPLAY1, preferred, 4x5, auto
            """)));

        Assert.Equal(new WorkspaceId("2"), reloaded.State.ActiveWorkspace);
        Assert.Equal(2, reloaded.State.Clients.Count);
        Assert.Equal(2, reloaded.State.Workspaces.Count);
        Assert.False(Client(reloaded, 1).Floating);
        Assert.Equal(new Rectangle(10, 10, 1900, 1060), BoundsOf(reloaded, 2));
        Assert.Equal(
            ["configreloaded>>"],
            Lines(reloaded));

        core.Handle(new KeyPressed("Super_L"));
        Assert.Null(core.Handle(new KeyPressed("Q")).Close);
        Assert.Equal(new WindowId(2), core.Handle(new KeyPressed("W")).Close);

        var opened = core.Handle(new WindowAppeared(new WindowId(3), Area)
        {
            Traits = WindowTraits.Normal with { Class = "kitty" },
        });
        Assert.True(Client(opened, 3).Floating);
        Assert.False(Client(opened, 1).Floating);
    }

    [Fact]
    public void WarningsStillApplyAndAreReportedBeforeReload()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new Configure(ConfigParser.Parse("""
            general:gaps_in = 0
            general:gaps_out = 10
            general:border_size = 0
            blur = yes
            """)));

        Assert.Equal(new Rectangle(10, 10, 1900, 1060), BoundsOf(outcome, 1));
        Assert.Equal(
            [
                "warning>>4,,Unsupported keyword 'blur'.",
                "configreloaded>>",
            ],
            Lines(outcome));
    }

    [Fact]
    public void AMissingSourceStillAppliesTheRest()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        var document = ConfigParser.Parse("""
            source = missing.conf
            general:gaps_in = 0
            general:gaps_out = 10
            general:border_size = 0
            """);
        core.ReadConfig = () => new ConfigRead(document);

        var reply = core.Execute("reload");

        Assert.Equal("ok", reply.Text);
        Assert.Equal(new Rectangle(10, 10, 1900, 1060), BoundsOf(reply.Effect, 1));
        Assert.Equal(
            [
                "warning>>1,,Could not read source 'missing.conf'.",
                "configreloaded>>",
            ],
            Lines(reply));
    }

    [Fact]
    public void AnUnreadableConfigKeepsTheWorkingConfigAndWarns()
    {
        var core = new Core();
        var called = false;
        core.Handle(new Configure(ConfigParser.Parse("""
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 0
            bind = SUPER, Q, killactive
            """)));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.ReadConfig = () => new ConfigRead(
            null,
            new ConfigWarning(0, "Could not read config 'winland.conf'.", "winland.conf"));
        core.AfterReload = _ => called = true;

        var reply = core.Execute("reload");

        Assert.False(called);
        Assert.Equal("error: Could not read config 'winland.conf'.", reply.Text);
        Assert.Equal(["warning>>0,winland.conf,Could not read config 'winland.conf'."], Lines(reply));
        Assert.Single(reply.Effect.State.Clients);

        core.Handle(new KeyPressed("Super_L"));
        Assert.Equal(new WindowId(1), core.Handle(new KeyPressed("Q")).Close);
    }

    [Fact]
    public void ReloadCommandAppliesTheDocumentAndNotifiesOnce()
    {
        var core = new Core();
        ConfigDocument? seen = null;
        var calls = 0;
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        var document = ConfigParser.Parse("""
            general:gaps_in = 0
            general:gaps_out = 10
            general:border_size = 0
            exec = notepad
            exec-once = once
            """);
        core.ReadConfig = () => new ConfigRead(document);
        core.AfterReload = loaded =>
        {
            calls++;
            seen = loaded;
        };

        var reply = core.Execute("reload");

        Assert.Equal("ok", reply.Text);
        Assert.Equal(1, calls);
        Assert.Same(document, seen);
        Assert.NotNull(seen);
        Assert.Equal([new Launch("notepad", null, false)], core.ReloadLaunches(seen));
        Assert.Equal(new Rectangle(10, 10, 1900, 1060), BoundsOf(reply.Effect, 1));
        Assert.Contains("configreloaded>>", Lines(reply));
    }

    [Fact]
    public void ApplyingADocumentDirectlyDoesNotNotify()
    {
        var called = false;
        var core = new Core { AfterReload = _ => called = true };

        core.Handle(new Configure(ConfigParser.Parse("exec = notepad")));

        Assert.False(called);
    }

    [Fact]
    public void ReloadWithoutAConfigSourceReportsAnError()
    {
        var reply = new Core().Execute("reload");

        Assert.Equal("error: config is not available", reply.Text);
        Assert.Empty(reply.Effect.Events);
    }

    [Fact]
    public void ReloadWithArgumentsIsRejected()
    {
        var parsed = CommandProtocol.Parse("reload");
        Assert.IsType<ReloadRequest>(Assert.IsType<CommandParsed>(parsed).Request);

        var reply = new Core().Execute("reload now");

        Assert.Equal("error: unknown request", reply.Text);
    }

    [Fact]
    public void ReloadAppliesMonitorRulesAlreadyKnown()
    {
        var core = new Core();
        core.Handle(new MonitorsChanged(
        [
            new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 100, 100, 1, true, new Rectangle(0, 0, 100, 100)),
        ]));
        core.ReadConfig = () => new ConfigRead(ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, 4x5, auto
            """));

        var reply = core.Execute("reload");

        var monitor = Assert.Single(reply.Effect.State.Monitors);
        Assert.Equal(4, monitor.X);
        Assert.Equal(5, monitor.Y);
        Assert.Equal("ok", reply.Text);
    }

    [Fact]
    public void ReloadSwitchesEveryLayoutAndKeepsTheClients()
    {
        var area = new Rectangle(0, 0, 2000, 1000);
        var floated = new Rectangle(40, 50, 320, 200);
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(LayoutText("dwindle"))));
        core.Handle(Appeared(1, area));
        core.Handle(Appeared(2, area));
        core.Handle(Appeared(3, area, new Size(1, 700)));
        core.Handle(new WindowAppeared(new WindowId(4), area)
        {
            Traits = WindowTraits.Normal with { Class = "c4", Title = "t4", Dialog = true },
            Bounds = floated,
            ProcessId = 1004,
        });
        floated = NewFloat.Centered(area, floated);

        var master = Reload(core, "master");
        AssertKept(master, floated);
        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(master, 1));
        Assert.Equal(new Rectangle(1100, 0, 900, 300), BoundsOf(master, 2));
        Assert.Equal(new Rectangle(1100, 300, 900, 700), BoundsOf(master, 3));

        var monocle = Reload(core, "monocle");
        AssertKept(monocle, floated);
        Assert.Equal(area, BoundsOf(monocle, 3));
        Assert.DoesNotContain(monocle.Placements, placement => placement.Client == Client(monocle, 1).Id);
        Assert.DoesNotContain(monocle.Placements, placement => placement.Client == Client(monocle, 2).Id);
        Assert.Contains(new WindowId(1), monocle.Hide);
        Assert.Contains(new WindowId(2), monocle.Hide);

        var scrolling = Reload(core, "scrolling");
        AssertKept(scrolling, floated);
        Assert.DoesNotContain(scrolling.Placements, placement => placement.Client == Client(scrolling, 1).Id);
        Assert.Equal(new Rectangle(0, 0, 1000, 1000), BoundsOf(scrolling, 2));
        Assert.Equal(new Rectangle(1000, 0, 1000, 1000), BoundsOf(scrolling, 3));

        var again = Reload(core, "master");
        AssertKept(again, floated);
        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(again, 1));
        Assert.Equal(new Rectangle(1100, 0, 900, 300), BoundsOf(again, 2));
        Assert.Equal(new Rectangle(1100, 300, 900, 700), BoundsOf(again, 3));

        var dwindle = Reload(core, "dwindle");
        AssertKept(dwindle, floated);
        Assert.Equal(3, dwindle.Placements.Count);
        Assert.NotEqual(new Rectangle(0, 0, 1100, 1000), BoundsOf(dwindle, 1));
    }

    [Fact]
    public void SourcesAreRecordedInOrder()
    {
        var source = new MemorySource(new Dictionary<string, string>
        {
            ["colors.conf"] = "general:gaps_in = 1\n",
        });

        var document = ConfigParser.Parse("source = colors.conf\nsource = missing.conf\n", source);

        Assert.Equal(["colors.conf", "missing.conf"], document.Sources);
    }

    private static string LayoutText(string name) =>
        "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\ngeneral:layout = " + name + "\n";

    private static Outcome Reload(Core core, string layout) =>
        core.Handle(new Configure(ConfigParser.Parse(LayoutText(layout))));

    private static WindowAppeared Appeared(long window, Rectangle area, Size? minSize = null) =>
        new(new WindowId(window), area)
        {
            Traits = WindowTraits.Normal with
            {
                Class = "c" + window,
                Title = "t" + window,
                MinSize = minSize,
            },
            ProcessId = 1000 + window,
        };

    private static void AssertKept(Outcome outcome, Rectangle floated)
    {
        Assert.Equal(4, outcome.State.Clients.Count);
        for (var window = 1L; window <= 4; window++)
        {
            var client = Client(outcome, window);
            Assert.Equal("c" + window, client.Class);
            Assert.Equal("t" + window, client.Title);
            Assert.Equal(1000 + window, client.ProcessId);
        }

        var dialog = Client(outcome, 4);
        Assert.True(dialog.Floating);
        Assert.Equal(floated, dialog.Bounds);
    }

    private static IReadOnlyList<string> Lines(Outcome outcome) =>
        CommandProtocol.FormatStream(outcome.Events, outcome.State);

    private static IReadOnlyList<string> Lines(CommandReply reply) =>
        Lines(reply.Effect);

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Client(outcome, window);
        var placement = Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id);
        return placement.Bounds;
    }

    private sealed class MemorySource(IReadOnlyDictionary<string, string> files) : IConfigSource
    {
        public string? Read(string path) => files.TryGetValue(path, out var text) ? text : null;
    }
}
