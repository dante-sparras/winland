using Winland.Core;

namespace Winland.Core.Tests;

public class WorkspaceGapTests
{
    private static readonly Rectangle Screen = new(0, 0, 1000, 500);

    [Fact]
    public void ASlideTravelsTheMonitorAndTheGapBetweenWorkspaces()
    {
        Assert.Equal(new WorkspaceShift(1020, 0, false), MotionStyles.Workspace("slide", entering: true, left: true, special: false, Screen, gap: 20));
        Assert.Equal(new WorkspaceShift(0, -260, false), MotionStyles.Workspace("slidevert 50%", entering: true, left: false, special: false, Screen, gap: 20));
        Assert.Equal(new WorkspaceShift(1000, 0, true), MotionStyles.Workspace("slidefade", entering: true, left: true, special: false, Screen, gap: 20));
    }

    [Fact]
    public void TheGapIsReadClampedAndScaled()
    {
        Assert.Equal(40, LayoutOptions.From(ConfigParser.Parse("general:gaps_workspaces = 40\n")).GapsWorkspaces);
        Assert.Equal(100, LayoutOptions.From(ConfigParser.Parse("general:gaps_workspaces = 400\n")).GapsWorkspaces);
        Assert.Equal(60, LayoutOptions.From(ConfigParser.Parse("general:gaps_workspaces = 40\n")).ScaledBy(1.5).GapsWorkspaces);
        Assert.Empty(ConfigParser.Parse("general:gaps_workspaces = 40\n").Warnings);
    }
}
