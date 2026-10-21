using Windows.Win32.UI.Input.KeyboardAndMouse;
using Winland;

namespace Winland.Windows.Tests;

public class KeyDeviceTests
{
    [Fact]
    public void APassedKeyRunsItsDeviceBindWhenThePacketNamesTheKeyboard()
    {
        using var hook = Hook("bindnk = , Q, +board, killactive");
        var tracked = hook.DeliverTracked(Edge(0x51, "Q"), repeated: false);

        Assert.True(tracked.Passes);
        Assert.Null(tracked.Decision.Dispatcher);
        Assert.False(tracked.Decision.Consumed);

        Assert.True(hook.DeliverDevice(0x51, 16, up: false, "board", out var delivery));
        Assert.False(delivery.Decision.Consumed);
        Assert.Equal("killactive", delivery.Decision.Dispatcher);
        Assert.False(hook.DeliverDevice(0x51, 16, up: false, "board", out _));
    }

    [Fact]
    public void AWrongOrUnnamedDeviceHandlesThePressAndRunsNothing()
    {
        using var hook = Hook("bindnk = , Q, +board, killactive");
        Assert.True(hook.DeliverTracked(Edge(0x51, "Q"), repeated: false).Passes);

        Assert.True(hook.DeliverDevice(0x51, 16, up: false, "other", out var wrong));
        Assert.Null(wrong.Decision.Dispatcher);

        using var unnamed = Hook("bindnk = , Q, +board, killactive");
        Assert.True(unnamed.DeliverTracked(Edge(0x51, "Q"), repeated: false).Passes);
        Assert.True(unnamed.DeliverPacket(0x51, 16, up: false, handle: 5, out var missing));
        Assert.Null(missing.Decision.Dispatcher);
        Assert.True(unnamed.DeviceRefreshPending);
    }

    [Fact]
    public void AKeyWithNoDeviceBindIsDecidedImmediately()
    {
        using var hook = Hook("bindnk = , Q, +board, killactive\nbind = , W, killactive\n");

        var queued = hook.DeliverTracked(Edge(0x51, "Q"), repeated: false);
        Assert.True(queued.Passes);
        Assert.Null(queued.Decision.Dispatcher);
        Assert.True(hook.DeliverDevice(0x51, 16, up: false, "board", out var delivery));
        Assert.Equal("killactive", delivery.Decision.Dispatcher);

        var plain = hook.DeliverTracked(Edge(0x45, "E"), repeated: false);
        Assert.True(plain.Passes);
        Assert.Null(plain.Decision.Dispatcher);

        var other = hook.DeliverTracked(Edge(0x57, "W"), repeated: false);
        Assert.False(other.Passes);
        Assert.Equal("killactive", other.Decision.Dispatcher);
    }

    [Fact]
    public void NoDeviceBindKeepsTheSynchronousPath()
    {
        using var hook = Hook("bind = , Q, killactive");
        var tracked = hook.DeliverTracked(Edge(0x51, "Q"), repeated: false);

        Assert.False(tracked.Passes);
        Assert.Equal("killactive", tracked.Decision.Dispatcher);
        Assert.False(hook.DeliverDevice(0x51, 16, up: false, "board", out _));
    }

    [Fact]
    public void AnAutoConsumingPressIsSwallowedForReplay()
    {
        using var hook = Hook("binda = , Q, killactive");
        var tracked = hook.DeliverTracked(Edge(0x51, "Q"), repeated: false);

        Assert.False(tracked.Passes);
        Assert.True(tracked.Decision.Consumed);
        Assert.True(tracked.Decision.PassesOnFailure);
        Assert.Equal("killactive", tracked.Decision.Dispatcher);
        Assert.False(hook.DeliverDevice(0x51, 16, up: false, "board", out _));
    }

    [Fact]
    public void AConsumingDeviceBindIsNotRegisteredSoTheKeyPasses()
    {
        using var hook = Hook("bindk = , Q, +board, killactive");
        var tracked = hook.DeliverTracked(Edge(0x51, "Q"), repeated: false);

        Assert.True(tracked.Passes);
        Assert.Null(tracked.Decision.Dispatcher);
        Assert.False(hook.DeliverDevice(0x51, 16, up: false, "board", out _));
    }

    [Fact]
    public void AReleaseAfterThePacketUsesThePressKeyboard()
    {
        using var hook = Hook("bindnk = , Q, +board, killactive\nbindrnk = , Q, +board, exit\n");
        Assert.True(hook.DeliverTracked(Edge(0x51, "Q"), repeated: false).Passes);
        Assert.True(hook.DeliverDevice(0x51, 16, up: false, "board", out var press));
        Assert.Equal("killactive", press.Decision.Dispatcher);

        var release = hook.DeliverTracked(Edge(0x51, "Q", up: true), repeated: false);
        Assert.True(release.Passes);
        Assert.Equal("exit", release.Decision.Dispatcher);
        Assert.False(hook.DeliverDevice(0x51, 16, up: true, "board", out _));
    }

