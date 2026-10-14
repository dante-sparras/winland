using System.Runtime.InteropServices;

namespace Winland.Platform;

/// <summary>Builds <see cref="Native.Input"/> events that Windows will actually release.</summary>
internal static class KeyStroke
{
    /// <summary>Marks a stroke this process sent, so the hook can tell it from another hook's replay.</summary>
    internal const nint OwnStroke = 0x4F4D41;
    /// <summary>Throws before a hook is installed when <c>INPUT</c> would be the wrong size.</summary>
    internal static void EnsureLayout()
    {
        var expected = nint.Size == 8 ? 40 : 28;
        var actual = Marshal.SizeOf<Native.Input>();
        if (actual != expected)
        {
            throw new InvalidOperationException($"INPUT is {actual} bytes; SendInput needs {expected} or a Super key-up is dropped.");
        }
    }

    /// <summary>How many events Windows accepted. Fewer than <paramref name="inputs"/> means a hook stopped the rest.</summary>
    internal static uint Send(params Native.Input[] inputs)
    {
        if (inputs.Length == 0)
        {
            return 0;
        }

        return Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.Input>());
    }

    /// <summary>Key-up for both Windows keys. A dummy key first keeps that release from opening Start.</summary>
    internal static void ReleaseSuper() => Send(Down(0xFF), Up(0xFF), Up(Native.VkLWin), Up(Native.VkRWin));

    /// <summary>
    /// Key-up for every key whose down Windows accepted and whose up it did not.
    /// A short <c>SendInput</c> otherwise leaves a letter or an arrow down.
    /// </summary>
    internal static void ReleaseSentDowns(Native.Input[] inputs, uint sent)
    {
        if (sent == 0 || inputs.Length == 0)
        {
            return;
        }

        var open = new HashSet<ushort>();
        var limit = (int)Math.Min(sent, (uint)inputs.Length);
        for (var i = 0; i < limit; i++)
        {
            var key = inputs[i].U.Ki;
            if ((key.DwFlags & Native.KeyeventfKeyup) != 0)
            {
                open.Remove(key.WVk);
            }
            else if (key.WVk != 0)
            {
                open.Add(key.WVk);
            }
        }

        if (open.Count == 0)
        {
            return;
        }

        var ups = new Native.Input[open.Count];
        var index = 0;
        foreach (var vk in open)
        {
            ups[index++] = Up(vk);
        }

        Send(ups);
    }

    /// <summary>Key-up for Shift, Ctrl, and Alt, left and right. A key that is already up is unchanged.</summary>
    internal static void ReleaseModifiers() => Send(
        Up(Native.VkLShift), Up(Native.VkRShift),
        Up(Native.VkLControl), Up(Native.VkRControl),
        Up(Native.VkLMenu), Up(Native.VkRMenu));

    internal static Native.Input Down(ushort vk) => Make(vk, up: false);

    internal static Native.Input Up(ushort vk) => Make(vk, up: true);

    private static Native.Input Make(ushort vk, bool up)
    {
        var flags = up ? Native.KeyeventfKeyup : 0u;
        var scan = (ushort)Native.MapVirtualKey(vk, 0);
        if (vk is Native.VkLWin or Native.VkRWin)
        {
            // Super is an extended key. A key-up without this flag does not clear it,
            // and every following letter is delivered as a Win chord until reboot.
            flags |= Native.KeyeventfExtendedKey;
            if (scan == 0)
            {
                scan = vk;
            }
        }

        if (IsExtended(vk))
        {
            flags |= Native.KeyeventfExtendedKey;
        }

        return Event(vk, scan, flags);
    }

    private static Native.Input Event(ushort vk, ushort scan, uint flags) => new()
    {
        Type = Native.InputKeyboard,
        U = new Native.InputUnion
        {
            Ki = new Native.KeyboardInput
            {
                WVk = vk,
                WScan = scan,
                DwFlags = flags,
                DwExtraInfo = OwnStroke,
            },
        },
    };

    private static bool IsExtended(ushort vk) => vk is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28
        or 0x2C or 0x2D or 0x2E
        or 0x5D
        or 0x6F or 0x90
        or 0xA3 or 0xA5;
}
