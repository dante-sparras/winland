using Winland.Core;

namespace Winland.Core.Tests;

public class DiscreteScrollTests
{
    [Theory]
    [InlineData(0, 12, false)]
    [InlineData(0, 120, false)]
    [InlineData(1, 12, true)]
    [InlineData(1, -12, true)]
    [InlineData(1, 120, false)]
    [InlineData(1, -240, false)]
    [InlineData(1, 0, false)]
    [InlineData(2, 12, true)]
    [InlineData(2, 120, true)]
    [InlineData(2, -240, true)]
    [InlineData(2, 0, false)]
    public void EachModeEmulatesTheTurnsItNames(int mode, int delta, bool emulated) =>
        Assert.Equal(emulated, DiscreteScroll.Emulates(mode, delta));

    [Fact]
    public void ModeOneIsTheDefaultAndOrdinaryNotchesAreLeftAlone()
    {
        Assert.Equal(1, InputOptions.Documented.EmulateDiscreteScroll);
        Assert.False(DiscreteScroll.Emulates(InputOptions.Documented.EmulateDiscreteScroll, 120));
        Assert.False(DiscreteScroll.Emulates(InputOptions.Documented.EmulateDiscreteScroll, -120));
    }

    [Theory]
    [InlineData("disable", 0)]
    [InlineData("non_standard", 1)]
    [InlineData("force_all", 2)]
    [InlineData("FORCE_ALL", 2)]
    [InlineData("0", 0)]
    [InlineData("2", 2)]
    [InlineData("9", 2)]
    [InlineData("-3", 0)]
    public void TheModeTakesItsNamesAndClampsItsNumber(string value, int mode) =>
        Assert.Equal(mode, InputOptions.From(ConfigParser.Parse($"input:emulate_discrete_scroll = {value}\n")).EmulateDiscreteScroll);

    [Fact]
    public void AnUnreadableModeLeavesTheDefault()
    {
        Assert.Equal(1, InputOptions.From(ConfigParser.Parse("input:emulate_discrete_scroll = sometimes\n")).EmulateDiscreteScroll);
    }

    [Fact]
    public void ASmallTurnsSendsANotchAtOnceThenOneForEveryNotchItAddsUpTo()
    {
        var scroll = new DiscreteScroll();
        var sent = new List<int> { scroll.Emulate(30, false, 1, 1000) };
        for (var i = 1; i <= 10; i++)
            sent.Add(scroll.Emulate(30, false, 1, 1000 + (i * 10)));

        // The first notch is at once. The sum reaches 120 after the fourth turn, so the fifth sends the next, and the ninth the one after.
        Assert.Equal([120, 0, 0, 0, 120, 0, 0, 0, 120, 0, 0], sent);
    }

    [Fact]
    public void AForceAllNotchIsOneNotchAtFactorOne()
    {
        var scroll = new DiscreteScroll();

        Assert.Equal(120, scroll.Emulate(120, false, 1, 1000));
        Assert.Equal(120, scroll.Emulate(120, false, 1, 1010));
        Assert.Equal(120, scroll.Emulate(120, false, 1, 1020));
        Assert.Equal(-120, scroll.Emulate(-120, false, 1, 1030));
    }

    [Fact]
    public void ADoubleFactorSendsANotchForEveryHalfNotch()
    {
        var scroll = new DiscreteScroll();

        Assert.Equal(120, scroll.Emulate(30, false, 2, 1000));
        Assert.Equal(0, scroll.Emulate(30, false, 2, 1010));
        Assert.Equal(120, scroll.Emulate(30, false, 2, 1020));
        Assert.Equal(0, scroll.Emulate(30, false, 2, 1030));
        Assert.Equal(120, scroll.Emulate(30, false, 2, 1040));
    }

    [Fact]
    public void AReversalAnotherAxisOrAPauseStartsAgainWithANotchAtOnce()
    {
        var scroll = new DiscreteScroll();
        _ = scroll.Emulate(30, false, 1, 1000);

        Assert.Equal(-120, scroll.Emulate(-30, false, 1, 1010));
        Assert.Equal(-120, scroll.Emulate(-30, true, 1, 1020));
        Assert.Equal(120, scroll.Emulate(30, true, 1, 1020 + DiscreteScroll.IdleMs + 1));
    }

    [Fact]
    public void AnHorizontalTurnSendsNotchesOfItsOwnSign()
    {
        var scroll = new DiscreteScroll();

        Assert.Equal(-120, scroll.Emulate(-12, true, 1, 1000));
    }

    [Fact]
    public void ANonPositiveFactorFallsBackToANotchInterval()
    {
        var scroll = new DiscreteScroll();
        _ = scroll.Emulate(120, false, 0, 1000);

        Assert.Equal(120, scroll.Emulate(120, false, 0, 1010));
    }
}
