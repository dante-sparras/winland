using Winland;
using Winland.Windows;

namespace Winland.Tests;

public class BorderlessFrameTests
{
    [Fact]
    public void ModeTwoDropsTheFrameAndLeavingRestoresIt()
    {
        var frames = new FakeFrames();
        var window = new WindowId(9);
        frames.Style[window] = 0x00CF0000;
        var borderless = new BorderlessFrame(frames);

        borderless.Apply(window, true);

        Assert.Equal(BorderlessStyle.Strip(0x00CF0000), frames.Style[window]);
        Assert.True(borderless.Owns(window));
        Assert.Equal(1, frames.Writes);

        borderless.Apply(window, true);
        Assert.Equal(1, frames.Writes);

        borderless.Apply(window, false);
        Assert.Equal(0x00CF0000u, frames.Style[window]);
        Assert.False(borderless.Owns(window));
    }

    [Fact]
    public void AWindowThatCannotChangeStyleIsNotRemembered()
    {
        var frames = new FakeFrames { FailWrite = true };
        var window = new WindowId(3);
        frames.Style[window] = 0x00CF0000;
        var borderless = new BorderlessFrame(frames);

        borderless.Apply(window, true);

        Assert.False(borderless.Owns(window));
        Assert.Equal(0x00CF0000u, frames.Style[window]);
    }

    private sealed class FakeFrames : IWindowFrame
    {
        internal readonly Dictionary<WindowId, uint> Style = [];
        internal int Writes;
        internal bool FailWrite;

        public bool TryReadStyle(WindowId window, out uint style) => Style.TryGetValue(window, out style);

        public bool TryWriteStyle(WindowId window, uint style)
        {
            Writes++;
            if (FailWrite)
                return false;

            Style[window] = style;
            return true;
        }
    }
}
