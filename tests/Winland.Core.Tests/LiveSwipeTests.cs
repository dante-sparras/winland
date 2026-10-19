using Winland;

namespace Winland.Tests;

/// <summary>A workspace swipe moves the Workspaces with the fingers, and animates on from there when they lift.</summary>
public class LiveSwipeTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    private const string Horizontal = "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\ngesture = 3, horizontal, workspace\n";

    [Fact]
    public void TheWorkspaceFollowsTheFingersAndItsNeighbourComesInBeside()
    {
        var core = Twin(Horizontal);
        core.Handle(new SwipeBegan(3));

        // The first update shows workspace 2, and halfway through a 300 pixel swipe each has moved half a monitor left.
        var first = Move(core, 1, -10);
        var halfway = Move(core, 14, -10);

        Assert.Contains(new WindowId(2), first.Show);
        Assert.DoesNotContain(new WindowId(2), halfway.Show);

        var leaving = Assert.Single(halfway.Motions, motion => motion.Window == new WindowId(1));
        var coming = Assert.Single(halfway.Motions, motion => motion.Window == new WindowId(2));
        Assert.Equal(-960, leaving.To.X);
        Assert.Equal(960, coming.To.X);
        Assert.Null(leaving.Move);
    }

    [Fact]
    public void ASwipeThatFallsShortSlidesBackAndHidesTheNeighbourAgain()
    {
        var core = Twin(Horizontal);
        core.Handle(new SwipeBegan(3));
        Move(core, 5, -10);

        var ended = core.Handle(new SwipeEnded());

        Assert.Equal(WorkspaceId.Initial, ended.State.ActiveWorkspace);
        var back = Assert.Single(ended.Motions, motion => motion.Window == new WindowId(1));
        Assert.Equal(new Rectangle(-320, 0, 1920, 1080), back.From);
        Assert.Equal(Area, back.To);
        var away = Assert.Single(ended.Motions, motion => motion.Window == new WindowId(2));
        Assert.Equal(MotionEnd.Hide, away.End);
        Assert.Equal(1920, away.To.X);
        Assert.Contains(new WindowId(2), ended.Hide);
    }

    [Fact]
    public void ASwitchStartsFromWhereTheSwipeLeftTheWindows()
    {
        var core = Twin(Horizontal);
        core.Handle(new SwipeBegan(3));
        Move(core, 20, -10);

        var ended = core.Handle(new SwipeEnded());

        Assert.Equal(new WorkspaceId("2"), ended.State.ActiveWorkspace);
        var coming = Assert.Single(ended.Motions, motion => motion.Window == new WindowId(2));
        Assert.Equal(new Rectangle(640, 0, 1920, 1080), coming.From);
        Assert.Equal(Area, coming.To);
        Assert.NotNull(coming.Move);
        var leaving = Assert.Single(ended.Motions, motion => motion.Window == new WindowId(1));
        Assert.Equal(new Rectangle(-1280, 0, 1920, 1080), leaving.From);
    }

    [Fact]
    public void AVerticalSlideStyleSwipesVertically()
    {
        var core = Twin(Horizontal + "animation = workspaces, 1, 4, default, slidevert\n");
        core.Handle(new SwipeBegan(3));

        var halfway = Move(core, 15, -10);

        Assert.Equal(-540, Assert.Single(halfway.Motions, motion => motion.Window == new WindowId(1)).To.Y);
    }

    private static Outcome Move(Core core, int count, double x)
    {
        Outcome last = null!;
        for (var i = 0; i < count; i++)
            last = core.Handle(new SwipeUpdated(3, x, 0));
        return last;
    }

    // Workspaces 1 and 2 each hold a Client, and 1 is shown.
    private static Core Twin(string config)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(config)));
        Open(core, 1, "1");
        Open(core, 2, "2");
        core.Handle(new Dispatch("workspace", "1"));
        return core;
    }

    private static void Open(Core core, long window, string workspace)
    {
        core.Handle(new Dispatch("workspace", workspace));
        core.Handle(new WindowAppeared(new WindowId(window), Area));
    }
}
