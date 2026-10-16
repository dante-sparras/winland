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

    [Fact]
    public void ReleasingAllPutsEveryFrameBack()
    {
        var frames = new FakeFrames();
        var first = new WindowId(4);
        var second = new WindowId(5);
        frames.Style[first] = 0x00CF0000;
        frames.Style[second] = 0x00C40000;
        var borderless = new BorderlessFrame(frames);
        borderless.Apply(first, true);
        borderless.Apply(second, true);

        borderless.ReleaseAll();

        Assert.Equal(0x00CF0000u, frames.Style[first]);
        Assert.Equal(0x00C40000u, frames.Style[second]);
        Assert.False(borderless.Owns(first));
        Assert.False(borderless.Owns(second));
    }

    [Fact]
    public void TheOriginalStyleIsRecordedAndAfterACrashWrittenBackInsteadOfTheStrippedOne()
    {
        var frames = new FakeFrames();
        var window = new WindowId(6);
        var other = new WindowId(7);
        frames.Style[window] = 0x00CF0000;
        var borderless = new BorderlessFrame(frames);
        borderless.Apply(window, true);

        var stamped = borderless.Stamp(new RecoverySnapshot(
        [
            new RecoveredClient(window, WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1),
            new RecoveredClient(other, WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1),
        ]));
        Assert.Equal(0x00CF0000u, stamped.Clients[0].FrameStyle);
        Assert.Null(stamped.Clients[1].FrameStyle);

        var restarted = new BorderlessFrame(frames);
        restarted.PutBack(window, stamped.Clients[0].FrameStyle!.Value);
        Assert.Equal(0x00CF0000u, frames.Style[window]);

        restarted.Apply(window, true);
        restarted.Apply(window, false);
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
