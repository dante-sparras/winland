namespace Winland.Infrastructure.Tests;

public class DeclinedWindowsTests
{
    private static readonly WindowId Player = new(1);
    private static readonly WindowTraits Declined = WindowTraits.Normal with { Class = "Player", Title = "Song" };

    private static bool Unchanged(DeclinedWindows declined, bool titleRules = false, int revision = 1, string current = "Player") =>
        declined.Unchanged(Player, titleRules, revision, () => current);

    [Fact]
    public void ADeclinedWindowsNewTitleChangesNothingWhenNoRuleReadsTitles()
    {
        var declined = new DeclinedWindows();
        declined.Note(Player, Declined, revision: 1);

        Assert.True(Unchanged(declined));
    }

    [Fact]
    public void ADeclinedWindowIsOfferedAgainWhenARuleReadsTitles()
    {
        var declined = new DeclinedWindows();
        declined.Note(Player, Declined, revision: 1);

        Assert.False(Unchanged(declined, titleRules: true));
    }

    [Fact]
    public void ADeclinedWindowIsOfferedAgainWhenItsClassChanged()
    {
        var declined = new DeclinedWindows();
        declined.Note(Player, Declined, revision: 1);

        Assert.False(Unchanged(declined, current: "Windows.UI.Core.CoreWindow"));
    }

    [Fact]
    public void AReloadMakesEveryDeclinedWindowOfferedAgain()
    {
        var declined = new DeclinedWindows();
        declined.Note(Player, Declined, revision: 1);

        Assert.False(Unchanged(declined, revision: 2));
    }

    [Fact]
    public void AWindowNobodyDeclinedIsOffered()
    {
        Assert.False(Unchanged(new DeclinedWindows()));
    }

    [Fact]
    public void AWindowWithNoTitleIsOfferedAgainForItsTitleIsWhatItLacks()
    {
        var declined = new DeclinedWindows();
        declined.Note(Player, Declined, revision: 1);
        declined.Note(Player, Declined with { Titled = false, Title = "" }, revision: 1);

        Assert.False(Unchanged(declined));
    }

    [Fact]
    public void AForgottenWindowIsOffered()
    {
        var declined = new DeclinedWindows();
        declined.Note(Player, Declined, revision: 1);
        declined.Forget(Player);

        Assert.False(Unchanged(declined));
    }

    [Fact]
    public void TheClassIsReadOnlyWhenTheWindowCouldStayDeclined()
    {
        var declined = new DeclinedWindows();
        declined.Note(Player, Declined, revision: 1);
        var reads = 0;

        Assert.False(declined.Unchanged(Player, titleRules: true, revision: 1, () => { reads++; return "Player"; }));
        Assert.False(declined.Unchanged(Player, titleRules: false, revision: 2, () => { reads++; return "Player"; }));
        Assert.False(declined.Unchanged(new WindowId(2), titleRules: false, revision: 1, () => { reads++; return "Player"; }));
        Assert.Equal(0, reads);
    }
}
