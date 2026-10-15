using Winland;
using Winland.Windows;

namespace Winland.Tests;

public class OpacityAdapterTests
{
    [Fact]
    public void AlphaBelowOpaqueAddsLayeredAndResetRestoresTheOriginalStyle()
    {
        var styles = new FakeStyles();
        var window = new WindowId(42);
        styles.ExStyle[window] = 0x80;
        var adapter = new OpacityAdapter(styles);

        adapter.Apply(window, 255);
        Assert.Equal(0, styles.Writes);
        Assert.False(adapter.Owns(window));

        adapter.Apply(window, 128);
        Assert.Equal(OpacityAdapter.LayeredStyle | 0x80, styles.ExStyle[window]);
        Assert.Equal(128, styles.Alpha[window]);
        Assert.Equal(1, styles.Writes);
        Assert.True(adapter.Owns(window));

        styles.ExStyle[window] = OpacityAdapter.LayeredStyle;
        adapter.Apply(window, 64);
        Assert.Equal(OpacityAdapter.LayeredStyle, styles.ExStyle[window]);
        Assert.Equal(64, styles.Alpha[window]);
        Assert.Equal(1, styles.Writes);

        adapter.Reset(window);
        Assert.Equal(0x80u, styles.ExStyle[window]);
        Assert.False(adapter.Owns(window));
    }

    [Fact]
    public void ReturningToOpaqueRestoresTheStyleWithoutASeparateReset()
    {
        var styles = new FakeStyles();
        var window = new WindowId(7);
        styles.ExStyle[window] = 0x80;
        var adapter = new OpacityAdapter(styles);

        adapter.Apply(window, 100);
        adapter.Apply(window, 255);

        Assert.Equal(0x80u, styles.ExStyle[window]);
        Assert.False(adapter.Owns(window));
    }

    [Fact]
    public void AnAlreadyLayeredWindowIsLeftAloneAndReportedOnce()
    {
        var styles = new FakeStyles();
        var window = new WindowId(42);
        styles.ExStyle[window] = OpacityAdapter.LayeredStyle | 0x80;
        styles.Alpha[window] = 200;
        styles.ColorKey.Add(window);
        var adapter = new OpacityAdapter(styles);

        adapter.Apply(window, 10);
        adapter.Apply(window, 10);

        Assert.Equal(200, styles.Alpha[window]);
        Assert.Equal(0, styles.Writes);
        Assert.Equal(0, styles.AlphaWrites);
        Assert.False(adapter.Owns(window));
        var report = Assert.Single(adapter.Reports);
        Assert.Equal("Window 0x2a was already layered and was left alone.", report);
    }

    [Fact]
    public void AWindowThatCannotBeLayeredIsNotTriedAgain()
    {
        var styles = new FakeStyles { FailAlpha = true };
        var window = new WindowId(3);
        styles.ExStyle[window] = 0;
        var adapter = new OpacityAdapter(styles);

        adapter.Apply(window, 128);
        var writes = styles.Writes;
        var alphaWrites = styles.AlphaWrites;
        adapter.Apply(window, 128);

        Assert.Equal(writes, styles.Writes);
        Assert.Equal(alphaWrites, styles.AlphaWrites);
        Assert.Equal(0u, styles.ExStyle[window]);
        Assert.False(adapter.Owns(window));
        Assert.True(adapter.Failed(window));
        Assert.Equal("Window 0x3 could not be layered.", Assert.Single(adapter.Reports));
    }

    [Fact]
    public void StampRecordsAddedLayeredWindowsAndReleaseClearsTheBit()
    {
        var styles = new FakeStyles();
        var window = new WindowId(5);
        styles.ExStyle[window] = 0x80;
        var adapter = new OpacityAdapter(styles);
        adapter.Apply(window, 128);

        var snapshot = new RecoverySnapshot(
        [
            new RecoveredClient(window, WorkspaceId.Initial, MonitorId.Primary, false, "Notepad", "Notes", 10),
        ]);
        var stamped = adapter.Stamp(snapshot);
        Assert.True(Assert.Single(stamped.Clients).Layered);

        var restarted = new OpacityAdapter(styles);
        restarted.ReleaseAdded(window);
        Assert.Equal(0x80u, styles.ExStyle[window]);
    }

    [Fact]
    public void ResetAllRestoresEveryOwnedWindow()
    {
        var styles = new FakeStyles();
        var first = new WindowId(1);
        var second = new WindowId(2);
        styles.ExStyle[first] = 0x80;
        styles.ExStyle[second] = 0x80;
        var adapter = new OpacityAdapter(styles);
        adapter.Apply(first, 128);
        adapter.Apply(second, 64);

        adapter.ResetAll();

        Assert.Equal(0x80u, styles.ExStyle[first]);
        Assert.Equal(0x80u, styles.ExStyle[second]);
        Assert.False(adapter.Owns(first));
        Assert.False(adapter.Owns(second));
    }

    private sealed class FakeStyles : IWindowStyles
    {
        public Dictionary<WindowId, uint> ExStyle { get; } = [];

        public Dictionary<WindowId, byte> Alpha { get; } = [];

        public HashSet<WindowId> ColorKey { get; } = [];

        public bool FailAlpha { get; init; }

        public int Writes { get; private set; }

        public int AlphaWrites { get; private set; }

        public bool TryRead(WindowId window, out WindowChrome chrome)
        {
            chrome = default;
            if (!ExStyle.TryGetValue(window, out var style))
                return false;

            chrome = new WindowChrome(style, ColorKey.Contains(window), Alpha.GetValueOrDefault(window));
            return true;
        }

        public bool TryWriteExStyle(WindowId window, uint exStyle)
        {
            Writes++;
            ExStyle[window] = exStyle;
            return true;
        }

        public bool TrySetAlpha(WindowId window, byte alpha)
        {
            AlphaWrites++;
            if (FailAlpha)
                return false;

            Alpha[window] = alpha;
            return true;
        }
    }
}
