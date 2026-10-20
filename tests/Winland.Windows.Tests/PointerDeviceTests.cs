using Winland;

namespace Winland.Windows.Tests;

public class PointerDeviceTests
{
    [Fact]
    public void ARelativeMoveIsKeptAndAnAbsoluteInjectedOrEmptyOneIsNot()
    {
        Assert.True(PointerDeviceSource.TryMove(0, 4, -2, 0, 7, out var motion));
        Assert.Equal(new PointerDeviceSource.RawMotion(7, 4, -2), motion);

        Assert.False(PointerDeviceSource.TryMove(0x01, 4, -2, 0, 7, out _));
        Assert.False(PointerDeviceSource.TryMove(0, 4, -2, KeyboardHook.InjectedTag, 7, out _));
        Assert.False(PointerDeviceSource.TryMove(0, 4, -2, 0, 0, out _));
        Assert.False(PointerDeviceSource.TryMove(0, 0, 0, 0, 7, out _));
    }

    [Fact]
    public void AWheelUsesThePacketsOwnSignedDelta()
    {
        Assert.Equal(-120, PointerDeviceSource.WheelDelta(0x0400, unchecked((ushort)-120), 0, 7, horizontal: false));
        Assert.Null(PointerDeviceSource.WheelDelta(0x0400, 120, 0, 7, horizontal: true));
        Assert.Equal(120, PointerDeviceSource.WheelDelta(0x0800, 120, 0, 7, horizontal: true));
        Assert.Null(PointerDeviceSource.WheelDelta(0x0400, 120, KeyboardHook.InjectedTag, 7, horizontal: false));
        Assert.Null(PointerDeviceSource.WheelDelta(0x0400, 120, 0, 0, horizontal: false));
        Assert.Null(PointerDeviceSource.WheelDelta(0, 120, 0, 7, horizontal: false));
    }

    [Fact]
    public void PointerNamesFollowTheDevicesReplyAndSkipKeyboards()
    {
        var cache = new DeviceNameCache();
        cache.Replace(
        [
            new InputDevice(InputDeviceKind.Pointer, 10, "Pad"),
            new InputDevice(InputDeviceKind.Keyboard, 20, "Pad"),
            new InputDevice(InputDeviceKind.Pointer, 11, "Pad"),
        ],
        InputDeviceKind.Pointer);

        Assert.Equal("pad", Name(cache, 10));
        Assert.False(cache.TryName(20, out _));
        Assert.Equal("pad-2", Name(cache, 11));
    }

    [Fact]
    public void ADeviceOverrideDefersTheHookRotation()
    {
        Assert.Equal(0, MouseHook.AppliedRotation(defersMotion: true, global: 90));
        Assert.Equal(90, MouseHook.AppliedRotation(defersMotion: false, global: 90));
    }

    private static string Name(DeviceNameCache cache, long address)
    {
        Assert.True(cache.TryName(address, out var name));
        return name;
    }
}
