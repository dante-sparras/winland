using Winland;

namespace Winland.Tests;

public class MinimizedConcealmentTests
{
    private static readonly WindowId Kept = new(4);
    private static readonly WindowId Other = new(9);

    [Fact]
    public void MinimizedClientOnAnInactiveWorkspaceIsConcealedOnce()
    {
        var first = MinimizedConcealment.Plan([Inactive(Kept)], []);

        Assert.Equal([Kept], first.Hide);
        Assert.Empty(first.Show);
        Assert.Equal([Kept], first.Concealed);

        var again = MinimizedConcealment.Plan([Inactive(Kept)], first.Concealed);

        Assert.Empty(again.Hide);
        Assert.Empty(again.Show);
        Assert.Equal([Kept], again.Concealed);
    }

    [Fact]
    public void ShowingItsWorkspaceShowsItStillMinimized()
    {
        var away = MinimizedConcealment.Plan([Inactive(Kept)], []);

        var back = MinimizedConcealment.Plan([Active(Kept)], away.Concealed);

        Assert.Empty(back.Hide);
        Assert.Equal([Kept], back.Show);
        Assert.Empty(back.Concealed);
    }

    [Fact]
    public void MinimizedClientOnTheShownWorkspaceIsLeftAlone()
    {
        var plan = MinimizedConcealment.Plan([Active(Kept)], []);

        Assert.Empty(plan.Hide);
        Assert.Empty(plan.Show);
        Assert.Empty(plan.Concealed);
    }

    [Fact]
    public void RestoringDropsTheConcealmentWithoutShowingItMinimized()
    {
        var away = MinimizedConcealment.Plan([Inactive(Kept)], []);

        var restored = MinimizedConcealment.Plan(
            [new MinimizedPresence(Kept, WorkspaceShown: false, Minimized: false)],
            away.Concealed);

        Assert.Empty(restored.Hide);
        Assert.Empty(restored.Show);
        Assert.Empty(restored.Concealed);
    }

    [Fact]
    public void ClosingDropsTheConcealment()
    {
        var away = MinimizedConcealment.Plan([Inactive(Kept), Active(Other)], []);

        var closed = MinimizedConcealment.Plan([Active(Other)], away.Concealed);

        Assert.Empty(closed.Hide);
        Assert.Empty(closed.Show);
        Assert.Empty(closed.Concealed);
    }

    [Fact]
    public void OrdinaryClientIsNotPartOfTheMinimizedPlan()
    {
        var plan = MinimizedConcealment.Plan(
            [new MinimizedPresence(Kept, WorkspaceShown: false, Minimized: false)],
            []);

        Assert.Empty(plan.Hide);
        Assert.Empty(plan.Show);
        Assert.Empty(plan.Concealed);
    }

    private static MinimizedPresence Inactive(WindowId window) =>
        new(window, WorkspaceShown: false, Minimized: true);

    private static MinimizedPresence Active(WindowId window) =>
        new(window, WorkspaceShown: true, Minimized: true);
}
