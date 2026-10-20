using Winland;

namespace Winland.Tests;

public class MotionEffectTests
{
    private static readonly WobbleSettings Spring = WobbleSettings.Documented;

    [Theory]
    [InlineData(200, 12, 1)] // underdamped
    [InlineData(200, 28.2842712, 1)] // critically damped
    [InlineData(200, 120, 1)] // overdamped
    [InlineData(1000, 3, 0.0001)] // a stiff spring on a light mass
    public void ASpringStepMatchesTinyStepsOfTheSameSpring(double stiffness, double damping, double mass)
    {
        double whole = 5, wholeVelocity = -3;
        Winland.Spring.Advance(ref whole, ref wholeVelocity, 1, stiffness, damping, mass, 0.25);

        double split = 5, splitVelocity = -3;
        for (var i = 0; i < 250; i++)
            Winland.Spring.Advance(ref split, ref splitVelocity, 1, stiffness, damping, mass, 0.001);

        Assert.Equal(whole, split, 4);
        Assert.Equal(wholeVelocity, splitVelocity, 3);
    }

    [Fact]
    public void ASpringSettlesOnItsTarget()
    {
        double value = 40, velocity = 0;
        Winland.Spring.Advance(ref value, ref velocity, 1, 200, 12, 1, 5);

        Assert.Equal(1, value, 6);
        Assert.Equal(0, velocity, 6);
    }

    [Fact]
    public void AnUndampedSpringKeepsItsEnergy()
    {
        double value = 3, velocity = 0;
        Winland.Spring.Advance(ref value, ref velocity, 1, 100, 0, 1, Math.PI / 10);

        // Half a period of an amplitude of 2 about 1.
        Assert.Equal(-1, value, 6);
    }

    [Fact]
    public void AMoveRightPushesTheMeshLeft()
    {
        var mesh = new WobbleMesh(4);
        mesh.Push(new EffectBox(0, 0, 400, 300), new EffectBox(20, 0, 400, 300), 0.2);

        var (x, _) = mesh.Displacement(1, 1);
        Assert.True(x < 0);
        Assert.True(mesh.Extents(new EffectBox(20, 0, 400, 300)).X < 20);
    }

    [Fact]
    public void TheMiddleOfAnEdgeIsPushedHarderThanItsCorner()
    {
        var mesh = new WobbleMesh(5);
        mesh.Push(new EffectBox(0, 0, 400, 300), new EffectBox(20, 0, 400, 300), 1);

        // The weight along x is 0.45 at the top and bottom edges and 1 in the middle row, and the left edge moved 20 too.
        Assert.Equal(-29, mesh.Displacement(0, 0).X, 6);
        Assert.Equal(-40, mesh.Displacement(0, 2).X, 6);
    }

    [Fact]
    public void ADisplacementIsLimitedToAQuarterOfTheWindow()
    {
        var mesh = new WobbleMesh(4);
        mesh.Push(new EffectBox(0, 0, 100, 100), new EffectBox(1000, 0, 100, 100), 2);

        var (x, y) = mesh.Displacement(1, 1);
        Assert.Equal(25, Math.Sqrt(x * x + y * y), 6);
    }

    [Fact]
    public void PointsFarFromTheGrabLagMoreThanThePointHeld()
    {
        var mesh = new WobbleMesh(5);
        mesh.Push(new EffectBox(0, 0, 400, 300), new EffectBox(20, 0, 400, 300), 1, (0, 0));

        Assert.Equal(0, mesh.Displacement(0, 0).X, 6);
        Assert.True(mesh.Displacement(4, 4).X < mesh.Displacement(1, 1).X);
    }

    [Fact]
    public void AGrowingEdgeIsPushedBackWhereItMoved()
    {
        var mesh = new WobbleMesh(3);
        mesh.Push(new EffectBox(0, 0, 100, 100), new EffectBox(0, 0, 130, 100), 1);

        Assert.Equal(0, mesh.Displacement(0, 1).X, 6);
        // Pushed back by the 30 pixels it moved, less what the limit of a quarter of the window allows.
        Assert.Equal(-25, mesh.Displacement(2, 1).X, 6);
    }

    [Fact]
    public void AJumpAcrossTheScreenIsNotAWobble()
    {
        Assert.True(WobbleMesh.Jumps(new EffectBox(0, 0, 100, 100), new EffectBox(200, 0, 100, 100)));
        Assert.False(WobbleMesh.Jumps(new EffectBox(0, 0, 100, 100), new EffectBox(120, 0, 100, 100)));
    }

