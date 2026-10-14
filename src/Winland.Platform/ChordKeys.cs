
namespace Winland.Platform;

/// <summary>Maps virtual-key codes onto physical keys. Every <see cref="ChordKey"/> has a case.</summary>
internal static class ChordKeys
{
    private static readonly Dictionary<int, ChordKey> Bindable = Build();

    /// <summary>Throws when two chord keys share a virtual-key code.</summary>
    public static void EnsureComplete()
    {
        _ = Bindable;
    }

    /// <summary>The physical key for a virtual-key code.</summary>
    public static PhysicalKey ToPhysical(int vk)
    {
        if (vk is Native.VkLWin or Native.VkRWin)
        {
            return PhysicalKey.Super(vk);
        }

        if (vk is Native.VkShift or Native.VkLShift or Native.VkRShift)
        {
            return PhysicalKey.Shift(vk);
        }

        if (vk is Native.VkControl or Native.VkLControl or Native.VkRControl)
        {
            return PhysicalKey.Ctrl(vk);
        }

        if (vk is Native.VkMenu or Native.VkLMenu or Native.VkRMenu)
        {
            return PhysicalKey.Alt(vk);
        }

        return Bindable.TryGetValue(vk, out var key)
            ? PhysicalKey.Bindable(vk, key)
            : PhysicalKey.Plain(vk);
    }

    private static Dictionary<int, ChordKey> Build()
    {
        var map = new Dictionary<int, ChordKey>();
        foreach (var key in Enum.GetValues<ChordKey>())
        {
            map.Add(VirtualKey(key), key);
        }

        return map;
    }

    // CS8524 is the unnamed-cast case. A new ChordKey still fails the build until it has a case.
#pragma warning disable CS8524
    private static int VirtualKey(ChordKey key) => key switch
    {
        ChordKey.D1 => 0x31,
        ChordKey.D2 => 0x32,
        ChordKey.D3 => 0x33,
        ChordKey.D4 => 0x34,
        ChordKey.D5 => 0x35,
        ChordKey.D6 => 0x36,
        ChordKey.D7 => 0x37,
        ChordKey.D8 => 0x38,
        ChordKey.D9 => 0x39,
        ChordKey.Left => 0x25,
        ChordKey.Up => 0x26,
        ChordKey.Right => 0x27,
        ChordKey.Down => 0x28,
        ChordKey.Tab => 0x09,
        ChordKey.Return => 0x0D,
        ChordKey.Space => 0x20,
        ChordKey.Escape => 0x1B,
        ChordKey.W => 0x57,
        ChordKey.Q => 0x51,
        ChordKey.T => 0x54,
        ChordKey.F => 0x46,
        ChordKey.G => 0x47,
        ChordKey.L => 0x4C,
        ChordKey.J => 0x4A,
        ChordKey.C => 0x43,
        ChordKey.X => 0x58,
        ChordKey.V => 0x56,
        ChordKey.I => 0x49,
        ChordKey.S => 0x53,
        ChordKey.Minus => 0xBD,
        ChordKey.Equal => 0xBB,
        ChordKey.Backspace => 0x08,
        ChordKey.Comma => 0xBC,
        ChordKey.F9 => 0x78,
        ChordKey.PrintScreen => 0x2C,
        ChordKey.P => 0x50,
        ChordKey.A => 0x41,
        ChordKey.B => 0x42,
        ChordKey.D => 0x44,
        ChordKey.E => 0x45,
        ChordKey.O => 0x4F,
        ChordKey.Grave => 0xC0,
        ChordKey.K => 0x4B,
        ChordKey.R => 0x52,
        ChordKey.H => 0x48,
        ChordKey.M => 0x4D,
        ChordKey.N => 0x4E,
        ChordKey.U => 0x55,
        ChordKey.Y => 0x59,
        ChordKey.Z => 0x5A,
        ChordKey.Slash => 0xBF,
        ChordKey.Period => 0xBE,
        ChordKey.LeftBracket => 0xDB,
        ChordKey.RightBracket => 0xDD,
        ChordKey.Home => 0x24,
        ChordKey.Delete => 0x2E,
    };
#pragma warning restore CS8524
}
