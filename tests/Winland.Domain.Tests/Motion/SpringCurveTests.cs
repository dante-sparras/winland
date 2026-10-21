
namespace Winland.Domain.Tests;

public class SpringCurveTests
{
    [Theory]
    [InlineData(70, 10, 1)]
    [InlineData(238.1191, 24.21279333, 1)]
    [InlineData(100, 20, 1)]
    [InlineData(100, 60, 1)]
    public void ItStartsAtZeroAndEndsAtOne(double stiffness, double damping, double mass)
    {
        var spring = new SpringCurve(stiffness, damping, mass);

        Assert.Equal(0, spring.Ease(0));
        Assert.Equal(1, spring.Ease(1));
        Assert.InRange(spring.Milliseconds, 1, 60_000);
        Assert.InRange(spring.Ease(0.999), 1 - SpringCurve.Epsilon - 1e-4, 1 + SpringCurve.Epsilon + 1e-4);
    }

    [Theory]
    [InlineData(70, 10, 1)]
    [InlineData(100, 20, 1)]
    [InlineData(100, 60, 2)]
    public void TheClosedFormMatchesTheSpringSteppedInSmallSteps(double stiffness, double damping, double mass)
    {
        var spring = new SpringCurve(stiffness, damping, mass);
        var (value, velocity) = (0.0, 0.0);
        const double Step = 1e-5;
        var seconds = 0.0;
        foreach (var fraction in (double[])[0.05, 0.1, 0.25, 0.5])
        {
            var until = fraction * spring.Milliseconds / 1000;
            for (; seconds < until; seconds += Step)
            {
                var acceleration = ((stiffness * (1 - value)) - (damping * velocity)) / mass;
                velocity += acceleration * Step;
                value += velocity * Step;
            }

            Assert.Equal(value, spring.Ease(seconds / (spring.Milliseconds / 1000)), 0.001);
        }
    }

    [Fact]
    public void AnUnderdampedSpringOvershootsAndAnOverdampedOneDoesNot()
    {
        var bouncy = new SpringCurve(100, 2, 1);
        var slow = new SpringCurve(100, 60, 1);

        Assert.Contains(Enumerable.Range(1, 99), step => bouncy.Ease(step / 100.0) > 1);
        Assert.DoesNotContain(Enumerable.Range(1, 99), step => slow.Ease(step / 100.0) > 1);
        Assert.True(slow.Milliseconds > new SpringCurve(100, 20, 1).Milliseconds);
    }

    [Fact]
    public void AnAnimationOnASpringTakesTheSpringsTimeWhateverItsSpeed()
    {
        var options = AnimationOptions.From(ConfigParser.Parse("""
            spring = rubber, 70, 10, 1
            animation = windows, 1, 3, spring:rubber
            animation = fade, 1, 30, spring:rubber
            """));
        var spring = new SpringCurve(70, 10, 1);

        Assert.Equal(new Timing(spring, spring.Milliseconds), options.TimingOf("windowsMove"));
        Assert.Equal(new Timing(spring, spring.Milliseconds), options.TimingOf("fadeIn"));
        Assert.Equal("spring:rubber", options.Leaf("windows").Curve);
    }

    [Theory]
    [InlineData("spring = soft, 0.4, 10, 1", "Spring stiffness must be above 0.5.")]
    [InlineData("spring = soft, 70, 0.5, 1", "Spring damping must be above 0.5.")]
    [InlineData("spring = soft, 70, 10, wet", "Spring mass 'wet' is not a number.")]
    [InlineData("spring = soft, 70, 10, 1, 2", "Spring has too many arguments.")]
    [InlineData("animation = windows, 1, 3, spring:nosuch", "Curve 'spring:nosuch' does not exist.")]
    public void ABadSpringOrAnUnknownOneWarns(string line, string warning)
    {
        var document = ConfigParser.Parse(line + "\n");

        Assert.Equal(warning, Assert.Single(document.Warnings).Message);
    }
}
