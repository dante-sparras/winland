namespace Winland.Infrastructure.Tests;

/// <summary>
/// A window whose size changes during a motion is drawn by a stand-in until the motion ends. The adapter decides
/// what the stand-in shows and when, and the fake desktop records it.
/// </summary>
public class StandInTests
{
    private const string Fast = "animation = global, 1, 1, linear\n";
    private const uint Layered = 0x00080000;

    // Two tiles, then the second closes and the first grows to fill the screen.
    private static TestWindows Grow(out Rectangle before, Action<FakeDesktop>? arrange = null)
    {
        var windows = new TestWindows(Fast);
        windows.Open(1);
        var second = windows.Open(2);
        windows.Settle();
        before = windows.Desktop[1].Visible;
        arrange?.Invoke(windows.Desktop);
        windows.Desktop.Calls.Clear();
        windows.Apply(windows.Core.Handle(new WindowClosed(second)));
        return windows;
    }

    private static int Posted(TestWindows windows) =>
        windows.Desktop.Calls.Count(call => call.StartsWith("bounds 1 ", StringComparison.Ordinal) && call.EndsWith(" Post", StringComparison.Ordinal));

    [Fact]
    public void AGrowingWindowShowsOnlyWhatItPaintedUntilItHasPaintedTheRest()
    {
        using var windows = Grow(out var before);
        var standIn = windows.Desktop.StandInOf(1);

        windows.Advance(16);
        Assert.True(standIn.Picture!.Value.Source.Width <= before.Width);
        Assert.NotNull(standIn.Right);

        windows.Advance(32);
        Assert.True(standIn.Picture!.Value.Source.Width <= before.Width);
        Assert.NotNull(standIn.Right);

        windows.Advance(16);
        Assert.True(standIn.Picture!.Value.Source.Width > before.Width);
        Assert.Null(standIn.Right);
    }

    [Fact]
    public void AGrowingWindowThatSaysItIsStillPaintingStaysCutOffPast50Milliseconds()
    {
        using var windows = Grow(out var before, desktop => desktop.PaintTime = 1000);
        var standIn = windows.Desktop.StandInOf(1);

        windows.Advance(32);
        windows.Advance(32);

        Assert.True(standIn.Picture!.Value.Source.Width <= before.Width);
        Assert.NotNull(standIn.Right);
    }

    [Fact]
    public void AStandInIsPlacedBeforeItsPictureIsFitted()
    {
        using var windows = Grow(out _);
        windows.Settle();
        var calls = windows.Desktop.Calls;

        var fits = 0;
        for (var i = 0; i < calls.Count; i++)
        {
            if (!calls[i].StartsWith("standin show 1 Picture", StringComparison.Ordinal))
                continue;

            Assert.StartsWith("standin place 1 ", calls[i - 1], StringComparison.Ordinal);
            fits++;
        }

        Assert.True(fits > 3, $"{fits} frames were fitted.");
    }

    [Fact]
    public void AWindowThatShrinksKeepsItsLargerSizeUntilTheMotionEnds()
    {
        using var windows = new TestWindows(Fast);
        windows.Open(1);
        windows.Settle();
        var shrinking = windows.Desktop[1];
        var wide = shrinking.Visible.Width;

        windows.Open(2);
        windows.Advance(32);
        windows.Advance(32);
        Assert.Equal(wide, shrinking.Visible.Width);

        windows.Settle();
        Assert.True(shrinking.Visible.Width < wide);
    }

    [Fact]
    public void AWindowThatChangesSizeIsGivenItsSizeOnceAndNotOnEveryFrame()
    {
        using var windows = new TestWindows(Fast);
        windows.Open(1);
        windows.Settle();
        windows.Desktop.Calls.Clear();

        windows.Open(2);
        windows.Settle();

        var calls = windows.Desktop.Calls;
        Assert.Equal(1, Posted(windows));
        var placed = calls.FindIndex(call => call.StartsWith("bounds 1 ", StringComparison.Ordinal) && call.EndsWith(" Wait", StringComparison.Ordinal));
        Assert.True(placed > 0, "The window was not placed.");
        Assert.True(calls.IndexOf("standin close 1") > placed);
    }

    [Fact]
    public void AStandInGivesTheWindowItsBorderAndItsAlphaBackWhenTheMotionEnds()
    {
        using var windows = Grow(out _, desktop => desktop[1].Attributes[34] = 0x00112233);
        var window = windows.Desktop[1];
        Assert.Equal(0, window.Alpha);
        Assert.Equal(0xFFFFFFFEu, window.Attributes[34]);

        windows.Settle();

        Assert.True(windows.Desktop.StandInOf(1).Closed);
        Assert.Equal(0u, window.ExStyle & Layered);
        Assert.Equal(0x00112233u, window.Attributes[34]);
    }

    [Fact]
    public void AWindowItsProgramMadeLayeredIsResizedOnEveryFrameInsteadOfStoodInFor()
    {
        using var windows = Grow(out _, desktop => desktop[1].ExStyle = Layered);
        Assert.True(windows.Desktop.StandInOf(1).Closed);
        windows.Desktop.Calls.Clear();

        windows.Advance(16);
        windows.Advance(16);
        windows.Advance(16);

        Assert.True(Posted(windows) >= 3, $"{Posted(windows)} frames were sent.");
    }

    [Fact]
    public void AWindowWithNoPictureToShowIsResizedOnEveryFrame()
    {
        using var windows = Grow(out _, desktop => desktop.CanStandIn = false);
        Assert.DoesNotContain("standin open 1", windows.Desktop.Calls);
        windows.Desktop.Calls.Clear();

        windows.Advance(16);
        windows.Advance(16);
        windows.Advance(16);

        Assert.True(Posted(windows) >= 3, $"{Posted(windows)} frames were sent.");
    }

    [Fact]
    public void AClosingWindowsStandInHidesBeforeTheWindowIsAskedToCloseAndEndsIfItStays()
    {
        using var windows = new TestWindows(Fast);
        windows.Open(1);
        windows.Settle();
        windows.Desktop.Calls.Clear();

        windows.Dispatch("killactive");
        windows.Settle();

        var desktop = windows.Desktop;
        var hidden = desktop.IndexOf("standin hide 1");
        Assert.True(hidden >= 0, "The stand-in was not hidden.");
        Assert.True(hidden < desktop.IndexOf("close 1"));
        Assert.True(desktop.IndexOf("standin close 1") > desktop.IndexOf("close 1"));
        Assert.True(windows.Desktop.StandInOf(1).Closed);
    }
}
