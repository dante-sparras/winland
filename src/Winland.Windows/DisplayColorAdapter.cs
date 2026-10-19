using System.Runtime.CompilerServices;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;

namespace Winland.Windows;

/// <summary>
/// Shows a display in the color mode its monitor rule's <c>cm</c> preset names, by turning Windows' HDR and wide color
/// switches. A display that shows that mode already is left alone, so the display change a switch causes does not loop.
/// Like the HDR switch in Settings, the change is kept by Windows.
/// </summary>
// Stryker disable once all : changes the real displays; what to turn is decided and tested in DisplayColors.
internal static class DisplayColorAdapter
{
    private const int GetAdvancedColorInfo = 9;
    private const int SetAdvancedColorState = 10;
    private const int GetAdvancedColorInfo2 = 15;
    private const int SetHdrState = 16;
    private const int SetWideColorState = 17;

    public static bool TryApplyToSystem(DisplayColorRequest request)
    {
        if (request.Device.Length == 0 || !MonitorAdapter.TryTarget(request.Device, out var adapter, out var target))
            return false;

        // Windows 11 24H2 has a switch each for HDR and wide color; before it there is one switch, for HDR.
        var split = TryRead2(adapter, target, out var display);
        if (!split && !TryRead(adapter, target, out display))
            return false;

        var plan = DisplayColors.Plan(request.Mode, display);
        if (plan.Unsupported is string missing)
            Console.Error.WriteLine($"The display {request.Device} cannot show {missing}, which cm {request.Preset} asks for.");

        var applied = true;
        if (plan.Hdr is bool hdr)
            applied &= Set(adapter, target, split ? SetHdrState : SetAdvancedColorState, hdr);

        if (plan.Wide is bool wide && split)
            applied &= Set(adapter, target, SetWideColorState, wide);

        return applied;
    }

    private static unsafe bool TryRead2(LUID adapter, uint target, out DisplayColorSupport display)
    {
        var info = new ColorInfo2();
        info.Header.type = (DISPLAYCONFIG_DEVICE_INFO_TYPE)GetAdvancedColorInfo2;
        info.Header.size = (uint)sizeof(ColorInfo2);
        info.Header.adapterId = adapter;
        info.Header.id = target;
        if (PInvoke.DisplayConfigGetDeviceInfo(ref Unsafe.As<ColorInfo2, DISPLAYCONFIG_DEVICE_INFO_HEADER>(ref info)) != 0)
        {
            display = default;
            return false;
        }

        // Bit 4 is HDR supported, 5 HDR on, 6 wide color supported, 7 wide color on.
        display = new DisplayColorSupport(Bit(info.Value, 4), Bit(info.Value, 6), Bit(info.Value, 5), Bit(info.Value, 7));
        return true;
    }

    private static unsafe bool TryRead(LUID adapter, uint target, out DisplayColorSupport display)
    {
        var info = new ColorInfo();
        info.Header.type = (DISPLAYCONFIG_DEVICE_INFO_TYPE)GetAdvancedColorInfo;
        info.Header.size = (uint)sizeof(ColorInfo);
        info.Header.adapterId = adapter;
        info.Header.id = target;
        if (PInvoke.DisplayConfigGetDeviceInfo(ref Unsafe.As<ColorInfo, DISPLAYCONFIG_DEVICE_INFO_HEADER>(ref info)) != 0)
        {
            display = default;
            return false;
        }

        // Bit 0 is advanced color supported, 1 on. It is HDR; wide color has no switch of its own here.
        display = new DisplayColorSupport(Bit(info.Value, 0), false, Bit(info.Value, 1), false);
        return true;
    }

    private static unsafe bool Set(LUID adapter, uint target, int type, bool on)
    {
        var state = new ColorState();
        state.Header.type = (DISPLAYCONFIG_DEVICE_INFO_TYPE)type;
        state.Header.size = (uint)sizeof(ColorState);
        state.Header.adapterId = adapter;
        state.Header.id = target;
        state.Value = on ? 1u : 0u;
        return PInvoke.DisplayConfigSetDeviceInfo(in Unsafe.As<ColorState, DISPLAYCONFIG_DEVICE_INFO_HEADER>(ref state)) == 0;
    }

    private static bool Bit(uint value, int bit) => (value & (1u << bit)) != 0;

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

    private struct ColorState
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
        public uint Value;
    }
}
