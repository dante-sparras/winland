using Winland;

namespace Winland.Windows.Tests;

public class DisplayOrientationTests
{
    [Fact]
    public void MatchingOrientationDoesNotChangeTheDisplay()
    {
        var source = new FakeModes(new DisplayMode(1920, 1080, DisplayOrientation.Identity));

        var applied = DisplayOrientationAdapter.TryApply(
            new DisplayOrientationRequest(@"\\.\DISPLAY1", DisplayOrientation.Identity),
            source);

        Assert.True(applied);
        Assert.Empty(source.Commits);
    }

    [Fact]
    public void RotationBetweenAxesSwapsTheReportedSize()
    {
        var source = new FakeModes(new DisplayMode(1920, 1080, DisplayOrientation.Identity));

        var applied = DisplayOrientationAdapter.TryApply(
            new DisplayOrientationRequest(@"\\.\DISPLAY1", DisplayOrientation.Rotate90),
            source);

        Assert.True(applied);
        Assert.Equal(new DisplayMode(1080, 1920, DisplayOrientation.Rotate90), Assert.Single(source.Commits));
    }

    [Fact]
    public void RotationThatStaysOnTheSameAxisKeepsTheSize()
    {
        var source = new FakeModes(new DisplayMode(1080, 1920, DisplayOrientation.Rotate90));

        var applied = DisplayOrientationAdapter.TryApply(
            new DisplayOrientationRequest(@"\\.\DISPLAY1", DisplayOrientation.Rotate270),
            source);

        Assert.True(applied);
        Assert.Equal(new DisplayMode(1080, 1920, DisplayOrientation.Rotate270), Assert.Single(source.Commits));
    }

    [Fact]
    public void AMissingDisplayIsLeftAlone()
    {
        var source = new FakeModes(new DisplayMode(1920, 1080, DisplayOrientation.Identity)) { CanRead = false };

        var applied = DisplayOrientationAdapter.TryApply(
            new DisplayOrientationRequest(@"\\.\DISPLAY1", DisplayOrientation.Rotate180),
            source);

        Assert.False(applied);
        Assert.Empty(source.Commits);
    }

    private sealed class FakeModes(DisplayMode current) : IDisplayModeSource
    {
        public bool CanRead { get; init; } = true;

        public List<DisplayMode> Commits { get; } = [];

        public bool TryRead(string device, out DisplayMode mode)
        {
            mode = current;
            return CanRead && device.Length > 0;
        }

        public bool TryCommit(string device, DisplayMode mode)
        {
            Commits.Add(mode);
            return true;
        }
    }
}