    [Fact]
    public void AFastTapRunsThePressAndTheReleaseFromTheDownPacket()
    {
        using var hook = Hook("bindnk = , Q, +board, killactive\nbindrnk = , Q, +board, exit\n");
        Assert.Null(hook.DeliverTracked(Edge(0x51, "Q"), repeated: false).Decision.Dispatcher);
        Assert.Null(hook.DeliverTracked(Edge(0x51, "Q", up: true), repeated: false).Decision.Dispatcher);

        Assert.True(hook.DeliverDevice(0x51, 16, up: false, "board", out var delivery));
        Assert.Equal("killactive", delivery.Decision.Dispatcher);
        Assert.Equal("exit", delivery.Release?.Dispatcher);
        Assert.False(hook.DeliverDevice(0x51, 16, up: true, "board", out _));
    }

    [Fact]
    public void ASecondTapPairsWithTheNextPacket()
    {
        using var hook = Hook("bindnk = , Q, +board, killactive\nbindrnk = , Q, +board, exit\n");
        hook.DeliverTracked(Edge(0x51, "Q"), repeated: false);
        hook.DeliverTracked(Edge(0x51, "Q", up: true), repeated: false);
        hook.DeliverTracked(Edge(0x51, "Q"), repeated: false);

        Assert.True(hook.DeliverDevice(0x51, 16, up: false, "board", out var first));
        Assert.Equal("killactive", first.Decision.Dispatcher);
        Assert.Equal("exit", first.Release?.Dispatcher);

        Assert.True(hook.DeliverDevice(0x51, 16, up: false, "board", out var second));
        Assert.Equal("killactive", second.Decision.Dispatcher);
        Assert.Null(second.Release);
    }

    [Fact]
    public void RepeatsBeforeThePacketAreFlushedWithItAndLaterRepeatsAreTheHooks()
    {
        using var early = Hook("bindenk = , Q, +board, killactive");
        early.DeliverTracked(Edge(0x51, "Q"), repeated: false);
        Assert.Null(early.DeliverTracked(Edge(0x51, "Q"), repeated: true).Decision.Dispatcher);
        Assert.True(early.DeliverDevice(0x51, 16, up: false, "board", out var flushed));
        Assert.Equal(1, flushed.Repeats);
        Assert.Equal("killactive", flushed.Decision.Dispatcher);
        Assert.Equal("killactive", flushed.Repeated?.Dispatcher);

        using var late = Hook("bindenk = , Q, +board, killactive");
        late.DeliverTracked(Edge(0x51, "Q"), repeated: false);
        Assert.True(late.DeliverDevice(0x51, 16, up: false, "board", out var press));
        Assert.Equal("killactive", press.Decision.Dispatcher);
        Assert.Equal(0, press.Repeats);

        var repeat = late.DeliverTracked(Edge(0x51, "Q"), repeated: true);
        Assert.Equal("killactive", repeat.Decision.Dispatcher);
        Assert.False(late.DeliverDevice(0x51, 16, up: false, "board", out _));
    }

    [Fact]
    public void RepeatsBeforeThePacketStopAtEight()
    {
        using var hook = Hook("bindenk = , Q, +board, killactive");
        hook.DeliverTracked(Edge(0x51, "Q"), repeated: false);
        for (var i = 0; i < KeyboardHook.DeviceRepeatCap + 2; i++)
            hook.DeliverTracked(Edge(0x51, "Q"), repeated: true);

        Assert.True(hook.DeliverDevice(0x51, 16, up: false, "board", out var delivery));
        Assert.Equal(KeyboardHook.DeviceRepeatCap, delivery.Repeats);
    }

    [Fact]
    public void AFullQueueDropsTheOldestPassedPressAndStillPassesTheKey()
    {
        var lines = new System.Text.StringBuilder();
        for (var i = 0; i < KeyboardHook.PassedCapacity + 1; i++)
            lines.Append("bindnk = , ").Append((char)('A' + i)).Append(", +board, killactive\n");

        using var hook = Hook(lines.ToString());
        for (var i = 0; i < KeyboardHook.PassedCapacity + 1; i++)
        {
            var tracked = hook.DeliverTracked(Edge(0x41 + i, ((char)('A' + i)).ToString(), scan: 1), repeated: false);
            Assert.True(tracked.Passes);
        }

        Assert.False(hook.DeliverDevice(0x41, 1, up: false, "board", out _));
        Assert.True(hook.DeliverDevice(0x41 + KeyboardHook.PassedCapacity, 1, up: false, "board", out var kept));
        Assert.Equal("killactive", kept.Decision.Dispatcher);
    }

    [Fact]
    public void AMismatchedScanDoesNotTakeANewerPress()
    {
        using var hook = Hook("bindnk = , Q, +board, killactive");
        hook.DeliverTracked(Edge(0x51, "Q", scan: 16), repeated: false);
        hook.DeliverTracked(Edge(0x51, "Q", scan: 17), repeated: false);

        Assert.False(hook.DeliverDevice(0x51, 17, up: false, "board", out _));
        Assert.True(hook.DeliverDevice(0x51, 16, up: false, "board", out var first));
        Assert.Equal("killactive", first.Decision.Dispatcher);
        Assert.True(hook.DeliverDevice(0x51, 17, up: false, "board", out var second));
        Assert.Equal("killactive", second.Decision.Dispatcher);
    }

