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

    [Fact]
    public void ApplyingAnOutcomeCommitsItsRotations()
    {
        var rotated = new List<DisplayOrientationRequest>();
        var system = WindowAdapter.Rotate;
        WindowAdapter.Rotate = request =>
        {
            rotated.Add(request);
            return true;
        };
        try
        {
            var request = new DisplayOrientationRequest(@"\\.\DISPLAY9", DisplayOrientation.Rotate90);
            WindowAdapter.Apply(new Outcome([], new State([])) { Orientations = [request] });

            Assert.Equal([request], rotated);
        }
        finally
        {
            WindowAdapter.Rotate = system;
        }
    }

    // Every request the core puts in an outcome must reach the system. A new field fails here until
    // someone decides where it is applied and adds it to this list.
    [Fact]
    public void EveryOutcomeFieldHasAKnownConsumer()
    {
        string[] known =
        [
            nameof(Outcome.Placements), // WindowAdapter.ApplyCore
            nameof(Outcome.State), // WindowAdapter.ApplyCore, SessionEffects, queries
            nameof(Outcome.Consumed), // the hooks swallow consumed input
            nameof(Outcome.Dispatcher), // events and command replies
            nameof(Outcome.Closes), // WindowAdapter.ApplyCore
            nameof(Outcome.Close), // shorthand for Closes
            nameof(Outcome.Focus), // WindowAdapter.ApplyCore
            nameof(Outcome.Show), // WindowAdapter.ApplyCore
            nameof(Outcome.Hide), // WindowAdapter.ApplyCore
            nameof(Outcome.ShowMinimized), // WindowAdapter.ApplyCore
            nameof(Outcome.Launches), // WindowAdapter.ApplyCore
            nameof(Outcome.Launch), // shorthand for Launches
            nameof(Outcome.Events), // Core.Noticed, the event stream
            nameof(Outcome.GestureCalls), // Core.Noticed, LuaRuntime.Listen
            nameof(Outcome.Decorations), // WindowAdapter.ApplyCore
            nameof(Outcome.Motions), // WindowAdapter.ApplyCore
            nameof(Outcome.Live), // WindowAdapter.ApplyCore
            nameof(Outcome.ResetDecorations), // WindowAdapter.ApplyCore
            nameof(Outcome.GroupBars), // WindowAdapter.ApplyCore
            nameof(Outcome.ErrorBar), // WindowAdapter.ApplyCore
            nameof(Outcome.Tray), // WindowAdapter.ApplyCore, TrayHost
            nameof(Outcome.Orientations), // WindowAdapter.ApplyCore
            nameof(Outcome.DisplayModes), // WindowAdapter.ApplyCore
            nameof(Outcome.DisplayScales), // WindowAdapter.ApplyCore
            nameof(Outcome.DisplayColors), // SessionEffects
            nameof(Outcome.DisplayMirrors), // SessionEffects
            nameof(Outcome.Warp), // SessionEffects
            nameof(Outcome.InputSettings), // SessionEffects
            nameof(Outcome.Vrr), // SessionEffects
            nameof(Outcome.KeyboardLayout), // SessionEffects
            nameof(Outcome.Cursor), // SessionEffects
            nameof(Outcome.ResizeCursor), // not applied: behavior.md says the system pointer is not changed
            nameof(Outcome.Error), // command replies
            nameof(Outcome.Kills), // WindowAdapter.ApplyCore
            nameof(Outcome.Kill), // shorthand for Kills
            nameof(Outcome.Exit), // WindowAdapter.ApplyCore
            nameof(Outcome.DisplayPower), // WindowAdapter.ApplyCore
            nameof(Outcome.Relays), // SessionEffects
            nameof(Outcome.ZOrder), // WindowAdapter.ApplyCore
            nameof(Outcome.Notice), // command replies
            nameof(Outcome.Order), // WindowAdapter.Apply through ApplyOrder
        ];

        var fields = typeof(Outcome)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(known.Order(StringComparer.Ordinal), fields);
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
