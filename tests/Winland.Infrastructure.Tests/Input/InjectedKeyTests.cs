using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winland.Infrastructure.Tests;

// Keys Winland sends itself must carry its tag, or its own hook takes them for the user's keys.
public class InjectedKeyTests
{
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
