namespace Winland.Infrastructure.Tests;

[Collection("Console")]
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

        var stderr = Console.Error;
        using var log = new StringWriter();
        Console.SetError(log);
        try
        {
            adapter.Apply(window, 10);
            adapter.Apply(window, 10);
            Assert.Equal("Window 0x2a was already layered and was left alone." + Environment.NewLine, log.ToString());
        }
        finally
        {
            Console.SetError(stderr);
        }

        Assert.Equal(200, styles.Alpha[window]);
        Assert.Equal(0, styles.Writes);
        Assert.Equal(0, styles.AlphaWrites);
        Assert.False(adapter.Owns(window));
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

    // A style write waits for the window. One whose program stopped answering is given full alpha, which does not wait,
    // and stays layered and recorded, so the next start takes the style off.
    [Fact]
    public void ResetAllLeavesAWindowThatStoppedAnsweringOpaqueAndRecorded()
    {
        var styles = new FakeStyles();
        var window = new WindowId(5);
        styles.ExStyle[window] = 0x80;
        var stopped = false;
        var adapter = new OpacityAdapter(styles, _ => stopped);
        adapter.Apply(window, 128);
        stopped = true;
        var writes = styles.Writes;

        adapter.ResetAll();

        Assert.Equal(writes, styles.Writes);
        Assert.Equal(255, styles.Alpha[window]);
        var clean = new RecoverySnapshot(
        [
            new RecoveredClient(window, WorkspaceId.Initial, MonitorId.Primary, false, "Notepad", "Notes", 10),
        ]) { Clean = true };
        var stamped = adapter.Stamp(clean);
        Assert.True(Assert.Single(stamped.Clients).Layered);
        Assert.True(stamped.Clean);
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

    // The exit cleanup resets every window, and then the process ends with the state file gone. An
    // outcome or a motion that arrives after that must not make a window layered again.
    [Fact]
    public void AfterResetAllNoWindowIsMadeLayered()
    {
        var styles = new FakeStyles();
        var window = new WindowId(3);
        styles.ExStyle[window] = 0x80;
        var adapter = new OpacityAdapter(styles);
        adapter.ResetAll();

        adapter.Apply(window, 128);

        Assert.Equal(0x80u, styles.ExStyle[window]);
        Assert.False(adapter.Owns(window));
    }

    [Fact]
    public void AWindowThatRefusesTheLayeredStyleIsNotOwned()
    {
        var styles = new FakeStyles { FailStyle = true };
        var window = new WindowId(4);
        styles.ExStyle[window] = 0x80;
        var adapter = new OpacityAdapter(styles);

        adapter.Apply(window, 128);

        Assert.False(adapter.Owns(window));
        Assert.True(adapter.Failed(window));
        Assert.Equal(0, styles.AlphaWrites);
    }

    // A reset, as when a window opens again, lets a window that failed or was left alone be tried again.
    [Fact]
    public void AResetForgetsAFailureAndAWindowLeftAlone()
    {
        var styles = new FakeStyles { FailStyle = true };
        var failed = new WindowId(4);
        var layered = new WindowId(5);
        styles.ExStyle[failed] = 0x80;
        styles.ExStyle[layered] = OpacityAdapter.LayeredStyle;
        var adapter = new OpacityAdapter(styles);
        adapter.Apply(failed, 128);
        adapter.Apply(layered, 128);

        adapter.Reset(failed);
        adapter.Reset(layered);
        styles.FailStyle = false;
        styles.ExStyle[layered] = 0x80;
        adapter.Apply(failed, 128);
        adapter.Apply(layered, 128);

        Assert.False(adapter.Failed(failed));
        Assert.True(adapter.Owns(failed));
        Assert.True(adapter.Owns(layered));
    }

    // The window was not layered before, so putting its style back does not set an alpha.
    [Fact]
    public void RestoringTheStyleSetsNoAlpha()
    {
        var styles = new FakeStyles();
        var window = new WindowId(6);
        styles.ExStyle[window] = 0x80;
        var adapter = new OpacityAdapter(styles);
        adapter.Apply(window, 128);
        var alphaWrites = styles.AlphaWrites;

        adapter.Reset(window);

        Assert.Equal(alphaWrites, styles.AlphaWrites);
        Assert.Equal(0x80u, styles.ExStyle[window]);
    }

    [Fact]
    public void StampChangesOnlyTheMarksThatAreWrong()
    {
        var styles = new FakeStyles();
        var window = new WindowId(5);
        styles.ExStyle[window] = 0x80;
        var adapter = new OpacityAdapter(styles);
        var right = new RecoverySnapshot(
        [
            new RecoveredClient(window, WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1) { Layered = true },
            new RecoveredClient(new WindowId(9), WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1),
        ]);
        var wrong = new RecoverySnapshot(
        [
            new RecoveredClient(new WindowId(10), WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1) { Layered = true },
            new RecoveredClient(new WindowId(11), WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1) { Layered = true },
        ]);
        var beforeAny = adapter.Stamp(wrong);
        adapter.Apply(window, 128);

        var unchanged = adapter.Stamp(right);
        var corrected = adapter.Stamp(wrong);

        Assert.Same(wrong, beforeAny);
        Assert.Same(right, unchanged);
        Assert.All(corrected.Clients, client => Assert.False(client.Layered));
        Assert.Throws<ArgumentNullException>(() => adapter.Stamp(null!));
        Assert.Throws<ArgumentNullException>(() => new OpacityAdapter(null!));
    }

    [Fact]
    public void ReleasingForgetsTheWindowAndLeavesAnUnlayeredOneAlone()
    {
        var styles = new FakeStyles { FailStyle = true };
        var owned = new WindowId(1);
        var failed = new WindowId(2);
        var plain = new WindowId(3);
        styles.ExStyle[failed] = 0x80;
        styles.ExStyle[plain] = 0x80;
        var adapter = new OpacityAdapter(styles);
        adapter.Apply(failed, 128);
        styles.FailStyle = false;
        styles.ExStyle[owned] = 0x80;
        adapter.Apply(owned, 128);
        var writes = styles.Writes;

        adapter.ReleaseAdded(failed);
        adapter.ReleaseAdded(plain);
        adapter.ReleaseAdded(new WindowId(99));
        var plainWrites = styles.Writes - writes;
        adapter.ReleaseAdded(owned);

        Assert.Equal(0, plainWrites);
        Assert.False(adapter.Failed(failed));
        Assert.False(adapter.Owns(owned));
        Assert.Equal(0x80u, styles.ExStyle[owned]);
    }

    [Fact]
    public void AForgottenWindowIsNotTouchedAndStartsOverIfItComesBack()
    {
        var styles = new FakeStyles();
        var window = new WindowId(11);
        styles.ExStyle[window] = 0x80;
        var adapter = new OpacityAdapter(styles);
        adapter.Apply(window, 0);
        var writes = styles.Writes;

        adapter.Forget(window);

        Assert.Equal(writes, styles.Writes);
        Assert.False(adapter.Owns(window));
        Assert.Equal(255, adapter.Wanted(window));
    }

    [Fact]
    public void AVeiledWindowIsTransparentAndKeepsTheAlphaItIsGivenForLater()
    {
        var styles = new FakeStyles();
        var window = new WindowId(9);
        styles.ExStyle[window] = 0x80;
        var adapter = new OpacityAdapter(styles);
        adapter.Apply(window, 200);

        Assert.True(adapter.Veil(window));
        Assert.Equal(0, styles.Alpha[window]);
        Assert.Equal(200, adapter.Wanted(window));

        adapter.Apply(window, 150);
        Assert.Equal(0, styles.Alpha[window]);
        Assert.Equal(150, adapter.Wanted(window));

        adapter.Unveil(window);
        Assert.Equal(150, styles.Alpha[window]);
        Assert.Equal(150, adapter.Wanted(window));
    }

    [Fact]
    public void AnOpaqueWindowIsLayeredToBeVeiledAndGetsItsStyleBackAfter()
    {
        var styles = new FakeStyles();
        var window = new WindowId(10);
        styles.ExStyle[window] = 0x80;
        var adapter = new OpacityAdapter(styles);

        Assert.True(adapter.Veil(window));
        Assert.Equal(OpacityAdapter.LayeredStyle | 0x80, styles.ExStyle[window]);
        Assert.Equal(255, adapter.Wanted(window));

        adapter.Unveil(window);
        Assert.Equal(0x80u, styles.ExStyle[window]);
        Assert.False(adapter.Owns(window));

        adapter.Unveil(window);
        Assert.Equal(0x80u, styles.ExStyle[window]);
    }

    [Fact]
    public void AWindowItsProgramLayeredCannotBeVeiled()
    {
        var styles = new FakeStyles();
        var window = new WindowId(11);
        styles.ExStyle[window] = OpacityAdapter.LayeredStyle;
        styles.Alpha[window] = 255;
        var adapter = new OpacityAdapter(styles);

        Assert.False(adapter.Veil(window));
        Assert.Equal(255, styles.Alpha[window]);
        Assert.Equal(0, styles.AlphaWrites);
    }

    [Fact]
    public void NoWindowIsVeiledAfterResetAllAndAResetForgetsTheVeil()
    {
        var styles = new FakeStyles();
        var first = new WindowId(12);
        var second = new WindowId(13);
        styles.ExStyle[first] = 0;
        styles.ExStyle[second] = 0;
        var adapter = new OpacityAdapter(styles);

        Assert.True(adapter.Veil(first));
        adapter.Reset(first);
        Assert.Equal(0u, styles.ExStyle[first]);
        Assert.Equal(255, adapter.Wanted(first));

        adapter.ResetAll();
        Assert.False(adapter.Veil(second));
        Assert.Equal(0u, styles.ExStyle[second]);
    }

    private sealed class FakeStyles : IWindowStyles
    {
        public Dictionary<WindowId, uint> ExStyle { get; } = [];

        public Dictionary<WindowId, byte> Alpha { get; } = [];

        public HashSet<WindowId> ColorKey { get; } = [];

        public bool FailAlpha { get; init; }

        public bool FailStyle { get; set; }

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
            if (FailStyle)
                return false;

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
