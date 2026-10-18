using System.Runtime.CompilerServices;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Winland.Windows;

/// <summary>
/// Sets a display's mode and scale as a monitor rule asks. A display already in that mode, or at that scale, is left as
/// it is, so the display change a new mode causes does not loop. Like a rotation, the change is kept by Windows.
/// </summary>
// Stryker disable once all : changes the real displays; the choices are made and tested in DisplaySettings.
internal static class DisplayModeAdapter
{
    public static unsafe bool TryApplyToSystem(DisplayModeRequest request)
    {
        if (request.Device.Length == 0 || !TryCurrent(request.Device, out var current))
            return false;

        var offered = new List<DisplayModeChoice>();
        var devmode = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
        fixed (char* name = request.Device)
        {
            for (uint index = 0; PInvoke.EnumDisplaySettingsEx(name, (ENUM_DISPLAY_SETTINGS_MODE)index, &devmode, default); index++)
            {
                // Modes of another colour depth or rotation are not offered, so only the size and rate change.
                if (devmode.dmBitsPerPel == current.dmBitsPerPel
                    && devmode.Anonymous1.Anonymous2.dmDisplayOrientation == current.Anonymous1.Anonymous2.dmDisplayOrientation)
                    offered.Add(new DisplayModeChoice((int)devmode.dmPelsWidth, (int)devmode.dmPelsHeight, (int)devmode.dmDisplayFrequency));
            }
        }

        if (DisplaySettings.Choose(offered, request.Mode) is not DisplayModeChoice chosen)
        {
            Console.Error.WriteLine($"The display {request.Device} offers no mode {request.Mode}.");
            return false;
        }

        if (chosen.Width == current.dmPelsWidth && chosen.Height == current.dmPelsHeight && chosen.Rate == current.dmDisplayFrequency)
            return true;

        current.dmFields = DEVMODE_FIELD_FLAGS.DM_PELSWIDTH | DEVMODE_FIELD_FLAGS.DM_PELSHEIGHT | DEVMODE_FIELD_FLAGS.DM_DISPLAYFREQUENCY;
        current.dmPelsWidth = (uint)chosen.Width;
        current.dmPelsHeight = (uint)chosen.Height;
        current.dmDisplayFrequency = (uint)chosen.Rate;
        fixed (char* name = request.Device)
        {
            var result = PInvoke.ChangeDisplaySettingsEx(new PCWSTR(name), &current, default, CDS_TYPE.CDS_UPDATEREGISTRY, null);
            return result == DISP_CHANGE.DISP_CHANGE_SUCCESSFUL;
        }
    }

    // Windows keeps a display's scale as a step from the one it recommends. Reading gives the lowest, current and highest
    // steps; the recommended scale is the one at step 0.
    public static unsafe bool TryApplyScaleToSystem(DisplayScaleRequest request)
    {
        if (!MonitorAdapter.TrySource(request.Device, out var adapter, out var source))
            return false;

        var get = new ScaleGet();
        get.Header.type = (DISPLAYCONFIG_DEVICE_INFO_TYPE)(-3);
        get.Header.size = (uint)sizeof(ScaleGet);
        get.Header.adapterId = adapter;
        get.Header.id = source;
        if (PInvoke.DisplayConfigGetDeviceInfo(ref Unsafe.As<ScaleGet, DISPLAYCONFIG_DEVICE_INFO_HEADER>(ref get)) != 0)
            return false;

        var percents = DisplaySettings.ScalePercents;
        var recommended = Math.Abs(get.Minimum);
        var wanted = IndexOf(percents, DisplaySettings.NearestPercent(request.Scale));
        var step = Math.Clamp(wanted - recommended, get.Minimum, get.Maximum);
        if (step == get.Current)
            return true;

        var set = new ScaleSet();
        set.Header.type = (DISPLAYCONFIG_DEVICE_INFO_TYPE)(-4);
        set.Header.size = (uint)sizeof(ScaleSet);
        set.Header.adapterId = adapter;
        set.Header.id = source;
        set.Step = step;
        return PInvoke.DisplayConfigSetDeviceInfo(in Unsafe.As<ScaleSet, DISPLAYCONFIG_DEVICE_INFO_HEADER>(ref set)) == 0;
    }

    private static int IndexOf(IReadOnlyList<int> percents, int percent)
    {
        for (var i = 0; i < percents.Count; i++)
        {
            if (percents[i] == percent)
                return i;
        }

        return 0;
    }

    private static unsafe bool TryCurrent(string device, out DEVMODEW devmode)
    {
        devmode = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
        fixed (char* name = device)
        fixed (DEVMODEW* pointer = &devmode)
        {
            return PInvoke.EnumDisplaySettingsEx(name, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, pointer, default);
        }
    }

#pragma warning disable CS0649 // Windows fills these in.
    private struct ScaleGet
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
        public int Minimum;
        public int Current;
        public int Maximum;
    }

#pragma warning restore CS0649

    private struct ScaleSet
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
        public int Step;
    }
}
