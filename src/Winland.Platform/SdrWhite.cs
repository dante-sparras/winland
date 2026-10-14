using System.Runtime.InteropServices;

namespace Winland.Platform;

/// <summary>
/// How bright ordinary SDR content is drawn on a monitor, in scRGB units where 1.0 is 80 nits.
/// With HDR on, Windows draws SDR windows at the SDR content brightness the person chose, so a linear copy of the screen
/// has to be divided by this before it is shown again as an SDR picture. With HDR off it is 1.0.
/// </summary>
internal static class SdrWhite
{
    private const uint ActivePaths = 2;
    private const int PathSize = 72;
    private const int ModeSize = 64;
    private const int GetSourceName = 1;
    private const int GetAdvancedColor = 9;
    private const int GetSdrWhiteLevel = 11;
    private const uint AdvancedColorEnabled = 0x2;

    /// <summary>The SDR white level of <paramref name="monitor"/>, or 1.0 when HDR is off or Windows does not say.</summary>
    /// <param name="monitor">A monitor handle.</param>
    /// <returns>scRGB units: 1.0 is 80 nits.</returns>
    public static double For(nint monitor)
    {
        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
        {
            return 1.0;
        }

        if (GetDisplayConfigBufferSizes(ActivePaths, out var pathCount, out var modeCount) != 0)
        {
            return 1.0;
        }

        var paths = new byte[pathCount * PathSize];
        var modes = new byte[modeCount * ModeSize];
        if (QueryDisplayConfig(ActivePaths, ref pathCount, paths, ref modeCount, modes, 0) != 0)
        {
            return 1.0;
        }

        for (var i = 0; i < pathCount; i++)
        {
            var offset = i * PathSize;
            var source = new SourceName
            {
                Header = Header(GetSourceName, Marshal.SizeOf<SourceName>(), paths, offset, offset + 8),
            };
            if (DisplayConfigGetDeviceInfo(ref source) != 0 || !string.Equals(source.Device, info.Device, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var color = new AdvancedColor { Header = Header(GetAdvancedColor, Marshal.SizeOf<AdvancedColor>(), paths, offset + 20, offset + 28) };
            if (DisplayConfigGetDeviceInfo(ref color) != 0 || (color.Value & AdvancedColorEnabled) == 0)
            {
                return 1.0;
            }

            var white = new WhiteLevel { Header = Header(GetSdrWhiteLevel, Marshal.SizeOf<WhiteLevel>(), paths, offset + 20, offset + 28) };
            return DisplayConfigGetDeviceInfo(ref white) == 0 && white.Level > 0 ? white.Level / 1000.0 : 1.0;
        }

        return 1.0;
    }

    private static ConfigHeader Header(int type, int size, byte[] paths, int luidOffset, int idOffset) => new()
    {
        Type = type,
        Size = size,
        AdapterLow = BitConverter.ToUInt32(paths, luidOffset),
        AdapterHigh = BitConverter.ToInt32(paths, luidOffset + 4),
        Id = BitConverter.ToUInt32(paths, idOffset),
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct ConfigHeader
    {
        public int Type;
        public int Size;
        public uint AdapterLow;
        public int AdapterHigh;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceName
    {
        public ConfigHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColor
    {
        public ConfigHeader Header;
        public uint Value;
        public int Encoding;
        public int BitsPerChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WhiteLevel
    {
        public ConfigHeader Header;
        public uint Level;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Native.Rect Monitor;
        public Native.Rect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, byte[] paths, ref uint modeCount, byte[] modes, nint topology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref SourceName info);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColor info);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref WhiteLevel info);
}
