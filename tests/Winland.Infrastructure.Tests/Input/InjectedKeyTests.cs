using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winland.Infrastructure.Tests;

// Keys Winland sends itself must carry its tag, or its own hook takes them for the user's keys.
public class InjectedKeyTests
{
    [Theory]
    [InlineData(0xA2, 0x1D)]
    [InlineData(0xA3, 0x1D)]
    [InlineData(0xA4, 0x38)]
    [InlineData(0xA5, 0x38)]
    [InlineData(0xA0, 0x2A)]
    [InlineData(0xA1, 0x36)]
    [InlineData(0x5B, 0x5B)]
    public void InjectedModifiersCarryTheirScanCodes(ushort virtualKey, ushort scan)
    {
        Assert.Equal(scan, KeyboardHook.Key(virtualKey, up: false).Anonymous.ki.wScan);
        Assert.Equal(scan, KeyboardHook.Key(virtualKey, up: true).Anonymous.ki.wScan);
    }

    [Fact]
    public void AnExplicitScanCodeIsPreserved()
    {
        Assert.Equal(0x56, KeyboardHook.Key(0, up: false, scan: 0x56).Anonymous.ki.wScan);
    }

    [Fact]
    public void TheMenuTapIsATaggedLeftAltAroundTheMaskKey()
    {
        var inputs = Foreground.MenuTap(altHeld: false);

        Assert.Equal([0x12, KeyboardHook.MaskKey, KeyboardHook.MaskKey, 0x12], inputs.Select(input => (int)input.Anonymous.ki.wVk));
        Assert.All(inputs, input =>
        {
            Assert.Equal(KeyboardHook.InjectedTag, input.Anonymous.ki.dwExtraInfo);
            // Extended would make it Right Alt, which is AltGr, with a fake Left Ctrl, on many layouts.
            Assert.False(input.Anonymous.ki.dwFlags.HasFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY));
        });
        Assert.Equal([false, false, true, true], inputs.Select(input => input.Anonymous.ki.dwFlags.HasFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP)));
    }

    [Fact]
    public void AnAltTheUserHoldsIsLeftDown()
    {
        var inputs = Foreground.MenuTap(altHeld: true);

        Assert.All(inputs, input => Assert.Equal(KeyboardHook.MaskKey, (ushort)input.Anonymous.ki.wVk));
    }
}
