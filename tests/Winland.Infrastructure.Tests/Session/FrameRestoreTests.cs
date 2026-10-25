namespace Winland.Infrastructure.Tests;

/// <summary>The frame a fullscreen Client lost is given back when the exit cleanup puts everything back.</summary>
public class FrameRestoreTests
{
    [Fact]
    public void ResettingDecorationsPutsBackTheFrameOfAFullscreenClient()
    {
        using var windows = new TestWindows(reducedMotion: true);
        windows.Open(1);
        var original = windows.Desktop[1].Style;

        windows.Dispatch("fullscreen", "2");
        Assert.NotEqual(original, windows.Desktop[1].Style);

        windows.Adapter.ResetDecorations();

        Assert.Equal(original, windows.Desktop[1].Style);
    }

    // The hook worker and the pipe still apply outcomes while the exit cleanup runs. One that arrives after
    // the frames were put back must not strip a frame again: the process ends next, with the state file gone.
    [Fact]
    public void AnOutcomeAfterTheCleanupDoesNotStripTheFrameAgain()
    {
        using var windows = new TestWindows(reducedMotion: true);
        windows.Open(1);
        var original = windows.Desktop[1].Style;
        windows.Dispatch("fullscreen", "2");
        windows.Dispatch("fullscreen", "2");
        Assert.Equal(original, windows.Desktop[1].Style);

        windows.Adapter.RevealHidden();
        windows.Adapter.ResetDecorations();
        windows.Dispatch("fullscreen", "2");

        Assert.Equal(original, windows.Desktop[1].Style);
    }
}
