using Windows.Win32.UI.Input.KeyboardAndMouse;
using Winland;

namespace Winland.Windows.Tests;

public class KeyDeviceTests
{
    [Fact]
    public void APacketWithinTheSlackMatchesAndAnOlderOneIsDropped()
    {
        var correlator = new KeyDeviceCorrelator();
        correlator.Offer(new KeyDeviceCorrelator.Packet(1, 0x51, 16, false, 1000));
        correlator.Offer(new KeyDeviceCorrelator.Packet(2, 0x51, 16, false, 1000 - KeyDeviceCorrelator.SlackMs - 1));

        Assert.True(correlator.Take(0x51, 16, false, 1000, out var device));
        Assert.Equal(1, device);
        Assert.Equal(0, correlator.Count);
    }

    [Fact]
    public void ANewerPacketIsKeptAndTheOldestMatchIsTaken()
    {
        var correlator = new KeyDeviceCorrelator();
        correlator.Offer(new KeyDeviceCorrelator.Packet(1, 0x51, 16, false, 1000));
        correlator.Offer(new KeyDeviceCorrelator.Packet(2, 0x51, 16, false, 1010));
        correlator.Offer(new KeyDeviceCorrelator.Packet(3, 0x52, 17, false, 1000));

        Assert.True(correlator.Take(0x51, 16, false, 1005, out var device));
        Assert.Equal(1, device);
        Assert.True(correlator.Take(0x51, 16, false, 1010, out device));
        Assert.Equal(2, device);
        Assert.True(correlator.Take(0x52, 17, false, 1000, out device));
        Assert.Equal(3, device);
    }

    [Fact]
    public void TheSlackIsInclusiveAndADifferentScanStays()
    {
        var correlator = new KeyDeviceCorrelator();
        correlator.Offer(new KeyDeviceCorrelator.Packet(1, 0x51, 16, false, 1000 - KeyDeviceCorrelator.SlackMs));
        correlator.Offer(new KeyDeviceCorrelator.Packet(2, 0x51, 16, false, 2000 + KeyDeviceCorrelator.SlackMs));
        correlator.Offer(new KeyDeviceCorrelator.Packet(3, 0x51, 16, false, 3000 + KeyDeviceCorrelator.SlackMs + 1));
        correlator.Offer(new KeyDeviceCorrelator.Packet(4, 0x51, 99, false, 4000));

        Assert.True(correlator.Take(0x51, 16, false, 1000, out var device));
        Assert.Equal(1, device);
        Assert.True(correlator.Take(0x51, 16, false, 2000, out device));
        Assert.Equal(2, device);
        Assert.False(correlator.Take(0x51, 16, false, 3000, out _));
        Assert.False(correlator.Take(0x51, 17, false, 4000, out _));
        Assert.True(correlator.Take(0x51, 0, false, 4000, out device));
        Assert.Equal(4, device);
    }

    [Fact]
    public void AReleaseDoesNotMatchAPress()
    {
        var correlator = new KeyDeviceCorrelator();
        correlator.Offer(new KeyDeviceCorrelator.Packet(1, 0x51, 16, true, 1000));

        Assert.False(correlator.Take(0x51, 16, false, 1000, out _));
        Assert.Equal(1, correlator.Count);
    }

    [Fact]
    public void TimeWrapsAcrossTheClock()
    {
        var correlator = new KeyDeviceCorrelator();
        correlator.Offer(new KeyDeviceCorrelator.Packet(7, 0x51, 16, false, 10));
        Assert.True(correlator.Take(0x51, 16, false, uint.MaxValue - 5, out var device));
        Assert.Equal(7, device);

        correlator.Offer(new KeyDeviceCorrelator.Packet(8, 0x51, 16, false, uint.MaxValue - 5));
        Assert.True(correlator.Take(0x51, 16, false, 10, out device));
        Assert.Equal(8, device);

        correlator.Offer(new KeyDeviceCorrelator.Packet(9, 0x51, 16, false, uint.MaxValue - 100));
        Assert.False(correlator.Take(0x51, 16, false, 100, out _));
        Assert.Equal(0, correlator.Count);
    }

    [Fact]
    public void TheQueueDropsTheOldestPastItsCap()
    {
        var correlator = new KeyDeviceCorrelator();
        for (var i = 0; i < 33; i++)
            correlator.Offer(new KeyDeviceCorrelator.Packet(i, 1, 1, false, (uint)i));

        Assert.Equal(32, correlator.Count);
        Assert.True(correlator.Take(1, 1, false, 1, out var device));
        Assert.Equal(1, device);
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
        Assert.False(KeyDeviceSource.TryKey(0x51, 16, 0, KeyboardHook.InjectedTag, 5, 1, out _));
        Assert.False(KeyDeviceSource.TryKey(0x51, 16, 0, 0, 0, 1, out _));

        Assert.True(KeyDeviceSource.TryKey(0x51, 16, 1, 0, 5, 9, out var packet));
        Assert.Equal(new KeyDeviceCorrelator.Packet(5, 0x51, 16, true, 9), packet);
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

    private static string Name(DeviceNameCache cache, long address)
    {
        Assert.True(cache.TryName(address, out var name));
        return name;
    }
}
