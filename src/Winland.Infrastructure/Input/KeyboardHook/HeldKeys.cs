using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winland.Infrastructure;

/// <summary>Which keys the user holds, as the hook saw them: reconciling them after a lock or a lost release, and injecting keys.</summary>
public sealed partial class KeyboardHook : IDisposable
{
    private void OnSession(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is not (Microsoft.Win32.SessionSwitchReason.SessionLock or Microsoft.Win32.SessionSwitchReason.SessionUnlock))
            return;

        var inhibited = e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock;
        _session.Inhibit(inhibited);

        // Keys released on the lock screen were never seen. The session lives on the hook thread.
        if (!inhibited)
            Post(ReconcileMessage);
    }

    private void Reconcile()
    {
        _session.Forget(IsDown);
        for (var slot = 0; slot < _pressedNames.Length; slot++)
        {
            if (!Down(slot & 0xFF))
                _pressedNames[slot] = null;
        }

        // A drag whose button or key was let go on the lock screen is over, or the window would follow the cursor.
        if (Drag.Current is BindDrag.Active dragging && !IsDown(dragging.Button) && Drag.Abandon() is BindDrag.Active drag)
            PostGesture(drag.Dispatcher, drag.Arguments, CursorPoint(), PointerDragPhase.End, drag.Button);

        // A release the hook missed leaves its bit set. A key still held sets it again when it repeats.
        var held = Volatile.Read(ref _held);
        for (var i = 0; i < Modifiers.Length; i++)
        {
            if (!Down(Modifiers[i].Vk))
                held &= ~(1 << i);
        }

        Volatile.Write(ref _held, held);
        ArmSweep();
    }

    /// <summary>
    /// The watch tick, on the hook thread. A modifier can be left down at any time, for one by a
    /// relay that pressed it again just after the user let go, so the sweep runs on every tick.
    /// It waits while the hook may have missed a key, or a held key would be taken as stale.
    /// </summary>
    internal void Watch()
    {
        if (_disposed || _session.Inhibited || Environment.TickCount64 - _startedAt < SweepDelayMs || !Current())
            return;

        ReleaseStale();
    }

    // True when the hook has seen the newest input, so what it holds is up to date.
    private bool Current()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!PInvoke.GetLastInputInfo(ref info))
            return false;

        return unchecked((int)info.dwTime - Volatile.Read(ref _lastSeen)) <= CurrentSlackMs;
    }

    // A modifier the OS holds down that the user is not holding: left by a release lost
    // before this daemon ran, on a desktop the hook cannot see, or by a relay. Release it.
    private static void ReleaseStale()
    {
        var kept = Volatile.Read(ref _held) | Volatile.Read(ref _relayed);
        for (var i = 0; i < Modifiers.Length; i++)
        {
            var (name, vk) = Modifiers[i];
            if ((kept & (1 << i)) != 0 || !Down(vk))
                continue;

            Console.Error.WriteLine($"Releasing {name}, which was left down.");

            // Letting go of Super or Alt alone opens Start or a menu bar. The mask key makes it a chord.
            if (vk is 0x5B or 0x5C or 0xA4 or 0xA5)
                InputInjector.Post(Key(MaskKey, up: false), Key(MaskKey, up: true), Key(vk, up: true));
            else
                InputInjector.Post(Key(vk, up: true));
        }
    }

    /// <summary>True when the user physically holds this modifier key, as the hook last saw.</summary>
    internal static bool Held(int vk)
    {
        var bit = ModifierBit(vk);
        return bit != 0 && (Volatile.Read(ref _held) & bit) != 0;
    }

    /// <summary>A relay pressed a modifier and left it down on purpose, or let it go.</summary>
    internal static void NoteRelayed(int vk, bool down)
    {
        var bit = ModifierBit(vk);
        if (bit == 0)
            return;

        if (down)
            Interlocked.Or(ref _relayed, bit);
        else
            Interlocked.And(ref _relayed, ~bit);
    }

    private static void NoteHeld(uint vk, bool up)
    {
        var bit = ModifierBit((int)vk);
        if (bit == 0)
            return;

        var held = Volatile.Read(ref _held);
        Volatile.Write(ref _held, up ? held & ~bit : held | bit);
    }

    private static int ModifierBit(int vk)
    {
        for (var i = 0; i < Modifiers.Length; i++)
        {
            if (Modifiers[i].Vk == vk)
                return 1 << i;
        }

        return 0;
    }

    // Called from the mouse hook, so it is sent from the injector's thread.
    internal static void Mask() => InputInjector.Post(Key(MaskKey, up: false), Key(MaskKey, up: true));

    internal static void Send(params ReadOnlySpan<INPUT> inputs)
    {
        if (PInvoke.SendInput(inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            Console.Error.WriteLine($"Input was not sent ({Marshal.GetLastPInvokeError()}).");
    }

    internal static INPUT Key(ushort vk, bool up, ushort scan = 0, bool extended = false)
    {
        if (scan == 0)
            scan = (ushort)PInvoke.MapVirtualKey(vk, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC);

        var flags = up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;
        if (extended || vk is 0x5B or 0x5C or 0xA3 or 0xA5 or 0x90)
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;

        return new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                ki = new KEYBDINPUT
                {
                    wVk = (VIRTUAL_KEY)vk,
                    wScan = scan,
                    dwFlags = flags,
                    dwExtraInfo = InjectedTag,
                },
            },
        };
    }

    private static bool Down(int vk) => (PInvoke.GetAsyncKeyState(vk) & 0x8000) != 0;
}
