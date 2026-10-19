using Winland;

namespace Winland.Tests;

public class ScriptedLayoutTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    private const string Flat = """
        general:gaps_in = 0
        general:gaps_out = 0
        general:border_size = 0
        lualayout = lua:rows
        general:layout = lua:rows

        """;

    // A script that answers nothing by itself: the test answers for it with layoutplace.
    private sealed class Script : ILayoutScripts
    {
        public List<LayoutRequest> Requests { get; } = [];

        public LayoutRequest Last => Requests[^1];

        public void Arrange(LayoutRequest request) => Requests.Add(request);
    }

    [Fact]
    public void ARegisteredNameSelectsTheScriptsLayoutAndAnyOtherLuaNameIsDwindle()
    {
        Assert.IsType<ScriptedLayout>(LayoutFactory.Create(ConfigParser.Parse(Flat)));
        Assert.Equal("lua:rows", LayoutFactory.NameOf(LayoutFactory.Create(ConfigParser.Parse(Flat))));

        var unregistered = ConfigParser.Parse("general:layout = lua:missing\n");
        Assert.Empty(unregistered.Warnings);
        Assert.IsType<DwindleLayout>(LayoutFactory.Create(unregistered));
        Assert.Null(LayoutFactory.Fallback("lua:missing").Warning);

        Assert.IsType<ScriptedLayout>(LayoutFactory.Create(ConfigParser.Parse(Flat + "general:layout = dwindle\n"), "lua:rows"));

        var bad = ConfigParser.Parse("lualayout = rows\n");
        Assert.Equal("A script's layout is named lua:NAME, not 'rows'.", Assert.Single(bad.Warnings).Message);
    }

    [Fact]
    public void TilesTakeAGridUntilTheScriptAnswersAndThenItsBoxes()
    {
        var (core, script) = Open();
        core.Handle(Appeared(1));
        var second = core.Handle(Appeared(2));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(second, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(second, 2));

        var request = script.Last;
        Assert.Equal("lua:rows", request.Name);
        Assert.Equal(Area, request.Area);
        Assert.Equal([new WindowId(1), new WindowId(2)], request.Targets.Select(target => target.Window));

        var answer = request.Answer(new Dictionary<int, LayoutBox> { [0] = new(0, 0, 1920, 540.4), [1] = new(0, 540.4, 1920, 539.6) });
        var reply = core.Execute(answer);

        Assert.Equal("ok", reply.Text);
        Assert.Equal(new Rectangle(0, 0, 1920, 540), BoundsOf(reply.Effect, 1));
        Assert.Equal(new Rectangle(0, 540, 1920, 540), BoundsOf(reply.Effect, 2));
    }

    [Fact]
    public void AnAnswerToAReplacedRequestChangesNothing()
    {
        var (core, script) = Open();
        core.Handle(Appeared(1));
        var stale = script.Last;
        core.Handle(Appeared(2));

        var reply = core.Execute(stale.Answer(new Dictionary<int, LayoutBox> { [0] = new(5, 5, 10, 10) }));

        Assert.Equal("ok", reply.Text);
        Assert.Empty(reply.Effect.Placements);
        Assert.Equal("error: invalid layout placement", core.Execute("layoutplace 1 x").Text);
    }

    [Fact]
    public void AFailedScriptFallsBackToTheGrid()
    {
        var (core, script) = Open();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Execute(script.Last.Answer(new Dictionary<int, LayoutBox> { [0] = new(0, 0, 1920, 540), [1] = new(0, 540, 1920, 540) }));
        core.Handle(Appeared(3));
        core.Execute(script.Last.Answer(new Dictionary<int, LayoutBox> { [2] = new(0, 0, 100, 100) }));

        var reply = core.Execute(Fail(script));

        Assert.Equal(new Rectangle(0, 0, 960, 540), BoundsOf(reply.Effect, 1));
        Assert.Equal(new Rectangle(0, 540, 960, 540), BoundsOf(reply.Effect, 3));
    }

    [Fact]
    public void AClosedTileKeepsTheOthersWhereTheyWereUntilTheScriptAnswers()
    {
        var (core, script) = Open();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Execute(script.Last.Answer(new Dictionary<int, LayoutBox> { [0] = new(0, 0, 1920, 540), [1] = new(0, 540, 1920, 540) }));

        var closed = core.Handle(new WindowClosed(new WindowId(1)));

        Assert.Equal(new Rectangle(0, 540, 1920, 540), BoundsOf(closed, 2));
        Assert.Equal([new WindowId(2)], script.Last.Targets.Select(target => target.Window));
    }

    [Fact]
    public void MoveWindowAndSwapExchangePlacesInTheOrderTheScriptReads()
    {
        var (core, script) = Open();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(Appeared(3));

        core.Handle(new Dispatch("movewindow", "l"));
        Assert.Equal([1L, 3, 2], script.Last.Targets.Select(target => target.Window.Value));

        var before = script.Requests.Count;
        core.Handle(new Dispatch("movewindow", "r"));
        core.Handle(new Dispatch("movewindow", "r"));
        Assert.Equal([1L, 2, 3], script.Last.Targets.Select(target => target.Window.Value));
        Assert.Equal(before + 1, script.Requests.Count);
    }

    [Fact]
    public void ALayoutMessageGoesToTheScriptWithTheTargets()
    {
        var (core, script) = Open();
        core.Handle(Appeared(1));

        var reply = core.Execute("dispatch layoutmsg grow 2");

        Assert.Equal("ok", reply.Text);
        Assert.Equal("grow 2", script.Last.Message);
        Assert.Equal([new WindowId(1)], script.Last.Targets.Select(target => target.Window));
    }

    [Fact]
    public void GapsAndBorderInsetTheScriptsBoxes()
    {
        var (core, script) = Open("""
            general:gaps_in = 5
            general:gaps_out = 20
            general:border_size = 2
            """);
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        Assert.Equal(new Rectangle(20, 20, 1880, 1040), script.Last.Area);

        var reply = core.Execute(script.Last.Answer(new Dictionary<int, LayoutBox> { [0] = new(20, 20, 940, 1040), [1] = new(960, 20, 940, 1040) }));

        Assert.Equal(new Rectangle(22, 22, 931, 1036), BoundsOf(reply.Effect, 1));
        Assert.Equal(new Rectangle(967, 22, 931, 1036), BoundsOf(reply.Effect, 2));
    }

    [Fact]
    public void ReloadingWithoutTheRegistrationReturnsToDwindle()
    {
        var (core, _) = Open();
        core.Handle(Appeared(1));
        Assert.Equal("lua:rows", Workspace(core.Execute("j/activeworkspace").Text));

        core.Handle(new Configure(ConfigParser.Parse("general:layout = lua:rows\n")));

        Assert.Equal("dwindle", Workspace(core.Execute("j/activeworkspace").Text));
    }

    private static string Workspace(string reply)
    {
        using var document = System.Text.Json.JsonDocument.Parse(reply);
        return document.RootElement.GetProperty("tiledLayout").GetString() ?? "";
    }

    private static string Fail(Script script) => script.Last.Answer(null);

    private static (Core Core, Script Script) Open(string extra = "")
    {
        var script = new Script();
        var core = new Core { LayoutScripts = script };
        core.Handle(new Configure(ConfigParser.Parse(Flat + extra)));
        return (core, script);
    }

    private static WindowAppeared Appeared(long window) =>
        new(new WindowId(window), Area);

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        var placement = Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id);
        return placement.Bounds;
    }
}
