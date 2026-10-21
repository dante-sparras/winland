using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winland.Windows.Tests;

// Keys Winland sends itself must carry its tag, or its own hook takes them for the user's keys.
public class InjectedKeyTests
{
    [Fact]
    public void TheMenuTapIsATaggedLeftAlt()
    {
        var inputs = Foreground.MenuTap();

        Assert.Equal(2, inputs.Length);
        Assert.All(inputs, input =>
        {
            Assert.Equal(KeyboardHook.InjectedTag, input.Anonymous.ki.dwExtraInfo);
            Assert.Equal(0x12, (int)input.Anonymous.ki.wVk);
            // Extended would make it Right Alt, which is AltGr, with a fake Left Ctrl, on many layouts.
            Assert.False(input.Anonymous.ki.dwFlags.HasFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY));
        });
        Assert.False(inputs[0].Anonymous.ki.dwFlags.HasFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP));
        Assert.True(inputs[1].Anonymous.ki.dwFlags.HasFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP));
    }
}
