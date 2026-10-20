using Windows.Win32.UI.Input.KeyboardAndMouse;
using Winland;

namespace Winland.Windows.Tests;

public class KeyDeviceTests
{
    [Fact]
    public void TheOldestWaitingPressTakesThePacketAndAReleasePacketDoesNot()
    {
        var waiting = new DeferredKeys();
        Assert.True(waiting.Hold(Edge(0x51, "Q", scan: 16), 1000));
        Assert.True(waiting.Hold(Edge(0x51, "Q", scan: 16), 1000));
        Assert.True(waiting.Hold(Edge(0x52, "W", scan: 17), 1000));

        Assert.False(waiting.Offer(0x51, 16, up: true, out _));
        Assert.True(waiting.IsWaiting(0x51));

        Assert.True(waiting.Offer(0x51, 16, up: false, out var first));
        Assert.Equal("Q", first.Key.Name);
        Assert.True(waiting.Offer(0x51, 16, up: false, out var second));
        Assert.Equal("Q", second.Key.Name);
        Assert.False(waiting.IsWaiting(0x51));
        Assert.True(waiting.Offer(0x52, 0, up: false, out var other));
        Assert.Equal("W", other.Key.Name);
    }

    [Fact]
    public void ADifferentScanStaysAndAReleaseRidesThePressPacket()
    {
        var waiting = new DeferredKeys();
        Assert.True(waiting.Hold(Edge(0x51, "Q", scan: 16), 1000));
        Assert.False(waiting.Offer(0x51, 99, up: false, out _));
        Assert.True(waiting.AttachRelease(Edge(0x51, "Q", scan: 16, up: true)));

        Assert.True(waiting.Offer(0x51, 16, up: false, out var pending));
        Assert.True(pending.HasRelease);
        Assert.True(pending.Release.Up);
        Assert.False(waiting.Offer(0x51, 16, up: false, out _));
    }

    [Fact]
    public void TheWaitExpiresOldestFirstAndALatePacketIsIgnored()
    {
        var waiting = new DeferredKeys();
        Assert.True(waiting.Hold(Edge(0x51, "Q"), 1000));
        Assert.True(waiting.Hold(Edge(0x52, "W"), 1010));
        Assert.True(waiting.TryNextDue(out var due));
        Assert.Equal(1000 + DeferredKeys.WaitMs, due);

        Assert.False(waiting.Expire(1000 + DeferredKeys.WaitMs - 1, out _));
        Assert.True(waiting.Expire(1000 + DeferredKeys.WaitMs, out var first));
        Assert.Equal("Q", first.Key.Name);
        Assert.False(waiting.Offer(0x51, 16, up: false, out _));
        Assert.True(waiting.Expire(1010 + DeferredKeys.WaitMs, out var second));
        Assert.Equal("W", second.Key.Name);
    }

    [Fact]
    public void RepeatsAreCappedAndAFullQueueRefusesTheNextPress()
    {
        var waiting = new DeferredKeys();
        Assert.True(waiting.Hold(Edge(0x51, "Q"), 1000));
        for (var i = 0; i < 10; i++)
            Assert.True(waiting.NoteRepeat(0x51));

        Assert.True(waiting.Offer(0x51, 16, up: false, out var pending));
        Assert.Equal(8, pending.Repeats);

        for (var i = 0; i < DeferredKeys.Capacity; i++)
            Assert.True(waiting.Hold(Edge(0x41 + i, ((char)('A' + i)).ToString()), 1000));

        Assert.False(waiting.Hold(Edge(0x5A, "Z"), 1000));
    }

    [Fact]
    public void APacketAfterTheHookRunsTheDeviceBind()
    {
        using var hook = Hook("bindk = , Q, +board, killactive");
        var tracked = hook.DeliverTracked(Edge(0x51, "Q"), 1000, repeated: false);

        Assert.True(tracked.Waiting);
        Assert.False(tracked.Passes);
        Assert.True(hook.Complete(0x51, 16, up: false, "board", out var outcome));
        Assert.True(outcome.Decision.Consumed);
        Assert.Equal("killactive", outcome.Decision.Dispatcher);
        Assert.False(outcome.Replayed);
        Assert.False(hook.IsWaiting(0x51));
    }

