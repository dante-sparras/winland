namespace Winland.Infrastructure.Tests;

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

    // Only the frame and popup bits are the ones mode 2 changed. A window shown, hidden, disabled for a modal dialog or
    // maximized meanwhile keeps that.
    [Fact]
    public void LeavingPutsBackOnlyTheFrameAndKeepsWhatChangedSince()
    {
        const uint Visible = 0x10000000;
        const uint Disabled = 0x08000000;
        var frames = new FakeFrames();
        var window = new WindowId(9);
        var crashed = new WindowId(10);
        frames.Style[window] = 0x00CF0000 | Visible;
        frames.Style[crashed] = BorderlessStyle.Strip(0x00CF0000) | Disabled;
        var borderless = new BorderlessFrame(frames);
        borderless.Apply(window, true);
        frames.Style[window] = (frames.Style[window] & ~Visible) | Disabled;

        borderless.Apply(window, false);
        borderless.PutBack(crashed, 0x00CF0000 | Visible);

        Assert.Equal(0x00CF0000u | Disabled, frames.Style[window]);
        Assert.Equal(0x00CF0000u | Disabled, frames.Style[crashed]);
    }

    // A style write waits for the window. One whose program stopped answering keeps its frame dropped at exit, and the
    // snapshot records it, so the next start puts it back.
    [Fact]
    public void ReleasingAllSkipsAWindowThatStoppedAnsweringAndKeepsItForTheNextStart()
    {
        var frames = new FakeFrames();
        var hung = new WindowId(4);
        var answering = new WindowId(5);
        frames.Style[hung] = 0x00CF0000;
        frames.Style[answering] = 0x00CF0000;
        var stopped = false;
        var borderless = new BorderlessFrame(frames, window => stopped && window == hung);
        borderless.Apply(hung, true);
        borderless.Apply(answering, true);
        stopped = true;

        borderless.ReleaseAll();

        Assert.Equal(BorderlessStyle.Strip(0x00CF0000), frames.Style[hung]);
        Assert.Equal(0x00CF0000u, frames.Style[answering]);
        var snapshot = borderless.Stamp(new RecoverySnapshot([Client(hung), Client(answering)]));
        Assert.Equal([0x00CF0000u, null], snapshot.Clients.Select(client => client.FrameStyle));
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

    // The exit cleanup puts every frame back, and then the process ends with the state file gone. An
    // outcome from the hook worker or the pipe that arrives after that must not drop a frame again.
    [Fact]
    public void AfterReleasingAllNoFrameIsDropped()
    {
        var frames = new FakeFrames();
        var window = new WindowId(8);
        frames.Style[window] = 0x00CF0000;
        var borderless = new BorderlessFrame(frames);
        borderless.ReleaseAll();

        borderless.Apply(window, true);

        Assert.Equal(0x00CF0000u, frames.Style[window]);
        Assert.False(borderless.Owns(window));
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

    // Only the frame bits go, and the window becomes a popup. Everything else in the style stays.
    [Theory]
    [InlineData(0x00CF0000u, 0x800B0000u)]
    [InlineData(0x10000000u, 0x90000000u)]
    [InlineData(0x80000000u, 0x80000000u)]
    [InlineData(0x16CF0001u, 0x960B0001u)]
    public void StrippingKeepsEveryBitButTheFrame(uint style, uint stripped)
    {
        Assert.Equal(stripped, BorderlessStyle.Strip(style));
    }

    // A window with no frame to drop is still remembered, and nothing is written to it.
    [Fact]
    public void AWindowWithoutAFrameIsRememberedButNotWritten()
    {
        var frames = new FakeFrames();
        var window = new WindowId(2);
        frames.Style[window] = BorderlessStyle.Popup;
        var borderless = new BorderlessFrame(frames);

        borderless.Apply(window, true);

        Assert.Equal(0, frames.Writes);
        Assert.True(borderless.Owns(window));
    }

    [Fact]
    public void AWindowWhoseStyleCannotBeReadIsLeftAlone()
    {
        var frames = new FakeFrames();
        var window = new WindowId(2);
        var borderless = new BorderlessFrame(frames);

        borderless.Apply(window, true);

        Assert.Equal(0, frames.Writes);
        Assert.False(borderless.Owns(window));
    }

    [Fact]
    public void PuttingBackForgetsWhatThisRunDropped()
    {
        var frames = new FakeFrames();
        var window = new WindowId(6);
        frames.Style[window] = 0x00CF0000;
        var borderless = new BorderlessFrame(frames);
        borderless.Apply(window, true);

        borderless.PutBack(window, 0x00C00000);
        var writes = frames.Writes;
        borderless.Release(window);

        Assert.False(borderless.Owns(window));
        Assert.Equal(writes, frames.Writes);

        // The recorded frame, with the system menu and the minimize and maximize boxes the window has now.
        Assert.Equal(0x00CB0000u, frames.Style[window]);
    }

    [Fact]
    public void StampChangesOnlyTheStylesThatAreWrong()
    {
        var frames = new FakeFrames();
        var window = new WindowId(6);
        frames.Style[window] = 0x00CF0000;
        var borderless = new BorderlessFrame(frames);
        borderless.Apply(window, true);
        var right = new RecoverySnapshot(
        [
            new RecoveredClient(window, WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1) { FrameStyle = 0x00CF0000 },
            new RecoveredClient(new WindowId(9), WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1),
        ]);
        var wrong = new RecoverySnapshot(
        [
            new RecoveredClient(new WindowId(10), WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1) { FrameStyle = 1 },
            new RecoveredClient(new WindowId(11), WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1) { FrameStyle = 2 },
        ]);

        var unchanged = borderless.Stamp(right);
        var corrected = borderless.Stamp(wrong);

        Assert.Same(right, unchanged);
        Assert.All(corrected.Clients, client => Assert.Null(client.FrameStyle));
        Assert.Throws<ArgumentNullException>(() => borderless.Stamp(null!));
        Assert.Throws<ArgumentNullException>(() => new BorderlessFrame(null!));
    }

    private static RecoveredClient Client(WindowId window) =>
        new(window, WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1);

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
