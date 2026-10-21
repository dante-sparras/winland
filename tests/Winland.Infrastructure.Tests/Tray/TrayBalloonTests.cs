
namespace Winland.Infrastructure.Tests;

public class TrayBalloonTests
{
    private static ConfigNotice Notice(int generation) =>
        new("Winland: 1 config errors", @"C:\winland\winland.conf:1: Unsupported keyword 'blur'.", @"C:\winland\winland.conf", generation);

    [Fact]
    public void AShownIconWithNoNoticeDrawsTheIconAndNoBalloon() =>
        Assert.Equal(new TrayBalloon.Plan(true, false, false, false), TrayBalloon.For(true, null, 0, 0));

    [Fact]
    public void ANewNoticeOnTheShownIconShowsTheBalloon() =>
        Assert.Equal(new TrayBalloon.Plan(true, false, true, false), TrayBalloon.For(true, Notice(1), 0, 0));

    [Fact]
    public void TheSameGenerationDoesNotShowTheBalloonAgain() =>
        Assert.Equal(new TrayBalloon.Plan(true, false, false, false), TrayBalloon.For(true, Notice(3), 3, 0));

    [Fact]
    public void ADismissedGenerationDoesNotShowTheBalloonAgain() =>
        Assert.Equal(new TrayBalloon.Plan(true, false, false, false), TrayBalloon.For(true, Notice(3), 3, 3));

    [Fact]
    public void ALaterGenerationReplacesTheBalloon() =>
        Assert.Equal(new TrayBalloon.Plan(true, false, true, false), TrayBalloon.For(true, Notice(4), 3, 3));

    [Fact]
    public void ACleanLoadClearsTheBalloonAndKeepsTheIcon() =>
        Assert.Equal(new TrayBalloon.Plan(true, false, false, true), TrayBalloon.For(true, null, 3, 0));

    [Fact]
    public void AHiddenIconAddsATemporaryIconForANewNotice() =>
        Assert.Equal(new TrayBalloon.Plan(true, true, true, false), TrayBalloon.For(false, Notice(1), 0, 0));

    [Fact]
    public void ATemporaryIconStaysUpWithoutShowingTheBalloonAgain() =>
        Assert.Equal(new TrayBalloon.Plan(true, true, false, false), TrayBalloon.For(false, Notice(1), 1, 0));

    [Fact]
    public void ADismissedNoticeOnAHiddenIconRemovesTheTemporaryIcon() =>
        Assert.Equal(new TrayBalloon.Plan(false, false, false, false), TrayBalloon.For(false, Notice(1), 1, 1));

    [Fact]
    public void ACleanLoadOnAHiddenIconRemovesTheTemporaryIcon() =>
        Assert.Equal(new TrayBalloon.Plan(false, false, false, false), TrayBalloon.For(false, null, 1, 0));

    [Fact]
    public void ALaterGenerationAfterDismissShowsATemporaryIconAgain() =>
        Assert.Equal(new TrayBalloon.Plan(true, true, true, false), TrayBalloon.For(false, Notice(2), 1, 1));
}