    [Fact]
    public void ADeviceTagDoesNotRunTheBind()
    {
        using var tagged = Hook("device[board]:tags = laptop\nbindnk = , Q, +laptop, killactive\n");
        tagged.DeliverTracked(Edge(0x51, "Q"), repeated: false);

        Assert.True(tagged.DeliverDevice(0x51, 16, up: false, "board", out var board));
        Assert.Null(board.Decision.Dispatcher);
        Assert.False(board.Decision.Consumed);

        using var named = Hook("bindnk = , Q, +laptop, killactive\n");
        named.DeliverTracked(Edge(0x51, "Q"), repeated: false);
        Assert.True(named.DeliverDevice(0x51, 16, up: false, "laptop", out var laptop));
        Assert.Equal("killactive", laptop.Decision.Dispatcher);
        Assert.False(laptop.Decision.Consumed);
    }

    [Fact]
    public void NamesFollowTheRawInputNamesIncludingDuplicates()
    {
        var cache = new DeviceNameCache();
        cache.Replace(
        [
            new InputDevice(InputDeviceKind.Pointer, 10, "Pad"),
            new InputDevice(InputDeviceKind.Keyboard, 20, "Pad"),
            new InputDevice(InputDeviceKind.Keyboard, 21, "Board Two"),
            new InputDevice(InputDeviceKind.Keyboard, 22, ""),
            new InputDevice(InputDeviceKind.Keyboard, 23, "Pad"),
        ]);

        Assert.False(cache.TryName(10, out _));
        Assert.Equal("pad-1", Name(cache, 20));
        Assert.Equal("board-two", Name(cache, 21));
        Assert.Equal("unknown-device", Name(cache, 22));
        Assert.Equal("pad-2", Name(cache, 23));
    }

    [Fact]
    public void InjectedAndDevicelessPacketsAreNotKeys()
    {
        Assert.False(KeyDeviceSource.TryKey(0x51, 16, 0, KeyboardHook.InjectedTag, 5, out _));
        Assert.False(KeyDeviceSource.TryKey(0x51, 16, 0, 0, 0, out _));

        Assert.True(KeyDeviceSource.TryKey(0x51, 16, 1, 0, 5, out var packet));
        Assert.Equal(new KeyDeviceSource.RawKey(5, 0x51, 16, true), packet);
    }

    [Fact]
    public void AReplayPressesAHeldKeyAndClicksOneAlreadyUp()
    {
        var held = KeyboardHook.ReplayInputs(new SwallowedKey(0x51, 16, true, "Q", true), stillDown: true);
        var press = Assert.Single(held);
        Assert.Equal(KeyboardHook.InjectedTag, press.Anonymous.ki.dwExtraInfo);
        Assert.Equal(0x51, (int)press.Anonymous.ki.wVk);
        Assert.Equal(16, press.Anonymous.ki.wScan);
        Assert.False(press.Anonymous.ki.dwFlags.HasFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP));
        Assert.True(press.Anonymous.ki.dwFlags.HasFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY));

        var gone = KeyboardHook.ReplayInputs(new SwallowedKey(0x51, 16, false, "Q", true), stillDown: false);
        Assert.Equal(2, gone.Length);
        Assert.False(gone[0].Anonymous.ki.dwFlags.HasFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP));
        Assert.True(gone[1].Anonymous.ki.dwFlags.HasFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP));
        Assert.False(gone[0].Anonymous.ki.dwFlags.HasFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY));
        Assert.All(gone, input => Assert.Equal(KeyboardHook.InjectedTag, input.Anonymous.ki.dwExtraInfo));

        var release = Assert.Single(KeyboardHook.ReplayInputs(new SwallowedKey(0x5B, 0, false, "Super_L", false), stillDown: false));
        Assert.True(release.Anonymous.ki.dwFlags.HasFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP));
        Assert.True(release.Anonymous.ki.dwFlags.HasFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY));
    }

    [Fact]
    public void ConsumptionReplaysOnceWhenEveryDispatchFails()
    {
        var replays = 0;
        var batch = new BindConsumption(() => replays++);
        batch.Expect();
        batch.Expect();
        batch.Note(false);
        Assert.Equal(0, replays);
        batch.Note(false);
        Assert.Equal(1, replays);
        batch.Note(false);
        Assert.Equal(1, replays);
    }

    [Fact]
    public void OneSuccessKeepsTheKey()
    {
        var replays = 0;
        var batch = new BindConsumption(() => replays++);
        batch.Expect();
        batch.Expect();
        batch.Note(true);
        batch.Note(false);
        Assert.Equal(0, replays);
    }

    private static KeyboardHook.KeyEdge Edge(int vk, string name, int scan = 16, bool up = false) =>
        new(vk, scan, false, up, name, 0);

    private static KeyboardHook Hook(string text)
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(text)));
        return new KeyboardHook(core, new BindSession());
    }

    private static string Name(DeviceNameCache cache, long address)
    {
        Assert.True(cache.TryName(address, out var name));
        return name;
    }
}
