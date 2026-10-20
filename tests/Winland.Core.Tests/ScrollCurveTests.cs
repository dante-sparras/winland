using Winland;

namespace Winland.Tests;

public class ScrollCurveTests
{
    [Theory]
    [InlineData("custom 0.2 1 2", "0.5 1 2 4", 0, 1)]
    [InlineData("custom 0.2 1 2", "0.5 1 2 4", 0.25, 1.5)]
    [InlineData("custom 0.2 1 2", "0.5 1 2 4", 0.5, 2)]
    [InlineData("custom 0.2 1 2", "0.5 1 2 4", 0.75, 3)]
    [InlineData("custom 0.2 1 2", "0.5 1 2 4", 99, 4)]
    [InlineData("CUSTOM", "1 0 3", 0.5, 1.5)]
    public void TheFactorIsInterpolatedBetweenPointsAndHeldPastTheLast(string profile, string points, double speed, double factor)
    {
        var curve = ScrollCurve.Parse(profile, points);

        Assert.NotNull(curve);
        Assert.Equal(factor, curve.Factor(speed), 9);
    }

    [Theory]
    [InlineData("custom", "")]
    [InlineData("custom", "   ")]
    [InlineData("custom", "0.5")]
    [InlineData("custom", "0.5 1")]
    [InlineData("custom", "0 1 2")]
    [InlineData("custom", "-1 1 2")]
    [InlineData("custom", "0.5 1 -2")]
    [InlineData("custom", "0.5 1 x")]
    [InlineData("flat", "0.5 1 2")]
    [InlineData("adaptive", "0.5 1 2")]
    [InlineData("", "0.5 1 2")]
    [InlineData(null, "0.5 1 2")]
    [InlineData("custom", null)]
    public void NoCurveIsMadeWithoutACustomProfileAndAStepWithTwoFactors(string? profile, string? points)
    {
        Assert.Null(ScrollCurve.Parse(profile, points));
    }

    [Fact]
    public void MoreThanTheMostPointsIsRefused()
    {
        var many = "1 " + string.Join(' ', Enumerable.Repeat("1", ScrollCurve.MaxPoints + 1));
        var most = "1 " + string.Join(' ', Enumerable.Repeat("1", ScrollCurve.MaxPoints));

        Assert.Null(ScrollCurve.Parse("custom", many));
        Assert.NotNull(ScrollCurve.Parse("custom", most));
    }

    [Fact]
    public void TheCurveIsReadFromTheConfig()
    {
        var options = InputOptions.From(ConfigParser.Parse("input:accel_profile = custom 0.2 1 2\ninput:scroll_points = 0.5 1 3\n"));

        Assert.NotNull(options.ScrollCurve);
        Assert.Equal(2, options.ScrollCurve.Factor(0.25), 9);
        Assert.Null(options.Request());
        Assert.Null(InputOptions.Documented.ScrollCurve);
        Assert.Null(InputOptions.From(ConfigParser.Parse("input:scroll_points = 0.5 1 3\n")).ScrollCurve);
        Assert.Null(InputOptions.From(ConfigParser.Parse("input:accel_profile = flat\ninput:scroll_points = 0.5 1 3\n")).ScrollCurve);
    }

    [Fact]
    public void ACustomProfileWithPointsStillWarnsThatMotionIsNotCurved()
    {
        var document = ConfigParser.Parse("input:accel_profile = custom 0.2 1 2\n");

        Assert.Equal(new ConfigWarning(1, "Custom acceleration profiles are not honored."), Assert.Single(document.Warnings));
    }

    [Fact]
    public void TheFirstTurnOfARunIsAtSpeedZero()
    {
        var curve = ScrollCurve.Parse("custom", "1 2 2");
        var accelerator = new ScrollAccelerator();

        Assert.Equal(240, accelerator.Accelerate(curve!, 120, false, 1000));
    }

    [Fact]
    public void AFasterTurnGetsTheFactorOfItsSpeed()
    {
        // 120 in 10 ms is 1.5 units per millisecond, which is the third point at a step of 0.75.
        var curve = ScrollCurve.Parse("custom", "0.75 1 2 5");
        var accelerator = new ScrollAccelerator();

        Assert.Equal(120, accelerator.Accelerate(curve!, 120, false, 1000));
        Assert.Equal(600, accelerator.Accelerate(curve!, 120, false, 1010));
    }

    [Fact]
    public void APauseOrAReversalStartsANewRun()
    {
        var curve = ScrollCurve.Parse("custom", "0.75 1 2 5");
        var accelerator = new ScrollAccelerator();
        _ = accelerator.Accelerate(curve!, 120, false, 1000);

        Assert.Equal(120, accelerator.Accelerate(curve!, 120, false, 1000 + ScrollAccelerator.IdleMs + 1));
        Assert.Equal(-120, accelerator.Accelerate(curve!, -120, false, 1000 + ScrollAccelerator.IdleMs + 11));
    }

    [Fact]
    public void TheAxesKeepSeparateRuns()
    {
        var curve = ScrollCurve.Parse("custom", "0.75 1 2 5");
        var accelerator = new ScrollAccelerator();
        _ = accelerator.Accelerate(curve!, 120, false, 1000);

        Assert.Equal(120, accelerator.Accelerate(curve!, 120, true, 1010));
    }

    [Fact]
    public void WhatRoundingLeavesGoesToTheNextTurn()
    {
        var curve = ScrollCurve.Parse("custom", "1 0.5 0.5");
        var accelerator = new ScrollAccelerator();

        // 30 at 0.5 is 15 each, so no remainder; 1 at 0.5 is 0.5, which rounds away from zero then back.
        Assert.Equal(15, accelerator.Accelerate(curve!, 30, false, 1000));
        var total = 0;
        for (var i = 0; i < 10; i++)
            total += accelerator.Accelerate(curve!, 1, false, 1001 + i);

        Assert.Equal(5, total);
    }

    [Fact]
    public void ACurveThatZeroesTheSpeedSendsNothing()
    {
        var curve = ScrollCurve.Parse("custom", "1 0 0");
        var accelerator = new ScrollAccelerator();

        Assert.Equal(0, accelerator.Accelerate(curve!, 120, false, 1000));
    }
}
