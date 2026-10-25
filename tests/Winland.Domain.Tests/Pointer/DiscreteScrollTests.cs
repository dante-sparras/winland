namespace Winland.Domain.Tests;

public class DiscreteScrollTests
{
    [Theory]
    [InlineData(12, true)]
    [InlineData(-12, true)]
    [InlineData(120, false)]
    [InlineData(-240, false)]
    [InlineData(0, false)]
    public void OnlyTurnsThatAreNotWholeNotchesAreEmulated(int delta, bool emulated) =>
        Assert.Equal(emulated, DiscreteScroll.Emulates(delta));

    [Fact]
    public void ASmallTurnsSendsANotchAtOnceThenOneForEveryNotchItAddsUpTo()
    {
        var scroll = new DiscreteScroll();
        var sent = new List<int> { scroll.Emulate(30, false, 1000) };
        for (var i = 1; i <= 10; i++)
            sent.Add(scroll.Emulate(30, false, 1000 + (i * 10)));

        // The first notch is at once. The sum reaches 120 after the fourth turn, so the fifth sends the next, and the ninth the one after.
        Assert.Equal([120, 0, 0, 0, 120, 0, 0, 0, 120, 0, 0], sent);
    }

    [Fact]
    public void AWholeNotchIsOneNotch()
    {
        var scroll = new DiscreteScroll();

        Assert.Equal(120, scroll.Emulate(120, false, 1000));
        Assert.Equal(120, scroll.Emulate(120, false, 1010));
        Assert.Equal(120, scroll.Emulate(120, false, 1020));
        Assert.Equal(-120, scroll.Emulate(-120, false, 1030));
    }

    [Fact]
    public void AReversalAnotherAxisOrAPauseStartsAgainWithANotchAtOnce()
    {
        var scroll = new DiscreteScroll();
        _ = scroll.Emulate(30, false, 1000);

        Assert.Equal(-120, scroll.Emulate(-30, false, 1010));
        Assert.Equal(-120, scroll.Emulate(-30, true, 1020));
        Assert.Equal(120, scroll.Emulate(30, true, 1020 + DiscreteScroll.IdleMs + 1));
    }

    [Fact]
    public void AnHorizontalTurnSendsNotchesOfItsOwnSign()
    {
        var scroll = new DiscreteScroll();

        Assert.Equal(-120, scroll.Emulate(-12, true, 1000));
    }
}