    [Fact]
    public void AWrongDeviceReplaysTheKey()
    {
        using var hook = Hook("bindk = , Q, +board, killactive");
        Assert.True(hook.DeliverTracked(Edge(0x51, "Q"), 1000, repeated: false).Waiting);

        Assert.True(hook.Complete(0x51, 16, up: false, "other", out var outcome));
        Assert.False(outcome.Decision.Consumed);
        Assert.True(outcome.Replayed);
    }

    [Fact]
    public void NoPacketReplaysAnInclusiveBindAndFiresAnExclusiveOne()
    {
        using var inclusive = Hook("bindk = , Q, +board, killactive");
        Assert.True(inclusive.DeliverTracked(Edge(0x51, "Q"), 1000, repeated: false).Waiting);
        Assert.False(inclusive.ExpireOne(1000 + DeferredKeys.WaitMs - 1, out _));
        Assert.True(inclusive.ExpireOne(1000 + DeferredKeys.WaitMs, out var missed));
        Assert.False(missed.Decision.Consumed);
        Assert.True(missed.Replayed);

        using var exclusive = Hook("bindk = , Q, -board, killactive");
        Assert.True(exclusive.DeliverTracked(Edge(0x51, "Q"), 1000, repeated: false).Waiting);
        Assert.True(exclusive.ExpireOne(1000 + DeferredKeys.WaitMs, out var matched));
        Assert.Equal("killactive", matched.Decision.Dispatcher);
        Assert.True(matched.Decision.Consumed);
        Assert.False(matched.Replayed);
    }

    [Fact]
    public void AnUnnamedPacketEndsTheWaitWithNoDevice()
    {
        using var hook = Hook("bindk = , Q, +board, killactive");
        Assert.True(hook.DeliverTracked(Edge(0x51, "Q"), 1000, repeated: false).Waiting);

        Assert.True(hook.DeliverPacket(0x51, 16, up: false, handle: 5, out var outcome));
        Assert.False(outcome.Decision.Consumed);
        Assert.True(outcome.Replayed);
        Assert.False(hook.IsWaiting(0x51));
    }

    [Fact]
    public void AKeyNoDeviceBindCouldMatchIsDecidedImmediately()
    {
        using var hook = Hook("bindk = , Q, +board, killactive\nbind = , W, killactive\n");

        var plain = hook.DeliverTracked(Edge(0x45, "E"), 1000, repeated: false);
        Assert.False(plain.Waiting);
        Assert.True(plain.Passes);
        Assert.False(hook.IsWaiting(0x45));

        var other = hook.DeliverTracked(Edge(0x57, "W"), 1000, repeated: false);
        Assert.False(other.Waiting);
        Assert.False(other.Passes);
        Assert.Equal("killactive", other.Decision.Dispatcher);
        Assert.False(hook.IsWaiting(0x57));

        Assert.True(hook.DeliverTracked(Edge(0x51, "Q"), 1000, repeated: false).Waiting);
    }

    [Fact]
    public void NoDeviceBindKeepsTheSynchronousPath()
    {
        using var hook = Hook("bind = , Q, killactive");
        var tracked = hook.DeliverTracked(Edge(0x51, "Q"), 1000, repeated: false);

        Assert.False(tracked.Waiting);
        Assert.False(tracked.Passes);
        Assert.Equal("killactive", tracked.Decision.Dispatcher);
        Assert.False(hook.IsWaiting(0x51));
    }

    [Fact]
    public void TheWrongModifiersDoNotWait()
    {
        using var hook = Hook("bindk = SUPER, Q, +board, killactive");
        var tracked = hook.DeliverTracked(Edge(0x51, "Q"), 1000, repeated: false);

        Assert.False(tracked.Waiting);
        Assert.True(tracked.Passes);
    }

