using Winland.Windows;
using Winland.Core;

namespace Winland.Windows.Tests;

public class TaskbarPolicyTests
{
    [Fact]
    public void TheTaskbarStaysAsItIsWhenTheOptionIsOff()
    {
        var choice = TaskbarPolicy.Decide(hide: false, owns: false, marker: false, open: true);

        Assert.Equal(new TaskbarChoice(false, false, false), choice);
    }

    [Fact]
    public void TurningTheOptionOnHidesTheBars()
    {
        Assert.Equal(
            new TaskbarChoice(Hide: true, Restore: false, Relayout: false),
            TaskbarPolicy.Decide(hide: true, owns: false, marker: false, open: false));
    }

    [Fact]
    public void TurningTheOptionOnLaysTheMonitorsOutOnceTheSessionIsOpen()
    {
        Assert.Equal(
            new TaskbarChoice(Hide: true, Restore: false, Relayout: true),
            TaskbarPolicy.Decide(hide: true, owns: false, marker: false, open: true));
    }

    [Fact]
    public void HidingAgainDoesNothing()
    {
        Assert.Equal(
            new TaskbarChoice(false, false, false),
            TaskbarPolicy.Decide(hide: true, owns: true, marker: true, open: true));
    }

    [Fact]
    public void TurningTheOptionOffRestoresTheBarsAndLaysTheMonitorsOut()
    {
        Assert.Equal(
            new TaskbarChoice(Hide: false, Restore: true, Relayout: true),
            TaskbarPolicy.Decide(hide: false, owns: true, marker: false, open: true));
    }

    [Fact]
    public void ACrashMarkerIsRestoredWhenTheOptionIsOff()
    {
        Assert.Equal(
            new TaskbarChoice(Hide: false, Restore: true, Relayout: false),
            TaskbarPolicy.Decide(hide: false, owns: false, marker: true, open: false));
        Assert.Equal(
            new TaskbarChoice(Hide: false, Restore: true, Relayout: true),
            TaskbarPolicy.Decide(hide: false, owns: false, marker: true, open: true));
    }
}