    [Fact]
    public void TheMeshSpringsBackToRestAndThenIsStable()
    {
        var mesh = new WobbleMesh(6);
        mesh.Push(new EffectBox(0, 0, 400, 300), new EffectBox(60, 20, 400, 300), 0.2);
        Assert.False(mesh.Stable(Spring));

        for (var i = 0; i < 600 && !mesh.Stable(Spring); i++)
            mesh.Advance(Spring, 1 / 60.0);

        Assert.True(mesh.Stable(Spring));
    }

    [Fact]
    public void TheMeshHasSixVerticesToACellAndKeepsTheRestShapeAtRest()
    {
        var mesh = new WobbleMesh(3);
        var triangles = new List<MeshVertex>();
        mesh.Triangles(new EffectBox(10, 20, 100, 60), triangles);

        Assert.Equal(4 * 6, triangles.Count);
        Assert.Equal(new MeshVertex(10, 20, 0, 0), triangles[0]);
        Assert.Contains(new MeshVertex(110, 80, 1, 1), triangles);
    }

    [Fact]
    public void TheMeshSizeIsKeptFromTwoToThirtyTwo()
    {
        Assert.Equal(2, new WobbleMesh(0).Size);
        Assert.Equal(32, new WobbleMesh(99).Size);
    }

    [Fact]
    public void ABlurSamplesBoxesFromTheCurrentBoxTowardThePreviousOne()
    {
        var state = new MotionBlurState(new EffectBox(0, 0, 100, 100), new EffectBox(40, 0, 100, 100), 4);

        Assert.Equal(new EffectBox(40, 0, 100, 100), state.Sample(0));
        Assert.Equal(new EffectBox(30, 0, 100, 100), state.Sample(1));
        Assert.Equal(new EffectBox(10, 0, 100, 100), state.Sample(3));
        Assert.Equal(new EffectBox(0, 0, 140, 100), state.Extents);
    }

    [Fact]
    public void ATrailIsFreshForAHundredMillisecondsAndSmallStepsAreNoMotion()
    {
        var trail = new MotionBlurTrail();

        Assert.False(trail.Record(new EffectBox(0, 0, 100, 100), new EffectBox(2, 0, 100, 100), 0));
        Assert.Null(trail.State(0, 7));

        Assert.True(trail.Record(new EffectBox(0, 0, 100, 100), new EffectBox(9, 0, 100, 100), 10));
        Assert.Equal(7, trail.State(110, 7)!.Value.Samples);
        Assert.Null(trail.State(111, 7));
        Assert.NotNull(trail.State(500, 7, allowStale: true));
    }

    [Fact]
    public void ASmallStepForgetsTheTrail()
    {
        var trail = new MotionBlurTrail();
        _ = trail.Record(new EffectBox(0, 0, 100, 100), new EffectBox(30, 0, 100, 100), 0);
        _ = trail.Record(new EffectBox(30, 0, 100, 100), new EffectBox(31, 0, 100, 100), 16);

        Assert.Null(trail.State(16, 7, allowStale: true));
    }

    [Fact]
    public void ATrackWobblesAfterTheMotionStopsAndThenCalms()
    {
        var track = new EffectTrack(new MotionEffects(Spring, 0), 0);
        track.Step(new EffectBox(0, 0, 400, 300), 0);
        track.Step(new EffectBox(40, 0, 400, 300), 16);
        var rest = new EffectBox(40, 0, 400, 300);

        Assert.True(track.Wobbling);
        Assert.True(track.Frame(rest, 16).Canvas.X < 40);

        var now = 16.0;
        var steps = 0;
        while (track.Tick(now += 16) && steps++ < 1000)
        {
        }

        Assert.False(track.Wobbling);
        Assert.Equal(rest, track.Frame(rest, now).Canvas);
    }

    [Fact]
    public void ATrackWithoutWobbleKeepsTheMeshAtRest()
    {
        var track = new EffectTrack(new MotionEffects(null, 7), 0);
        track.Step(new EffectBox(0, 0, 400, 300), 0);
        track.Step(new EffectBox(40, 0, 400, 300), 16);

        Assert.False(track.Wobbling);
        var frame = track.Frame(new EffectBox(40, 0, 400, 300), 16);
        Assert.Equal(new EffectBox(0, 0, 440, 300), frame.Canvas);
        Assert.NotNull(frame.Blur);
    }

