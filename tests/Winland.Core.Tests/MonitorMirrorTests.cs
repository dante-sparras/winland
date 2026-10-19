using System.Text.Json;
using Winland;
using Monitor = Winland.Monitor;

namespace Winland.Tests;

public class MonitorMirrorTests
{
    private const string Left = @"\\.\DISPLAY1";
    private const string Right = @"\\.\DISPLAY2";

    [Fact]
    public void AMirrorShowsItsSourceAndLeavesTheLayout()
    {
        var core = new Core(ConfigParser.Parse($"""
            monitor = {Right}, preferred, auto, 1, mirror, {Left}
            """));
        var left = Display(Left, "Dell", 0, 0, 1920, 1080);
        var right = Display(Right, "Samsung", 1920, 0, 1280, 1024);

        var outcome = core.Handle(new MonitorsChanged([left, right]));
        var opened = core.Handle(new WindowAppeared(new WindowId(1), right.WorkArea));

        Assert.Equal([new DisplayMirrorRequest(Right, Left)], outcome.DisplayMirrors);
        var mirror = Assert.Single(outcome.State.Monitors, monitor => monitor.Name == Right);
        Assert.False(mirror.Enabled);
        Assert.Equal(Left, mirror.MirrorOf);
        Assert.Equal((0, 0), (mirror.X, mirror.Y));
        Assert.All(opened.State.Workspaces, workspace => Assert.Equal(IdOf(outcome, Left), workspace.Monitor));
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), BoundsOf(opened, 1));
    }

    [Fact]
    public void TheMonitorsReplyLeavesAMirrorOutAndAllNamesWhatItMirrors()
    {
        var core = new Core(ConfigParser.Parse($"""
            monitor = {Right}, preferred, auto, 1, mirror, {Left}
            """));
        var outcome = core.Handle(new MonitorsChanged([Display(Left, "Dell", 0, 0, 1920, 1080), Display(Right, "Samsung", 1920, 0, 1920, 1080)]));

        var shown = Json(core.Execute("monitors").Text).EnumerateArray().ToArray();
        var all = Json(core.Execute("monitors all").Text).EnumerateArray().ToArray();

        Assert.Equal(Left, Assert.Single(shown).GetProperty("name").GetString());
        var source = Assert.Single(all, entry => entry.GetProperty("name").GetString() == Left);
        var mirror = Assert.Single(all, entry => entry.GetProperty("name").GetString() == Right);
        Assert.Equal("none", source.GetProperty("mirrorOf").GetString());
        Assert.Equal(IdOf(outcome, Left).Value.ToString(System.Globalization.CultureInfo.InvariantCulture), mirror.GetProperty("mirrorOf").GetString());
        Assert.False(mirror.GetProperty("disabled").GetBoolean());
    }

    [Fact]
    public void AMirrorAppliesWhenItsSourceAppearsAndEndsWhenItLeaves()
    {
        var core = new Core(ConfigParser.Parse($"""
            monitor = {Right}, preferred, auto, 1, mirror, {Left}
            """));
        var left = Display(Left, "Dell", 0, 0, 1920, 1080);
        var right = Display(Right, "Samsung", 0, 0, 1920, 1080);

        var alone = core.Handle(new MonitorsChanged([right]));
        Assert.Empty(alone.DisplayMirrors!);
        Assert.True(Assert.Single(alone.State.Monitors).Enabled);

        var joined = core.Handle(new MonitorsChanged([right, left with { X = 1920 }]));
        Assert.Equal([new DisplayMirrorRequest(Right, Left)], joined.DisplayMirrors);
        Assert.False(Assert.Single(joined.State.Monitors, monitor => monitor.Name == Right).Enabled);

        var left2 = core.Handle(new MonitorsChanged([right]));
        Assert.Empty(left2.DisplayMirrors!);
        var back = Assert.Single(left2.State.Monitors);
        Assert.True(back.Enabled);
        Assert.Equal("", back.MirrorOf);
    }

    [Fact]
    public void ADescriptionNamesTheSource()
    {
        var core = new Core(ConfigParser.Parse($"""
            monitor = {Right}, preferred, auto, 1, mirror, desc:Dell
            """));

        var outcome = core.Handle(new MonitorsChanged([Display(Left, "Dell U2720Q", 0, 0, 1920, 1080), Display(Right, "Samsung", 1920, 0, 1920, 1080)]));

        Assert.Equal([new DisplayMirrorRequest(Right, Left)], outcome.DisplayMirrors);
    }

    [Theory]
    [InlineData(@"\\.\DISPLAY2")]
    [InlineData(@"\\.\DISPLAY9")]
    [InlineData("")]
    public void AMonitorCannotMirrorItselfOrOneThatIsMissing(string source)
    {
        var core = new Core(ConfigParser.Parse($"""
            monitor = {Right}, preferred, auto, 1, mirror, {source}
            """));

        var outcome = core.Handle(new MonitorsChanged([Display(Left, "Dell", 0, 0, 1920, 1080), Display(Right, "Samsung", 1920, 0, 1920, 1080)]));

        Assert.Empty(outcome.DisplayMirrors!);
        Assert.All(outcome.State.Monitors, monitor => Assert.True(monitor.Enabled));
    }

    [Fact]
    public void AMonitorCannotMirrorAMirror()
    {
        var core = new Core(ConfigParser.Parse($"""
            monitor = {Right}, preferred, auto, 1, mirror, {Left}
            monitor = \\.\DISPLAY3, preferred, auto, 1, mirror, {Right}
            """));

        var outcome = core.Handle(new MonitorsChanged([
            Display(Left, "Dell", 0, 0, 1920, 1080),
            Display(Right, "Samsung", 1920, 0, 1920, 1080),
            Display(@"\\.\DISPLAY3", "LG", 3840, 0, 1920, 1080),
        ]));

        Assert.Equal([new DisplayMirrorRequest(Right, Left)], outcome.DisplayMirrors);
        Assert.True(Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY3").Enabled);
    }

    [Fact]
    public void AMonitorAnotherMirrorsCannotBecomeAMirror()
    {
        var core = new Core(ConfigParser.Parse($"""
            monitor = {Right}, preferred, auto, 1, mirror, {Left}
            monitor = {Left}, preferred, auto, 1, mirror, \\.\DISPLAY3
            """));

        var outcome = core.Handle(new MonitorsChanged([
            Display(Right, "Samsung", 1920, 0, 1920, 1080),
            Display(Left, "Dell", 0, 0, 1920, 1080),
            Display(@"\\.\DISPLAY3", "LG", 3840, 0, 1920, 1080),
        ]));

        Assert.Equal([new DisplayMirrorRequest(Right, Left)], outcome.DisplayMirrors);
        Assert.True(Assert.Single(outcome.State.Monitors, monitor => monitor.Name == Left).Enabled);
    }

    [Fact]
    public void ADisabledMonitorIsNoSource()
    {
        var core = new Core(ConfigParser.Parse($"""
            monitor = {Right}, preferred, auto, 1, mirror, {Left}
            monitor = {Left}, disable
            """));

        var outcome = core.Handle(new MonitorsChanged([Display(Left, "Dell", 0, 0, 1920, 1080), Display(Right, "Samsung", 1920, 0, 1920, 1080)]));

        Assert.Empty(outcome.DisplayMirrors!);
        Assert.True(Assert.Single(outcome.State.Monitors, monitor => monitor.Name == Right).Enabled);
    }

    [Fact]
    public void AMirrorSitsWhereItsSourceIsPlaced()
    {
        var core = new Core(ConfigParser.Parse($"""
            monitor = {Left}, preferred, 100x200, 1
            monitor = {Right}, preferred, 0x0, 1, mirror, {Left}
            """));

        var outcome = core.Handle(new MonitorsChanged([Display(Left, "Dell", 0, 0, 1920, 1080), Display(Right, "Samsung", 1920, 0, 1920, 1080)]));

        var mirror = Assert.Single(outcome.State.Monitors, monitor => monitor.Name == Right);
        Assert.Equal((100, 200), (mirror.X, mirror.Y));
    }

    [Fact]
    public void ADisplayWindowsStillMirrorsStaysOutOfTheLayoutButIsNotAskedFor()
    {
        var core = new Core(ConfigParser.Parse(""));
        var left = Display(Left, "Dell", 0, 0, 1920, 1080);

        var outcome = core.Handle(new MonitorsChanged([left, left with { Name = Right, Description = "Samsung", MirrorOf = Left }]));

        Assert.Empty(outcome.DisplayMirrors!);
        var mirror = Assert.Single(outcome.State.Monitors, monitor => monitor.Name == Right);
        Assert.False(mirror.Enabled);
        Assert.Equal(Left, mirror.MirrorOf);
    }

    [Fact]
    public void ReloadingWithoutTheRuleEndsTheMirror()
    {
        var core = new Core(ConfigParser.Parse($"""
            monitor = {Right}, preferred, auto, 1, mirror, {Left}
            """));
        core.Handle(new MonitorsChanged([Display(Left, "Dell", 0, 0, 1920, 1080), Display(Right, "Samsung", 1920, 0, 1920, 1080)]));

        var reloaded = core.Handle(new ConfigLoaded(ConfigParser.Parse("")));

        Assert.NotNull(reloaded.DisplayMirrors);
        Assert.Empty(reloaded.DisplayMirrors);
        Assert.True(Assert.Single(reloaded.State.Monitors, monitor => monitor.Name == Right).Enabled);
    }

    [Fact]
    public void AnOutcomeThatDoesNotLayOutTheMonitorsSaysNothingOfMirrors()
    {
        var core = new Core(ConfigParser.Parse(""));
        core.Handle(new MonitorsChanged([Display(Left, "Dell", 0, 0, 1920, 1080)]));

        var opened = core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 1920, 1080)));

        Assert.Null(opened.DisplayMirrors);
    }

    [Fact]
    public void AMirrorRuleDoesNotWarn()
    {
        var document = ConfigParser.Parse($"""
            monitor = {Right}, preferred, auto, 1, mirror, {Left}
            """);

        Assert.Empty(document.Warnings);
    }

    [Fact]
    public void TheReportAddsBackAMirrorWindowsFoldedIntoItsSource()
    {
        var left = Display(Left, "Dell", 0, 0, 1920, 1080);
        var original = new DisplayMirrorOriginal(Right, Left, "Samsung", 5, 2, 1, new DisplaySourceMode(1920, 1080, 4, 1920, 0));

        var listed = DisplayMirrors.Report([left], [original]);

        Assert.Equal([left, left with { Name = Right, Description = "Samsung", MirrorOf = Left }], listed);
    }

    [Fact]
    public void TheReportAddsNothingForAMirrorWindowsShowsOnItsOwnOrWhoseSourceIsGone()
    {
        var left = Display(Left, "Dell", 0, 0, 1920, 1080);
        var right = Display(Right, "Samsung", 1920, 0, 1920, 1080);
        var original = new DisplayMirrorOriginal(Right, Left, "Samsung", 5, 2, 1, null);

        Assert.Equal([left, right], DisplayMirrors.Report([left, right], [original]));
        Assert.Equal([right], DisplayMirrors.Report([right], [original with { Device = @"\\.\DISPLAY3" }]));
    }

    [Fact]
    public void SavedMirrorsRoundTripBesideTheClients()
    {
        var snapshot = new RecoverySnapshot([])
        {
            Mirrors =
            [
                new DisplayMirrorOriginal(Right, Left, "Samsung", -4294967291, 2, 1, new DisplaySourceMode(1280, 1024, 4, 1920, -10)),
                new DisplayMirrorOriginal(@"\\.\DISPLAY3", Left, "LG", 5, 3, 2, null),
            ],
        };

        Assert.True(RecoverySnapshot.TryParse(snapshot.ToJson(), out var parsed));
        Assert.Equal(snapshot.Mirrors, parsed.Mirrors);
        Assert.True(RecoverySnapshot.TryParse(new RecoverySnapshot([]).ToJson(), out var none));
        Assert.Null(none.Mirrors);
    }

    private static MonitorId IdOf(Outcome outcome, string name) =>
        Assert.Single(outcome.State.Screens, screen => screen.Monitor.Name == name).Id;

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static Monitor Display(string name, string description, int x, int y, int width, int height) =>
        new(name, description, x, y, width, height, 1, true, new Rectangle(x, y, width, height));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        var placement = Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id);
        return placement.Bounds;
    }
}
