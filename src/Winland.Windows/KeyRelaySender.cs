using System.Globalization;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winland.Windows;

internal interface IKeyRelaySender
{
    void Send(KeyRelay relay);
}

/// <summary>
/// Sends a relayed key with SendInput. Windows delivers keys to the foreground window, so a relay to
/// another window brings that window forward, sends, and gives the foreground back. Every event
/// carries the injected tag, so the hooks let it through and it never matches a bind.
/// </summary>
internal sealed class Win32KeyRelaySender : IKeyRelaySender
{
    // Long enough for the input to reach the target's queue before the foreground moves back.
    private const int SettleMs = 30;

    private static readonly (Modifier Modifier, ushort Left, ushort Right)[] Modifiers =
    [
        (Modifier.Super, 0x5B, 0x5C),
        (Modifier.Ctrl, 0xA2, 0xA3),
        (Modifier.Alt, 0xA4, 0xA5),
        (Modifier.Shift, 0xA0, 0xA1),
    ];

    public void Send(KeyRelay relay)
    {
        ArgumentNullException.ThrowIfNull(relay);
        if (!KeyStrokes.TryResolve(relay.Key, relay.Code, Layout(), out var stroke))
        {
            Console.Error.WriteLine($"The key '{relay.Key}' was not sent: it has no key on this layout.");
            return;
        }

        var previous = PInvoke.GetForegroundWindow();
        var moved = false;
        if (relay.Target is WindowId target && target.Value != ((nint)previous).ToInt64())
        {
            if (!Foreground.MoveTo(target))
            {
                Console.Error.WriteLine(
                    "The key was not sent: window 0x"
                    + target.Value.ToString("x", CultureInfo.InvariantCulture)
                    + " did not take the foreground.");
                return;
            }

            moved = true;
        }

        KeyboardHook.Send([.. Build(relay, stroke, Down, KeyboardHook.Held)]);
        if (!stroke.IsMouse && relay.Phase != KeyRelayPhase.Tap)
            KeyboardHook.NoteRelayed(stroke.VirtualKey, down: relay.Phase == KeyRelayPhase.Press);

        if (!moved || previous.IsNull)
            return;

        Thread.Sleep(SettleMs);
        if (relay.Target is WindowId sent && PInvoke.GetForegroundWindow() == new HWND((nint)sent.Value))
            _ = Foreground.MoveTo(new WindowId(((nint)previous).ToInt64()));
    }

    private static KeyLayout Layout()
    {
        try
        {
            return KeyboardLayout.Read(KeyboardLayout.ForegroundHandle());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return KeyLayout.UnitedStates;
        }
    }

    // Exactly the requested modifiers are down around the key: ones Windows holds that are not
    // wanted are let go, and wanted ones it does not hold are pressed and let go. A let-go modifier
    // is pressed again only when the user still holds it (held). Windows can still hold one whose
    // release is on its way; pressed again, it would land after that release and stay down.
    internal static List<INPUT> Build(KeyRelay relay, KeyStroke stroke, Func<int, bool> down, Func<int, bool> held)
    {
        var inputs = new List<INPUT>();
        var after = new List<INPUT>();
        var masked = false;
        if (relay.Modifiers is Modifier wanted)
        {
            foreach (var (modifier, left, right) in Modifiers)
            {
                var leftDown = down(left);
                var rightDown = down(right);
                if (wanted.HasFlag(modifier))
                {
                    if (leftDown || rightDown)
                        continue;

                    inputs.Add(KeyboardHook.Key(left, up: false));
                    after.Insert(0, KeyboardHook.Key(left, up: true));
                    continue;
                }

                foreach (var (key, isDown) in new[] { (left, leftDown), (right, rightDown) })
                {
                    if (!isDown)
                        continue;

                    masked |= modifier is Modifier.Super or Modifier.Alt;
                    inputs.Add(KeyboardHook.Key(key, up: true));
                    if (held(key))
                        after.Insert(0, KeyboardHook.Key(key, up: false));
                }
            }
        }

        // Letting go of Super or Alt alone opens Start or a menu bar. The mask key makes it a chord.
        if (masked)
            inputs.InsertRange(0, Mask());

        if (relay.Phase != KeyRelayPhase.Release)
            inputs.Add(Stroke(stroke, up: false));
        if (relay.Phase != KeyRelayPhase.Press)
            inputs.Add(Stroke(stroke, up: true));

        inputs.AddRange(after);
        if (masked)
            inputs.AddRange(Mask());

        return inputs;
    }

    private static INPUT[] Mask() =>
        [KeyboardHook.Key(KeyboardHook.MaskKey, up: false), KeyboardHook.Key(KeyboardHook.MaskKey, up: true)];

    private static INPUT Stroke(KeyStroke stroke, bool up)
    {
        if (stroke.IsMouse)
            return Button(stroke.MouseButton, up);

        var flags = up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;
        if (stroke.Extended)
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
        if (stroke.VirtualKey == 0)
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_SCANCODE;

        return new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                ki = new KEYBDINPUT
                {
                    wVk = (VIRTUAL_KEY)stroke.VirtualKey,
                    wScan = (ushort)stroke.Scan,
                    dwFlags = flags,
                    dwExtraInfo = KeyboardHook.InjectedTag,
                },
            },
        };
    }

    // Evdev buttons: 272 left, 273 right, 274 middle, 275 side (X1), 276 extra (X2).
    private static INPUT Button(int button, bool up)
    {
        var (flags, data) = button switch
        {
            272 => (up ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP : MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, 0),
            273 => (up ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP : MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN, 0),
            274 => (up ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP : MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEDOWN, 0),
            275 => (up ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_XUP : MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN, 1),
            _ => (up ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_XUP : MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN, 2),
        };
        return new INPUT
        {
            type = INPUT_TYPE.INPUT_MOUSE,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = flags,
                    mouseData = (uint)data,
                    dwExtraInfo = KeyboardHook.InjectedTag,
                },
            },
        };
    }

    private static bool Down(int vk) => (PInvoke.GetAsyncKeyState(vk) & 0x8000) != 0;
}
