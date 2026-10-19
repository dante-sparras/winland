using Winland;

namespace Winland.Tests;

/// <summary>The move and resize gestures act on the focused window, as the pinned release plays them.</summary>
public class WindowGestureTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    private const string Flat = "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n";

    [Fact]
    public void AFloatingWindowMovesWithTheFingers()
    {
        var core = Floating(Flat + "gesture = 3, swipe, move\n");
        var before = Bounds(core);

        var ended = Swipe(core, (10, 0), 10);

        Assert.Equal(before with { X = before.X + 100 }, Bounds(core, ended));
    }

    [Fact]
    public void AFloatingWindowResizesFromTheCornerNearestThePointer()
    {
        var core = Floating(Flat + "gesture = 3, swipe, resize\n");
        var before = Bounds(core);
        core.Handle(new PointerMoved(new Point(before.X + before.Width - 5, before.Y + before.Height - 5)));

        var ended = Swipe(core, (10, 6), 5);

        var after = Bounds(core, ended);
        Assert.Equal(before.X, after.X);
        Assert.Equal(before.Width + 50, after.Width);
        Assert.Equal(before.Height + 30, after.Height);
    }

    [Fact]
    public void ATiledWindowShiftsWithTheFingersAndMovesWhenTheyLift()
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(Flat + "gesture = 3, swipe, move\n")));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        core.Handle(new SwipeBegan(3));
        Outcome shifted = null!;
        for (var i = 0; i < 6; i++)
            shifted = core.Handle(new SwipeUpdated(3, -10, 0));

        var shift = Assert.Single(shifted.Motions);
        Assert.Equal(960 - 30, shift.To.X);

        var ended = core.Handle(new SwipeEnded());

        Assert.Equal(new Rectangle(0, 0, 960, 1080), Bounds(core, ended, window: 2));
        var move = Assert.Single(ended.Motions, motion => motion.Window == new WindowId(2));
        Assert.Equal(new Rectangle(930, 0, 960, 1080), move.From);
    }

    private static Outcome Swipe(Core core, (double X, double Y) step, int count)
    {
        core.Handle(new SwipeBegan(3));
        for (var i = 0; i < count; i++)
            core.Handle(new SwipeUpdated(3, step.X, step.Y));
        return core.Handle(new SwipeEnded());
    }

    private static Core Floating(string config)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(config + "windowrulev2 = float, class:.*\n")));
        core.Handle(new WindowAppeared(new WindowId(1), Area) { Bounds = new Rectangle(400, 300, 640, 480) });
        return core;
    }

    private static Rectangle Bounds(Core core) => Bounds(core, core.Handle(new Dispatch("no_op", "")));

    private static Rectangle Bounds(Core core, Outcome outcome, long window = 1) =>
        Assert.Single(outcome.State.Clients, client => client.Window == new WindowId(window)).Bounds;
}
