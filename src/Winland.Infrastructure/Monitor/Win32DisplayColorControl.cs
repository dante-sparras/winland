using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dxgi;

namespace Winland.Infrastructure;

/// <summary>
/// Reads Windows' HDR and wide color switches. The bits per channel have no switch of their own: advanced color is
/// what raises them above 8. Winland reads the switches and does not change them.
/// </summary>
internal static class Win32DisplayColorControl
{
    private const int GetAdvancedColorInfo = 9;
    private const int GetAdvancedColorInfo2 = 15;

    /// <summary>
    /// Reads the switches of the panel display config names by its adapter and target. <paramref name="panels"/> is
    /// what <see cref="Panels"/> read for this pass.
    /// </summary>
    internal static bool TryDescribe(string device, LUID adapter, uint target, IReadOnlyDictionary<string, Panel> panels,
        out DisplayColorReading reading)
    {
        reading = default;
        if (device.Length == 0)
            return false;

        var split = TryRead2(adapter, target, out reading);
        if (!split && !TryRead1(adapter, target, out reading))
            return false;

        if (panels.TryGetValue(device, out var panel))
        {
            reading = reading with
            {
                MinLuminance = panel.Min,
                MaxLuminance = panel.Max,
                MaxAvgLuminance = panel.Average,
                Chroma = panel.Chroma,
            };
        }

        return true;
    }

    /// <summary>A panel's mastering luminance, and whether it reports its color primaries.</summary>
    internal readonly record struct Panel(float Min, float Max, float Average, bool Chroma);

    private static unsafe bool TryRead2(LUID adapter, uint target, out DisplayColorReading reading)
    {
        var info = new ColorInfo2();
        info.Header.type = (DISPLAYCONFIG_DEVICE_INFO_TYPE)GetAdvancedColorInfo2;
        info.Header.size = (uint)sizeof(ColorInfo2);
        info.Header.adapterId = adapter;
        info.Header.id = target;
        if (PInvoke.DisplayConfigGetDeviceInfo(ref Unsafe.As<ColorInfo2, DISPLAYCONFIG_DEVICE_INFO_HEADER>(ref info)) != 0)
        {
            reading = default;
            return false;
        }

        // Bit 0 is advanced color supported, 4 HDR supported, 5 HDR on, 6 wide color supported, 7 wide color on.
        reading = new DisplayColorReading(
            Bit(info.Value, 4),
            Bit(info.Value, 6),
            Bit(info.Value, 5),
            Bit(info.Value, 7),
            Bit(info.Value, 0),
            true,
            Bits(info.BitsPerColorChannel));
        return true;
    }

    private static unsafe bool TryRead1(LUID adapter, uint target, out DisplayColorReading reading)
    {
        var info = new ColorInfo();
        info.Header.type = (DISPLAYCONFIG_DEVICE_INFO_TYPE)GetAdvancedColorInfo;
        info.Header.size = (uint)sizeof(ColorInfo);
        info.Header.adapterId = adapter;
        info.Header.id = target;
        if (PInvoke.DisplayConfigGetDeviceInfo(ref Unsafe.As<ColorInfo, DISPLAYCONFIG_DEVICE_INFO_HEADER>(ref info)) != 0)
        {
            reading = default;
            return false;
        }

        // Bit 0 is advanced color supported, 1 on. It is HDR; wide color has no switch of its own here.
        var supported = Bit(info.Value, 0);
        reading = new DisplayColorReading(supported, false, Bit(info.Value, 1), false, supported, false, Bits(info.BitsPerColorChannel));
        return true;
    }

    /// <summary>
    /// Every panel DXGI lists, keyed by its device name, read with one factory for a whole Monitor read. A failure
    /// leaves out the panels not read yet, whose luminance stays unknown.
    /// </summary>
    internal static Dictionary<string, Panel> Panels()
    {
        var panels = new Dictionary<string, Panel>(StringComparer.OrdinalIgnoreCase);
        IDXGIFactory1? factory = null;
        try
        {
            if (PInvoke.CreateDXGIFactory1(out factory).Failed || factory is null)
                return panels;

            for (uint adapterIndex = 0; ; adapterIndex++)
            {
                IDXGIAdapter1? adapter = null;
                try
                {
                    if (factory.EnumAdapters1(adapterIndex, out adapter).Failed || adapter is null)
                        break;

                    for (uint outputIndex = 0; ; outputIndex++)
                    {
                        IDXGIOutput? output = null;
                        try
                        {
                            if (adapter.EnumOutputs(outputIndex, out output).Failed || output is null)
                                break;

                            if (output is not IDXGIOutput6 detailed)
                                continue;

                            var described = detailed.GetDesc1();
                            panels[PanelName(described.DeviceName)] = new Panel(
                                described.MinLuminance,
                                described.MaxLuminance,
                                described.MaxFullFrameLuminance,
                                Primary(described.RedPrimary)
                                    || Primary(described.GreenPrimary)
                                    || Primary(described.BluePrimary)
                                    || Primary(described.WhitePoint));
                        }
                        finally
                        {
                            Release(output);
                        }
                    }
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
        finally
        {
            Release(factory);
        }

        return panels;
    }

    private static void Release(object? com)
    {
        if (com is null)
            return;

        try
        {
            _ = Marshal.ReleaseComObject(com);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
    }

    private static string PanelName(__char_32 name) => name.ToString().Trim();

    private static bool Primary(__float_2 primary)
    {
        var values = primary.AsReadOnlySpan();
        return values[0] != 0 || values[1] != 0;
    }

    private static bool Bit(uint value, int bit) => (value & (1u << bit)) != 0;

    private static int Bits(uint value) => value > int.MaxValue ? int.MaxValue : (int)value;

#pragma warning disable CS0649 // Windows fills these in.
    private struct ColorInfo
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
    }

    private struct ColorInfo2
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
        public uint ActiveColorMode;
    }
#pragma warning restore CS0649
}
