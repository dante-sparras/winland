using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winland.Windows;

/// <summary>
/// Reads unshifted characters for one layout. The keyboard state array is private and starts cleared,
/// so a latched modifier is never copied, and the query flag keeps the call from changing dead-key state.
/// </summary>
public static class KeyboardLayout
{
    private const uint DoNotChangeState = 0x4;

    public static KeyLayout Active()
    {
        try
        {
            return Read(ForegroundHandle());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return KeyLayout.UnitedStates;
        }
    }

    internal static unsafe nint ForegroundHandle()
    {
        var hwnd = PInvoke.GetForegroundWindow();
        var thread = hwnd.IsNull ? 0u : PInvoke.GetWindowThreadProcessId(hwnd, out _);
        if (thread == 0)
            thread = PInvoke.GetCurrentThreadId();

        return (nint)PInvoke.GetKeyboardLayout(thread).Value;
    }

    internal static unsafe KeyLayout Read(nint handle)
    {
        var layout = new HKL((void*)handle);
        var characters = new Dictionary<int, string>();
        var dead = new HashSet<int>();
        Span<byte> state = stackalloc byte[256];
        Span<char> buffer = stackalloc char[8];

        for (var virtualKey = 1; virtualKey < 256; virtualKey++)
        {
            state.Clear();
            var count = Translate(virtualKey, state, buffer, layout);
            if (count < 0)
                dead.Add(virtualKey);
            else if (count > 0)
                characters[virtualKey] = new string(buffer[..count]);
        }

        return KeyLayout.From(characters, HasAltGr(layout, characters), dead);
    }

    private static bool HasAltGr(HKL layout, Dictionary<int, string> unshifted)
    {
        Span<byte> state = stackalloc byte[256];
        Span<char> buffer = stackalloc char[8];
        for (var virtualKey = 1; virtualKey < 256; virtualKey++)
        {
            if (virtualKey is 0x10 or 0x11 or 0x12 or >= 0xA0 and <= 0xA5 or 0xE5 or 0xE7)
                continue;

            state.Clear();
            state[0x11] = 0x80;
            state[0x12] = 0x80;
            state[0xA2] = 0x80;
            state[0xA5] = 0x80;
            var count = Translate(virtualKey, state, buffer, layout);
            if (count <= 0)
                continue;

            var produced = buffer[..count];
            if (produced.Length == 1 && produced[0] < ' ')
                continue;

            if (!unshifted.TryGetValue(virtualKey, out var plain) || !produced.SequenceEqual(plain))
                return true;
        }

        return false;
    }

    private static unsafe int Translate(int virtualKey, Span<byte> state, Span<char> buffer, HKL layout)
    {
        fixed (byte* statePointer = state)
        fixed (char* bufferPointer = buffer)
        {
            return PInvoke.ToUnicodeEx((uint)virtualKey, 0, statePointer, bufferPointer, buffer.Length, DoNotChangeState, layout);
        }
    }
}