    [Fact]
    public void ATrackBlursOnlyAWindowThatMovesMoreThanTwoPixels()
    {
        var track = new EffectTrack(new MotionEffects(null, 7), 0);
        track.Step(new EffectBox(0, 0, 400, 300), 0);
        track.Step(new EffectBox(2, 0, 400, 300), 16);

        Assert.Null(track.Frame(new EffectBox(2, 0, 400, 300), 16).Blur);
        Assert.False(track.Busy(16));
    }

    [Fact]
    public void ATrackThatJumpsDoesNotWobble()
    {
        var track = new EffectTrack(new MotionEffects(Spring, 0), 0);
        track.Step(new EffectBox(0, 0, 100, 100), 0);
        track.Step(new EffectBox(900, 0, 100, 100), 16);

        Assert.False(track.Wobbling);
    }

    [Fact]
    public void ATrackThatLosesItsWobbleSettingCalms()
    {
        var track = new EffectTrack(new MotionEffects(Spring, 0), 0);
        track.Step(new EffectBox(0, 0, 400, 300), 0);
        track.Step(new EffectBox(40, 0, 400, 300), 16);

        track.Use(new MotionEffects(null, 0));

        Assert.False(track.Wobbling);
    }

    [Fact]
    public void TheConfigReadsEveryWobbleAndBlurKeyword()
    {
        var document = ConfigParser.Parse("""
            decoration:wobble:enabled = true
            decoration:wobble:mesh = 99
            decoration:wobble:stiffness = 300
            decoration:wobble:damping = 20
            decoration:wobble:mass = 2
            decoration:wobble:intensity = 5
            decoration:wobble:value_epsilon = 0.5
            decoration:wobble:velocity_epsilon = 4
            decoration:motion_blur:enabled = true
            decoration:motion_blur:samples = 12
            """);

        var options = DecorationOptions.From(document);

        Assert.Empty(document.Warnings);
        Assert.Equal(new WobbleSettings(32, 300, 20, 2, 2, 0.5, 4), options.WobbleSpring);
        Assert.Equal(new MotionEffects(options.WobbleSpring, 12), options.EffectsFor(noWobble: false, fullscreen: false));
    }

    [Fact]
    public void TheEffectsAreOffByDefaultAndForFullscreenAndNoWobble()
    {
        Assert.Null(DecorationOptions.From(ConfigParser.Parse("")).EffectsFor(false, false));

        var document = ConfigParser.Parse("""
            decoration:wobble:enabled = true
            decoration:motion_blur:enabled = true
            """);
        var options = DecorationOptions.From(document);

        Assert.Null(options.EffectsFor(noWobble: false, fullscreen: true));
        Assert.Equal(new MotionEffects(null, 7), options.EffectsFor(noWobble: true, fullscreen: false));
    }

    [Fact]
    public void OneSampleDrawsNoBlur()
    {
        var document = ConfigParser.Parse("""
            decoration:motion_blur:enabled = true
            decoration:motion_blur:samples = 1
            """);

        Assert.Null(DecorationOptions.From(document).EffectsFor(false, false));
    }

    [Fact]
    public void AnOutOfRangeValueIsClampedAndABadOneIsIgnored()
    {
        var options = DecorationOptions.From(ConfigParser.Parse("""
            decoration:wobble:stiffness = 0
            decoration:wobble:damping = soft
            """));

        Assert.Equal(0.0001, options.WobbleSpring.Stiffness);
        Assert.Equal(12, options.WobbleSpring.Damping);
    }

    [Fact]
    public void AClientsDecorationCarriesItsEffectsAndNoWobbleAndFullscreenTakeThemOff()
    {
        const string Config = """
            decoration:wobble:enabled = true
            decoration:motion_blur:enabled = true
            """;
        var area = new Rectangle(0, 0, 800, 600);
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(Config)));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, area)]));
        core.Handle(new WindowAppeared(new WindowId(1), area));

        var options = DecorationOptions.From(ConfigParser.Parse(Config));
        Assert.Equal(options.EffectsFor(false, false), Assert.Single(core.Execute("dispatch no_op").Effect.Decorations).Motion);

        core.Execute("setprop address:0x1 no_wobble 1");
        Assert.Equal(new MotionEffects(null, 7), Assert.Single(core.Execute("dispatch no_op").Effect.Decorations).Motion);

        core.Execute("setprop address:0x1 no_wobble 0");
        core.Execute("dispatch fullscreen 0");
        Assert.Null(Assert.Single(core.Execute("dispatch no_op").Effect.Decorations).Motion);
    }
}
