using Winland;

namespace Winland.Tests;

public class AnimatorTests
{
    private static readonly WindowId Window = new(1);
    private static readonly Timing Linear = new(Bezier.Linear, 100);

    [Fact]
    public void AMotionFollowsItsCurveAndLandsOnItsGoal()
    {
        var animator = new Animator();
        animator.Start(new Motion(Window, new Rectangle(100, 0, 200, 100)) { From = new Rectangle(0, 0, 100, 100), Move = Linear }, now: 0);

        var halfway = Assert.Single(animator.Step(50));
        var landed = Assert.Single(animator.Step(100));

        Assert.Equal(new Rectangle(50, 0, 150, 100), halfway.Bounds);
        Assert.False(halfway.Done);
        Assert.Equal(new Rectangle(100, 0, 200, 100), landed.Bounds);
        Assert.True(landed.Done);
        Assert.Equal(MotionEnd.None, landed.End);
        Assert.Equal(new Rectangle(100, 0, 200, 100), landed.Rest);
        Assert.Equal(0, animator.Count);
        Assert.Empty(animator.Step(150));
    }

    // A hide or close that only fades keeps the move already running to the same goal, as the
    // position and the alpha are animated on their own.
    [Fact]
    public void AFadeWithoutAMoveLetsARunningMoveToTheSameGoalContinue()
    {
        var animator = new Animator();
        var goal = new Rectangle(100, 0, 100, 100);
        animator.Start(new Motion(Window, goal) { From = new Rectangle(0, 0, 100, 100), Move = Linear }, now: 0);
        animator.Start(new Motion(Window, goal) { FadeTo = 0, Fade = Linear, End = MotionEnd.Hide, Rest = goal }, now: 50);

        Assert.Equal(new Rectangle(75, 0, 100, 100), Assert.Single(animator.Step(75)).Bounds);
    }

    // A motion without a move to a different goal still drops the running move.
    [Fact]
    public void AMotionWithoutAMoveToAnotherGoalDropsTheRunningMove()
    {
        var animator = new Animator();
        animator.Start(new Motion(Window, new Rectangle(100, 0, 100, 100)) { From = new Rectangle(0, 0, 100, 100), Move = Linear }, now: 0);
        animator.Start(new Motion(Window, new Rectangle(300, 0, 100, 100)) { FadeTo = 0, Fade = Linear }, now: 50);

        Assert.Null(Assert.Single(animator.Step(75)).Bounds);
    }

    [Fact]
    public void ACurvedMotionUsesTheCurveBetweenItsEnds()
    {
        var animator = new Animator();
        var curve = new Timing(new Bezier(0.25, 0.1, 0.25, 1), 1000);
        animator.Start(new Motion(Window, new Rectangle(1000, 0, 10, 10)) { From = new Rectangle(0, 0, 10, 10), Move = curve }, now: 0);

        var frame = Assert.Single(animator.Step(500));

        Assert.Equal(802, frame.Bounds!.Value.X);
    }

    [Fact]
    public void AnOvershootingMotionStillLandsExactly()
    {
        var animator = new Animator();
        var overshoot = new Timing(new Bezier(0.5, 0.9, 0.1, 1.5), 100);
        animator.Start(new Motion(Window, new Rectangle(100, 0, 10, 10)) { From = new Rectangle(0, 0, 10, 10), Move = overshoot }, now: 0);

        var passing = Enumerable.Range(1, 9).Select(i => animator.Peek(Window, i * 10)!.Bounds!.Value.X).Max();
        var landed = Assert.Single(animator.Step(100));

        Assert.True(passing > 100);
        Assert.Equal(new Rectangle(100, 0, 10, 10), landed.Bounds);
    }

    [Fact]
    public void AnInterruptedMotionContinuesFromWhereItIsAndLandsOnTheNewRectangle()
    {
        var animator = new Animator();
        animator.Start(new Motion(Window, new Rectangle(100, 0, 100, 100)) { From = new Rectangle(0, 0, 100, 100), Move = Linear }, now: 0);
        Assert.Equal(new Rectangle(50, 0, 100, 100), Assert.Single(animator.Step(50)).Bounds);

        animator.Start(new Motion(Window, new Rectangle(0, 200, 100, 100)) { Move = Linear }, now: 50);

        Assert.Equal(new Rectangle(50, 0, 100, 100), animator.Peek(Window, 50)!.Bounds);
        Assert.Equal(new Rectangle(25, 100, 100, 100), Assert.Single(animator.Step(100)).Bounds);
        var landed = Assert.Single(animator.Step(150));
        Assert.Equal(new Rectangle(0, 200, 100, 100), landed.Bounds);
        Assert.True(landed.Done);
        Assert.Equal(new Rectangle(0, 200, 100, 100), landed.Rest);
    }

    [Fact]
    public void EachChangeRaisesTheGeneration()
    {
        var animator = new Animator();
        animator.Start(new Motion(Window, new Rectangle(100, 0, 100, 100)) { Move = Linear }, now: 0);
        var first = animator.Generation(Window);

        animator.Start(new Motion(Window, new Rectangle(0, 0, 100, 100)) { Move = Linear }, now: 10);
        var second = animator.Generation(Window);
        Assert.False(animator.Settle(Window, new Rectangle(5, 5, 5, 5)));

        Assert.True(second > first);
        Assert.True(animator.Generation(Window) > second);
    }

    [Fact]
    public void APlacementOnTheSameGoalLeavesTheMotionRunning()
    {
        var animator = new Animator();
        var goal = new Rectangle(100, 0, 100, 100);
        animator.Start(new Motion(Window, goal) { From = new Rectangle(0, 0, 100, 100), Move = Linear }, now: 0);

        Assert.True(animator.Settle(Window, goal));
        Assert.Equal(new Rectangle(50, 0, 100, 100), Assert.Single(animator.Step(50)).Bounds);
    }

