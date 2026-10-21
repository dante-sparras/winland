using Winland;

namespace Winland.Tests;

/// <summary>The close gesture fades and shrinks the focused window with the fingers, as Winland plays it.</summary>
public class CloseSwipeTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    private const string Config = "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\ngesture = 3, down, close\n";

    [Fact]
    public void TheWindowFadesAndShrinksWithTheFingers()
    {
        var core = Opened(Config);
        core.Handle(new SwipeBegan(3));

        var halfway = Down(core, 10, 10);

        var motion = Assert.Single(halfway.Motions);
        Assert.Equal(0.5, motion.FadeTo, 3);
        Assert.Null(motion.Fade);
        Assert.True(motion.To.Width < Area.Width);
        Assert.Equal(Area, motion.Rest);
    }

    [Fact]
    public void AShortSwipeBringsTheWindowBack()
    {
        var core = Opened(Config);
        core.Handle(new SwipeBegan(3));
        Down(core, 3, 10);

        var ended = core.Handle(new SwipeEnded());

        Assert.Empty(ended.Closes);
        var back = Assert.Single(ended.Motions);
        Assert.Equal(Area, back.To);
        Assert.Equal(1, back.FadeTo);
        Assert.Equal(0.85, back.FadeFrom!.Value, 3);
    }

    [Fact]
    public void ALongerSwipeClosesFromWhereItLeftTheWindowAndWaitsTheTimeout()
    {
        var core = Opened(Config + "gestures:close_max_timeout = 600\n");
        core.Handle(new SwipeBegan(3));
        Down(core, 10, 10);

        var ended = core.Handle(new SwipeEnded());

        Assert.Equal([new WindowId(1)], ended.Closes);
        var leave = Assert.Single(ended.Motions, motion => motion.Window == new WindowId(1));
        Assert.Equal(MotionEnd.Close, leave.End);
        Assert.Equal(0.5, leave.FadeFrom!.Value, 3);
        Assert.Equal(0, leave.FadeTo);
        Assert.Equal(600, leave.CloseGraceMilliseconds);
        Assert.Equal(Area, leave.Rest);
    }

    [Fact]
    public void TheTimeoutIsAnOptionNotAGap()
    {
        var document = ConfigParser.Parse("gestures:close_max_timeout = 5000\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(2000, GestureOptions.From(document).CloseMaxTimeout);
    }

    private static Outcome Down(Core core, int count, double y)
    {
        Outcome last = null!;
        for (var i = 0; i < count; i++)
            last = core.Handle(new SwipeUpdated(3, 0, y));
        return last;
    }

    private static Core Opened(string config)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(config)));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        return core;
    }
}
