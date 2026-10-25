using System.Reflection;

namespace Winland.Application.Tests;

public class EmptyStepTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    private static readonly ClientId One = new(1);

    private static readonly WindowId OneWindow = new(1);

    private static readonly Rectangle Tile = new(0, 0, 100, 100);

    // What a step rebuilds each time carries a value of its own here, so that an outcome built from it with one change
    // differs from it in that property alone.
    private static readonly Outcome Applied = new([], new State([]))
    {
        Decorations = [new Decoration(One, new BorderColor(255, 0, 0, 0), CornerPreference.Round)],
        GroupBars = [new GroupBar(OneWindow, Tile, [new GroupTab(OneWindow, "a", true)], GroupBarStyle.Documented)],
        ConfigNotice = new ConfigNotice("title", "text", null, 1),
        Tray = new TrayIcon(true, "1"),
        Cursor = new CursorBehavior(true, 0),
        ResizeCursor = "right_side",
    };

    // Every property of Outcome, with a change that makes the empty rule see it, or true when it deliberately does not.
    private static readonly Dictionary<string, (Func<Outcome, Outcome> Change, bool StaysEmpty)> Rules = new()
    {
        [nameof(Outcome.Placements)] = (o => o with { Placements = [new Placement(One, Tile)] }, false),
        [nameof(Outcome.State)] = (o => o with { State = new State([]) { ActiveWorkspace = new WorkspaceId("2") } }, true),
        [nameof(Outcome.Consumed)] = (o => o with { Consumed = true }, false),
        [nameof(Outcome.Traits)] = (o => o with { Traits = DispatcherTraits.None }, false),
        [nameof(Outcome.Focus)] = (o => o with { Focus = OneWindow }, false),
        [nameof(Outcome.Closes)] = (o => o with { Closes = [OneWindow] }, false),
        [nameof(Outcome.Close)] = (o => o with { Close = OneWindow }, false),
        [nameof(Outcome.Show)] = (o => o with { Show = [OneWindow] }, false),
        [nameof(Outcome.Hide)] = (o => o with { Hide = [OneWindow] }, false),
        [nameof(Outcome.ShowMinimized)] = (o => o with { ShowMinimized = [OneWindow] }, false),
        [nameof(Outcome.Launches)] = (o => o with { Launches = [new Launch("notepad", null)] }, false),
        [nameof(Outcome.Launch)] = (o => o with { Launch = new Launch("notepad", null) }, false),
        [nameof(Outcome.Events)] = (o => o with { Events = [new ClientClosed(OneWindow)] }, false),
        [nameof(Outcome.Decorations)] = (o => o with { Decorations = [new Decoration(One, new BorderColor(255, 1, 1, 1), CornerPreference.Round)] }, false),
        [nameof(Outcome.Motions)] = (o => o with { Motions = [new Motion(OneWindow, Tile)] }, false),
        [nameof(Outcome.Live)] = (o => o with { Live = true }, false),
        [nameof(Outcome.ResetDecorations)] = (o => o with { ResetDecorations = [OneWindow] }, false),
        [nameof(Outcome.GroupBars)] = (o => o with { GroupBars = [] }, false),
        [nameof(Outcome.ConfigNotice)] = (o => o with { ConfigNotice = null }, false),
        [nameof(Outcome.Tray)] = (o => o with { Tray = new TrayIcon(true, "2") }, false),
        [nameof(Outcome.Warp)] = (o => o with { Warp = new WarpTo(new Point(5, 5)) }, false),
        [nameof(Outcome.Cursor)] = (o => o with { Cursor = new CursorBehavior(false, 0) }, false),
        [nameof(Outcome.ResizeCursor)] = (o => o with { ResizeCursor = null }, false),
        [nameof(Outcome.Error)] = (o => o with { Error = "error: no such workspace" }, false),
        [nameof(Outcome.Kills)] = (o => o with { Kills = [new KillRequest(7)] }, false),
        [nameof(Outcome.Kill)] = (o => o with { Kill = new KillRequest(7) }, false),
        [nameof(Outcome.Exit)] = (o => o with { Exit = true }, false),
        [nameof(Outcome.Relays)] = (o => o with { Relays = [new KeyRelay(null, "a", KeyRelayPhase.Press)] }, false),
        [nameof(Outcome.ZOrder)] = (o => o with { ZOrder = [new ZOrderRequest(OneWindow, Top: true)] }, false),
        [nameof(Outcome.Order)] = (o => o with { Order = new OutcomeOrder(1, 2) }, true),
        [nameof(Outcome.IsEmpty)] = (o => o with { IsEmpty = true }, true),
    };

    [Fact]
    public void EveryPropertyHasARuleInIsEmptyAfter()
    {
        var properties = typeof(Outcome)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name);

        // A new property fails here until the empty rule in Outcome.IsEmptyAfter decides what it means and this table says so.
        Assert.Equal(Rules.Keys.Order(StringComparer.Ordinal), properties.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AnOutcomeLikeTheLastAppliedOneIsEmpty()
    {
        Assert.True((Applied with { }).IsEmptyAfter(Applied));
    }

    [Fact]
    public void NothingIsEmptyBeforeAnythingWasApplied()
    {
        Assert.False((Applied with { }).IsEmptyAfter(null));
    }

    [Fact]
    public void EachPropertyIsSeenByTheRuleOrLeftOutOnPurpose()
    {
        foreach (var (name, rule) in Rules)
            Assert.True(rule.Change(Applied with { }).IsEmptyAfter(Applied) == rule.StaysEmpty, name);
    }

    [Fact]
    public void ThenIsEmptyOnlyWhenBothAre()
    {
        var empty = Outcome.Empty(new State([]));
        var busy = new Outcome([], new State([])) { Closes = [OneWindow] };

        Assert.True(empty.Then(empty).IsEmpty);
        Assert.False(empty.Then(busy).IsEmpty);
        Assert.False(busy.Then(empty).IsEmpty);
    }

    [Fact]
    public void TheFirstStepIsNotEmpty()
    {
        var core = new WindowManager(ConfigParser.Parse(Flat("")));

        Assert.False(core.Handle(new PointerMoved(new Point(10, 10)) { TimeMs = 0 }).IsEmpty);
    }

    [Fact]
    public void AMoveOverTheSameWindowChangesNothing()
    {
        var (core, laid) = Tiled();
        Move(core, Center(laid, 2), 1_000);

        var moved = Move(core, Center(laid, 2) with { X = Center(laid, 2).X + 10 }, 1_100);

        Assert.True(moved.IsEmpty);
        Assert.Empty(moved.Placements);
        Assert.Equal(2, moved.State.Clients.Count);
    }

    [Fact]
    public void AMoveThatChangesTheFocusIsNotEmpty()
    {
        var (core, laid) = Tiled();
        Move(core, Center(laid, 2), 1_000);
        Move(core, Center(laid, 2) with { X = Center(laid, 2).X + 10 }, 1_050);

        var followed = Move(core, Center(laid, 1), 1_100);

        Assert.False(followed.IsEmpty);
        Assert.Equal(OneWindow, followed.Focus);
    }

    [Fact]
    public void AStepThatChangesTheDecorationsIsNotEmpty()
    {
        var (core, laid) = Tiled();
        Move(core, Center(laid, 2), 1_000);
        Assert.True(Move(core, Center(laid, 2) with { X = Center(laid, 2).X + 10 }, 1_050).IsEmpty);

        var focused = core.Handle(new WindowFocused(OneWindow, FocusCause.User));

        Assert.False(focused.IsEmpty);
        Assert.NotEmpty(focused.Decorations);
    }

    [Fact]
    public void LeavingAResizeGrabIsNotEmpty()
    {
        var core = Configured("""
            general:resize_on_border = true
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 0
            """);
        core.Handle(new WindowAppeared(OneWindow, Area));
        core.Handle(new Dispatch("togglefloating"));
        var reached = core.Handle(new PointerMoved(new Point(1280, 540)));
        var along = core.Handle(new PointerMoved(new Point(1280, 560)));

        var left = core.Handle(new PointerMoved(new Point(700, 400)));

        Assert.Equal("right_side", reached.ResizeCursor);
        Assert.False(reached.IsEmpty);
        Assert.True(along.IsEmpty);
        Assert.Null(left.ResizeCursor);
        Assert.False(left.IsEmpty);
    }

    [Theory]
    [InlineData("clients")]
    [InlineData("j/clients")]
    [InlineData("workspaces")]
    [InlineData("monitors")]
    [InlineData("monitors all")]
    [InlineData("activewindow")]
    [InlineData("activeworkspace")]
    [InlineData("binds")]
    [InlineData("version")]
    [InlineData("configerrors")]
    [InlineData("getoption general:gaps_in")]
    [InlineData("getprop address:0x1 opacity")]
    public void AQueryAnswersAndChangesNothing(string query)
    {
        var (core, laid) = Tiled();
        Move(core, Center(laid, 2), 1_000);
        var before = core.Execute("j/clients").Text;

        var reply = core.Execute(query);

        Assert.NotEmpty(reply.Text);
        Assert.True(reply.Effect.IsEmpty);
        Assert.Equal(2, reply.Effect.State.Clients.Count);
        Assert.Equal(before, core.Execute("j/clients").Text);
        Assert.True(Move(core, Center(laid, 2) with { X = Center(laid, 2).X + 10 }, 1_100).IsEmpty);
    }

    [Fact]
    public void AQueryAnswersTheSameInABatchAsOnItsOwn()
    {
        var (core, _) = Tiled();

        var alone = core.Execute("j/clients").Text;
        var batched = core.Execute("[[BATCH]]j/clients");

        Assert.Equal(alone, batched.Text);
    }

    [Fact]
    public void ACommandThatChangesTheStateIsNotAQuery()
    {
        var (core, _) = Tiled();

        var reply = core.Execute("dispatch togglefloating");

        Assert.False(reply.Effect.IsEmpty);
    }

    [Fact]
    public void TheFirstStepAfterAConfigReloadIsNotEmpty()
    {
        var (core, laid) = Tiled();
        var document = ConfigParser.Parse(Flat(""));
        core.ReadConfig = () => new ConfigRead(document);
        Move(core, Center(laid, 2), 1_000);
        Assert.True(Move(core, Center(laid, 2) with { X = Center(laid, 2).X + 10 }, 1_050).IsEmpty);

        var reloaded = core.Execute("reload");
        var after = Move(core, Center(laid, 2) with { X = Center(laid, 2).X + 20 }, 1_100);
        var then = Move(core, Center(laid, 2) with { X = Center(laid, 2).X + 30 }, 1_150);

        Assert.False(reloaded.Effect.IsEmpty);
        Assert.False(after.IsEmpty);
        Assert.NotEmpty(after.Decorations);
        Assert.True(then.IsEmpty);
    }

    [Fact]
    public void TheFirstStepAfterAConfigIsConfiguredIsNotEmpty()
    {
        var (core, laid) = Tiled();
        Move(core, Center(laid, 2), 1_000);
        Assert.True(Move(core, Center(laid, 2) with { X = Center(laid, 2).X + 10 }, 1_050).IsEmpty);

        var configured = core.Handle(new Configure(ConfigParser.Parse(Flat(""))));
        var after = Move(core, Center(laid, 2) with { X = Center(laid, 2).X + 20 }, 1_100);

        Assert.False(configured.IsEmpty);
        Assert.False(after.IsEmpty);
    }

    private static (WindowManager WindowManager, Outcome Laid) Tiled()
    {
        var core = new WindowManager(ConfigParser.Parse(Flat("")));
        core.Handle(new WindowAppeared(OneWindow, Area));
        return (core, core.Handle(new WindowAppeared(new WindowId(2), Area)));
    }

    private static string Flat(string extra) =>
        "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + extra;

    private static Outcome Move(WindowManager core, Point point, long time) =>
        core.Handle(new PointerMoved(point) { TimeMs = time });

    private static Point Center(Outcome outcome, long window)
    {
        var bounds = Client(outcome, window).Bounds;
        return new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }
}