    [Fact]
    public void APlacementOnAnotherRectangleStopsTheMovement()
    {
        var animator = new Animator();
        animator.Start(new Motion(Window, new Rectangle(100, 0, 100, 100)) { From = new Rectangle(0, 0, 100, 100), Move = Linear }, now: 0);

        Assert.False(animator.Settle(Window, new Rectangle(300, 0, 100, 100)));

        var frame = Assert.Single(animator.Step(50));
        Assert.Null(frame.Bounds);
        Assert.True(frame.Done);
        Assert.Equal(new Rectangle(300, 0, 100, 100), frame.Rest);
    }

    [Fact]
    public void ALeavingWindowEndsHiddenOnItsLayoutRectangle()
    {
        var animator = new Animator();
        var rest = new Rectangle(0, 0, 100, 100);
        animator.Start(
            new Motion(Window, rest with { X = -1920 })
            {
                From = rest,
                Move = Linear,
                FadeTo = 0,
                Fade = Linear,
                AlphaTo = 200,
                End = MotionEnd.Hide,
                Rest = rest,
            },
            now: 0);

        Assert.True(animator.Settle(Window, new Rectangle(0, 0, 50, 50)));
        var halfway = Assert.Single(animator.Step(50));
        var landed = Assert.Single(animator.Step(100));

        Assert.Equal(new Rectangle(-960, 0, 100, 100), halfway.Bounds);
        Assert.Equal((byte)100, halfway.Alpha);
        Assert.True(landed.Done);
        Assert.Equal(MotionEnd.Hide, landed.End);
        Assert.Equal((byte)0, landed.Alpha);
        Assert.Equal(new Rectangle(0, 0, 50, 50), landed.Rest);
        Assert.Equal((byte)200, landed.RestAlpha);
    }

    [Fact]
    public void AFadeMultipliesTheNewestAlpha()
    {
        var animator = new Animator();
        animator.Start(new Motion(Window, new Rectangle(0, 0, 10, 10)) { FadeFrom = 0, Fade = Linear, AlphaTo = 200 }, now: 0);

        Assert.Equal((byte)100, Assert.Single(animator.Step(50)).Alpha);
        Assert.True(animator.Paint(Window, 100, default, now: 50).Alpha);
        Assert.Equal((byte)60, Assert.Single(animator.Step(60)).Alpha);
        var landed = Assert.Single(animator.Step(100));

        Assert.Equal((byte)100, landed.Alpha);
        Assert.Equal((byte)100, landed.RestAlpha);
        Assert.False(animator.Paint(Window, 50, default, now: 120).Alpha);
    }

    [Fact]
    public void AnAlphaChangeMovesTowardANewerAlpha()
    {
        var animator = new Animator();
        animator.Start(new Motion(Window, new Rectangle(0, 0, 10, 10)) { AlphaFrom = 0, AlphaTo = 200, Alpha = Linear }, now: 0);
        Assert.Equal((byte)100, Assert.Single(animator.Step(50)).Alpha);

        animator.Paint(Window, 0, default, now: 50);

        Assert.Equal((byte)50, Assert.Single(animator.Step(100)).Alpha);
        Assert.Equal((byte)0, Assert.Single(animator.Step(150)).Alpha);
    }

    [Fact]
    public void ABorderChangeLandsOnItsColor()
    {
        var animator = new Animator();
        var black = new BorderColor(0xFF, 0, 0, 0);
        var white = new BorderColor(0xFF, 0xFF, 0xFF, 0xFF);
        animator.Start(new Motion(Window, new Rectangle(0, 0, 10, 10)) { BorderFrom = black, BorderTo = white, Border = Linear }, now: 0);

        var middle = Assert.Single(animator.Step(50)).Border!.Value;
        var landed = Assert.Single(animator.Step(100));

        Assert.InRange(middle.R, 1, 254);
        Assert.InRange(Math.Abs(middle.R - middle.G), 0, 1);
        Assert.Equal(white, landed.Border);
        Assert.True(animator.Paint(Window, 255, white, now: 0).Border == false);
    }

    [Fact]
    public void LandingPutsEveryMotionOnItsGoalAtOnce()
    {
        var animator = new Animator();
        animator.Start(new Motion(Window, new Rectangle(100, 0, 100, 100)) { From = new Rectangle(0, 0, 100, 100), Move = Linear, FadeFrom = 0, Fade = Linear }, now: 0);
        animator.Start(new Motion(new WindowId(2), new Rectangle(-5, 0, 1, 1)) { Move = Linear, End = MotionEnd.Close, Rest = new Rectangle(0, 0, 1, 1) }, now: 0);

        var frames = animator.Land();

        var first = Assert.Single(frames, frame => frame.Window == Window);
        Assert.Equal(new Rectangle(100, 0, 100, 100), first.Bounds);
        Assert.Equal((byte)255, first.Alpha);
        Assert.True(first.Done);
        var closing = Assert.Single(frames, frame => frame.Window == new WindowId(2));
        Assert.Equal(MotionEnd.Close, closing.End);
        Assert.Equal(new Rectangle(0, 0, 1, 1), closing.Rest);
        Assert.Equal(0, animator.Count);
    }

    [Fact]
    public void AMotionWithoutTimingsLandsOnTheNextStep()
    {
        var animator = new Animator();
        animator.Start(new Motion(Window, new Rectangle(0, 0, 10, 10)) { End = MotionEnd.Hide }, now: 0);

        var frame = Assert.Single(animator.Step(0));

        Assert.True(frame.Done);
        Assert.Equal(MotionEnd.Hide, frame.End);
        Assert.Null(frame.Bounds);
    }
}