    [Fact]
    public void AReleaseWaitingWithThePressUsesTheSameDevice()
    {
        using var hook = Hook("bindrk = , Q, +board, killactive");
        Assert.True(hook.DeliverTracked(Edge(0x51, "Q"), 1000, repeated: false).Waiting);

        var release = hook.DeliverTracked(Edge(0x51, "Q", up: true), 1020, repeated: false);
        Assert.True(release.Waiting);
        Assert.True(hook.IsWaiting(0x51));

        Assert.True(hook.Complete(0x51, 16, up: false, "board", out var outcome));
        Assert.True(outcome.Decision.Consumed);
        Assert.Null(outcome.Decision.Dispatcher);
        Assert.Equal("killactive", outcome.Release?.Dispatcher);
        Assert.False(outcome.Replayed);
        Assert.False(outcome.ReleaseReplayed);
        Assert.False(hook.IsWaiting(0x51));
    }

    [Fact]
    public void AReleaseAfterThePressWasDecidedDoesNotWaitAgain()
    {
        using var hook = Hook("bindrk = , Q, +board, killactive");
        Assert.True(hook.DeliverTracked(Edge(0x51, "Q"), 1000, repeated: false).Waiting);
        Assert.True(hook.Complete(0x51, 16, up: false, "board", out var press));
        Assert.True(press.Decision.Consumed);
        Assert.Null(press.Decision.Dispatcher);

        var release = hook.DeliverTracked(Edge(0x51, "Q", up: true), 1080, repeated: false);
        Assert.False(release.Waiting);
        Assert.False(release.Passes);
        Assert.Equal("killactive", release.Decision.Dispatcher);
    }

    [Fact]
    public void ASwallowedPressAndReleaseAreReplayedTogetherWhenNothingConsumesThem()
    {
        using var hook = Hook("bindk = , Q, +board, killactive");
        Assert.True(hook.DeliverTracked(Edge(0x51, "Q"), 1000, repeated: false).Waiting);
        Assert.True(hook.DeliverTracked(Edge(0x51, "Q", up: true), 1010, repeated: false).Waiting);

        Assert.True(hook.ExpireOne(1000 + DeferredKeys.WaitMs, out var outcome));
        Assert.False(outcome.Decision.Consumed);
        Assert.True(outcome.Replayed);
        Assert.True(outcome.ReleaseReplayed);
        Assert.False(outcome.Release?.Consumed);
    }

    [Fact]
    public void RepeatsDuringTheWaitAreDecidedWithThePress()
    {
        using var hook = Hook("bindek = , Q, +board, killactive");
        Assert.True(hook.DeliverTracked(Edge(0x51, "Q"), 1000, repeated: false).Waiting);
        Assert.True(hook.DeliverTracked(Edge(0x51, "Q"), 1030, repeated: true).Waiting);

        Assert.True(hook.Complete(0x51, 16, up: false, "board", out var outcome));
        Assert.Equal(1, outcome.Repeats);
        Assert.Equal("killactive", outcome.Decision.Dispatcher);
        Assert.False(hook.IsWaiting(0x51));
    }

    [Fact]
    public void AFullWaitDecidesTheNextKeyWithNoDevice()
    {
        var lines = new System.Text.StringBuilder();
        for (var i = 0; i < DeferredKeys.Capacity + 1; i++)
            lines.Append("bindk = , ").Append((char)('A' + i)).Append(", +board, killactive\n");

        using var hook = Hook(lines.ToString());
        for (var i = 0; i < DeferredKeys.Capacity; i++)
            Assert.True(hook.DeliverTracked(Edge(0x41 + i, ((char)('A' + i)).ToString(), scan: 1), 1000, repeated: false).Waiting);

        var overflow = hook.DeliverTracked(Edge(0x41 + DeferredKeys.Capacity, "I", scan: 1), 1000, repeated: false);
        Assert.False(overflow.Waiting);
        Assert.True(overflow.Passes);
        Assert.False(hook.IsWaiting(0x41 + DeferredKeys.Capacity));
    }

    [Fact]
    public void NamesFollowTheDevicesReplyIncludingDuplicates()
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

    private static DeferredKeys.Edge Edge(int vk, string name, int scan = 16, bool up = false) =>
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
