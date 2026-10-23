namespace Winland.Infrastructure.Tests;

public class PresentClockTests
{
    [Fact]
    public void AVblankThatPassedGivesTheOneAfterNow() => Assert.Equal(130, PresentClock.Next(125, 100, 10));

    [Fact]
    public void AComingVblankIsTheNextOne() => Assert.Equal(100, PresentClock.Next(94, 100, 10));

    [Fact]
    public void NowOnAVblankGivesTheFollowingOne() => Assert.Equal(110, PresentClock.Next(100, 100, 10));

    [Fact]
    public void WithoutTimingItIsNow() => Assert.Equal(94, PresentClock.Next(94, 0, 10));
}
