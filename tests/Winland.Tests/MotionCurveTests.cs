using FluentAssertions;
using Winland.Core.Tiling;

namespace Winland.Tests;

[TestClass]
public sealed class MotionCurveTests
{
    [TestMethod]
    public void A_curve_starts_at_nothing_ends_at_all_and_never_goes_back()
    {
        MotionCurve.EaseOutQuint(0).Should().Be(0);
        MotionCurve.EaseOutQuint(1).Should().Be(1);
        var last = 0.0;
        for (var step = 1; step <= 100; step++)
        {
            var value = MotionCurve.EaseOutQuint(step / 100.0);
            value.Should().BeGreaterThanOrEqualTo(last);
            last = value;
        }
    }

    [TestMethod]
    public void Ease_out_quint_covers_most_of_the_way_in_the_first_third()
    {
        MotionCurve.EaseOutQuint(0.33).Should().BeGreaterThan(0.85);
        MotionCurve.EaseOutQuint(0.5).Should().BeGreaterThan(0.95);
    }

    [TestMethod]
    public void A_straight_curve_gives_back_its_progress()
    {
        MotionCurve.Bezier(0, 0, 1, 1, 0.25).Should().BeApproximately(0.25, 0.0001);
        MotionCurve.Bezier(0.5, 0.5, 0.75, 0.75, 0.6).Should().BeApproximately(0.6, 0.0001);
    }

    [TestMethod]
    public void A_window_that_opens_goes_from_clear_to_solid_and_one_that_closes_the_other_way()
    {
        MotionCurve.Alpha(0, MotionCurve.OpenFadeMilliseconds, opening: true).Should().Be(0);
        MotionCurve.Alpha(MotionCurve.OpenFadeMilliseconds, MotionCurve.OpenFadeMilliseconds, opening: true).Should().Be(255);
        MotionCurve.Alpha(5000, MotionCurve.OpenFadeMilliseconds, opening: true).Should().Be(255);
        MotionCurve.Alpha(0, MotionCurve.CloseFadeMilliseconds, opening: false).Should().Be(255);
        MotionCurve.Alpha(MotionCurve.CloseFadeMilliseconds, MotionCurve.CloseFadeMilliseconds, opening: false).Should().Be(0);
        MotionCurve.Alpha(80, MotionCurve.OpenFadeMilliseconds, opening: true).Should().BeInRange(100, 160);
    }
}
