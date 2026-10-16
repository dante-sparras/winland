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

    [Fact]
    public void AnExtendedKeyAndAScanCodeOnlyKeyCarryTheirFlags()
    {
        var right = new KeyStroke(0x27, 0x4D, Extended: true);
        var scanOnly = new KeyStroke(0, 0x56, Extended: false);

        var extended = Win32KeyRelaySender.Build(new KeyRelay(null, "Right", KeyRelayPhase.Tap), right, _ => false, _ => false);
        var scanned = Win32KeyRelaySender.Build(new KeyRelay(null, "code:94", KeyRelayPhase.Press), scanOnly, _ => false, _ => false);
        var plain = Win32KeyRelaySender.Build(new KeyRelay(null, "a", KeyRelayPhase.Press), A, _ => false, _ => false);

        Assert.Equal(
            [KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY, KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY | KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP],
            extended.Select(input => input.Anonymous.ki.dwFlags));
        Assert.All(extended, input => Assert.Equal(INPUT_TYPE.INPUT_KEYBOARD, input.type));
        Assert.Equal(0x4D, extended[0].Anonymous.ki.wScan);
        Assert.Equal(KEYBD_EVENT_FLAGS.KEYEVENTF_SCANCODE, Assert.Single(scanned).Anonymous.ki.dwFlags);
        Assert.Equal(0x56, scanned[0].Anonymous.ki.wScan);
        Assert.Equal((KEYBD_EVENT_FLAGS)0, Assert.Single(plain).Anonymous.ki.dwFlags);
    }

    // Evdev numbers: 272 left, 273 right, 274 middle, 275 side, 276 extra.
    [Theory]
    [InlineData(272, (uint)MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, (uint)MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, 0u)]
    [InlineData(273, (uint)MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN, (uint)MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP, 0u)]
    [InlineData(274, (uint)MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEDOWN, (uint)MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP, 0u)]
    [InlineData(275, (uint)MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN, (uint)MOUSE_EVENT_FLAGS.MOUSEEVENTF_XUP, 1u)]
    [InlineData(276, (uint)MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN, (uint)MOUSE_EVENT_FLAGS.MOUSEEVENTF_XUP, 2u)]
    public void AMouseButtonIsAMouseEvent(int button, uint down, uint up, uint data)
    {
        var stroke = new KeyStroke(0, 0, Extended: false, MouseButton: button);

        var inputs = Win32KeyRelaySender.Build(new KeyRelay(null, $"mouse:{button}", KeyRelayPhase.Tap), stroke, _ => false, _ => false);

        Assert.Equal(2, inputs.Count);
        Assert.All(inputs, input => Assert.Equal(INPUT_TYPE.INPUT_MOUSE, input.type));
        Assert.All(inputs, input => Assert.Equal(data, input.Anonymous.mi.mouseData));
        Assert.All(inputs, input => Assert.Equal(KeyboardHook.InjectedTag, input.Anonymous.mi.dwExtraInfo));
        Assert.Equal([down, up], inputs.Select(input => (uint)input.Anonymous.mi.dwFlags));
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
