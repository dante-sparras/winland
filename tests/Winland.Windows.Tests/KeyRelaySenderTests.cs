using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winland.Windows.Tests;

public class KeyRelaySenderTests
{
    private const int SuperL = 0x5B;
    private const int ControlL = 0xA2;
    private const int AltL = 0xA4;
    private const int Mask = KeyboardHook.MaskKey;
    private static readonly KeyStroke A = new(0x41, 0x1E, Extended: false);

    [Fact]
    public void AHeldModifierThatIsNotWantedIsLetGoAndPressedAgain()
    {
        var relay = new KeyRelay(null, "a", KeyRelayPhase.Tap) { Modifiers = Modifier.Ctrl };

        var inputs = Win32KeyRelaySender.Build(relay, A, down: vk => vk == SuperL, held: vk => vk == SuperL);

        Assert.Equal(
            [
                Down(Mask), Up(Mask), Up(SuperL), Down(ControlL), Down(0x41), Up(0x41), Up(ControlL), Down(SuperL), Down(Mask), Up(Mask),
            ],
            Describe(inputs));
    }

    // Windows still holds Super, but the user has let go and the release is on its way. Pressing it
    // again would land after that release and leave Super down, so Super+L would lock the PC.
    [Fact]
    public void AModifierWhoseReleaseIsInFlightIsNotPressedAgain()
    {
        var relay = new KeyRelay(null, "a", KeyRelayPhase.Tap) { Modifiers = Modifier.Ctrl };

        var inputs = Win32KeyRelaySender.Build(relay, A, down: vk => vk is SuperL or AltL, held: _ => false);

        Assert.Equal(
            [Down(Mask), Up(Mask), Up(SuperL), Down(ControlL), Up(AltL), Down(0x41), Up(0x41), Up(ControlL), Down(Mask), Up(Mask)],
            Describe(inputs));
    }

    [Fact]
    public void AWantedModifierAlreadyDownIsLeftAlone()
    {
        var relay = new KeyRelay(null, "a", KeyRelayPhase.Press) { Modifiers = Modifier.Ctrl };

        var inputs = Win32KeyRelaySender.Build(relay, A, down: vk => vk == ControlL, held: vk => vk == ControlL);

        Assert.Equal([Down(0x41)], Describe(inputs));
    }

    [Fact]
    public void NoModifiersLeavesTheHeldOnesAsTheyAre()
    {
        var relay = new KeyRelay(null, "a", KeyRelayPhase.Release);

        var inputs = Win32KeyRelaySender.Build(relay, A, down: _ => true, held: _ => true);

        Assert.Equal([Up(0x41)], Describe(inputs));
    }

    [Fact]
    public void EveryRelayedEventIsTagged()
    {
        var relay = new KeyRelay(null, "a", KeyRelayPhase.Tap) { Modifiers = Modifier.Shift };

        var inputs = Win32KeyRelaySender.Build(relay, A, down: vk => vk == SuperL, held: vk => vk == SuperL);

        Assert.All(inputs, input => Assert.Equal(KeyboardHook.InjectedTag, input.Anonymous.ki.dwExtraInfo));
    }

    private static string Down(int vk) => $"{vk:X2} down";

    private static string Up(int vk) => $"{vk:X2} up";

    private static List<string> Describe(IEnumerable<INPUT> inputs) =>
        inputs.Select(input =>
        {
            var key = input.Anonymous.ki;
            var up = key.dwFlags.HasFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP);
            return up ? Up((int)key.wVk) : Down((int)key.wVk);
        }).ToList();
}
