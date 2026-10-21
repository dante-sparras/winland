using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Winland.Core;

namespace Winland.Windows;

public static class MonitorAdapter
{
    public static IReadOnlyList<Winland.Core.Monitor> Read()
    {
        // -4 is per-monitor v2 awareness. The manifest asks for the same thing, and this call
        // covers a host that has not declared it yet. A second call fails, which is fine.
        _ = PInvoke.SetProcessDpiAwarenessContext((DPI_AWARENESS_CONTEXT)(nint)(-4));
        return Enumerate(Panels());
    }

    private static unsafe List<Winland.Core.Monitor> Enumerate(Dictionary<string, Panel> panels)
    {
        var monitors = new List<Winland.Core.Monitor>();
        PInvoke.EnumDisplayMonitors(
            default,
            null,
            (monitor, _, _, _) =>
            {
                if (TryRead(monitor, panels, out var described))
                    monitors.Add(described);
                return true;
            },
            default);
        return monitors;
    }

    private static unsafe bool TryRead(HMONITOR monitor, Dictionary<string, Panel> panels, out Winland.Core.Monitor described)
    {
        described = null!;
        var info = new MONITORINFOEXW();
        info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
            return false;

        var name = Text(info.szDevice);
        if (name.Length == 0)
            return false;

        var dpi = PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _);
        var scale = dpi.Succeeded && dpiX > 0 ? dpiX / 96.0 : 1;
        panels.TryGetValue(name, out var panel);
        var bounds = From(info.monitorInfo.rcMonitor);
        described = new Winland.Core.Monitor(
            name,
            panel.Description ?? "",
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            scale,
            true,
            From(info.monitorInfo.rcWork),
            Make: panel.Make ?? "",
            Model: panel.Model ?? "",
            Serial: panel.Serial ?? "",
            RefreshRate: panel.RefreshRate);
        if (Win32DisplayColorControl.TryDescribe(name, out var color))
        {
            described = described with
            {
                BitsPerChannel = color.BitsPerChannel,
                HdrCapable = color.HdrSupported,
                WideCapable = color.WideSupported,
                AdvancedCapable = color.AdvancedSupported,
                SeparateColor = color.Split,
                Chroma = color.Chroma,
                HdrActive = color.HdrOn,
                WideActive = color.WideOn,
                MinLuminance = color.MinLuminance,
                MaxLuminance = color.MaxLuminance,
                MaxAvgLuminance = color.MaxAvgLuminance,
            };
        }

        return true;
    }

    private readonly record struct Panel(string Description, string Make, string Model, string Serial, double RefreshRate);

    private static Dictionary<string, Panel> Panels()
    {
        var panels = new Dictionary<string, Panel>(StringComparer.OrdinalIgnoreCase);
        if (!TryPaths(out var paths))
            return panels;

        foreach (var path in paths)
        {
            var source = SourceName(path);
            if (source is null)
                continue;

            var target = ReadTarget(path);
            var parsed = Winland.Core.Edid.Parse(MonitorEdid.Read(target.DevicePath) ?? []);
            var make = parsed.Make.Length > 0 ? parsed.Make : target.Make;
            panels[source] = new Panel(target.Description, make, parsed.Model, parsed.Serial, Rate(path.targetInfo.refreshRate));
        }

        return panels;
    }

    /// <summary>The display-config source of a display, by its device name.</summary>
    internal static bool TrySource(string device, out LUID adapter, out uint source)
    {
        adapter = default;
        source = 0;
        if (!TryPaths(out var paths))
            return false;

        foreach (var path in paths)
        {
            if (!string.Equals(SourceName(path), device, StringComparison.OrdinalIgnoreCase))
                continue;

            adapter = path.sourceInfo.adapterId;
            source = path.sourceInfo.id;
            return true;
        }

        return false;
    }

    /// <summary>The display-config target of a display, the panel its source shows on, by its device name.</summary>
    internal static bool TryTarget(string device, out LUID adapter, out uint target)
    {
        adapter = default;
        target = 0;
        if (!TryPaths(out var paths))
            return false;

        foreach (var path in paths)
        {
            if (!string.Equals(SourceName(path), device, StringComparison.OrdinalIgnoreCase))
                continue;

            adapter = path.targetInfo.adapterId;
            target = path.targetInfo.id;
            return true;
        }

        return false;
    }

    private static bool TryPaths(out DISPLAYCONFIG_PATH_INFO[] paths) => TryConfig(out paths, out _);

    /// <summary>The active display-config paths and the modes they name.</summary>
    internal static bool TryConfig(out DISPLAYCONFIG_PATH_INFO[] paths, out DISPLAYCONFIG_MODE_INFO[] modes)
    {
        paths = [];
        modes = [];
        var flags = QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS;
        if (PInvoke.GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount) != WIN32_ERROR.ERROR_SUCCESS)
            return false;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var pathBuffer = pathCount == 0 ? [] : new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modeBuffer = modeCount == 0 ? [] : new DISPLAYCONFIG_MODE_INFO[modeCount];
            var error = PInvoke.QueryDisplayConfig(flags, ref pathCount, pathBuffer, ref modeCount, modeBuffer);
            if (error == WIN32_ERROR.ERROR_SUCCESS)
            {
                paths = pathCount == pathBuffer.Length ? pathBuffer : pathBuffer[..(int)pathCount];
                modes = modeCount == modeBuffer.Length ? modeBuffer : modeBuffer[..(int)modeCount];
                return true;
            }

            if (error != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
                return false;
        }

        return false;
    }

    internal static unsafe string? SourceName(DISPLAYCONFIG_PATH_INFO path)
    {
        var request = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
        request.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
        request.header.size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
        request.header.adapterId = path.sourceInfo.adapterId;
        request.header.id = path.sourceInfo.id;
        if (PInvoke.DisplayConfigGetDeviceInfo(ref request.header) != 0)
            return null;

        var name = Text(request.viewGdiDeviceName);
        return name.Length == 0 ? null : name;
    }

    internal readonly record struct Target(string Description, string Make, string DevicePath);

    internal static unsafe Target ReadTarget(DISPLAYCONFIG_PATH_INFO path)
    {
        var request = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
        request.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
        request.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
        request.header.adapterId = path.targetInfo.adapterId;
        request.header.id = path.targetInfo.id;
        if (PInvoke.DisplayConfigGetDeviceInfo(ref request.header) != 0)
            return new Target("", "", "");

        return new Target(
            Text(request.monitorFriendlyDeviceName),
            Manufacturer(request.edidManufactureId),
            Text(request.monitorDevicePath));
    }

    private static string Manufacturer(ushort id)
    {
        if (id == 0)
            return "";

        Span<char> letters = stackalloc char[3];
        letters[0] = (char)('@' + ((id >> 10) & 0x1F));
        letters[1] = (char)('@' + ((id >> 5) & 0x1F));
        letters[2] = (char)('@' + (id & 0x1F));
        if (letters[0] is < 'A' or > 'Z' || letters[1] is < 'A' or > 'Z' || letters[2] is < 'A' or > 'Z')
            return "";

        return new string(letters);
    }

    private static double Rate(DISPLAYCONFIG_RATIONAL rate) =>
        rate.Denominator == 0 ? 0 : rate.Numerator / (double)rate.Denominator;

    private static string Text(__char_32 buffer) => Trim(buffer.ToString());

    private static string Text(__char_64 buffer) => Trim(buffer.ToString());

    private static string Text(__char_128 buffer) => Trim(buffer.ToString());

    private static string Trim(string text)
    {
        var end = text.IndexOf('\0');
        if (end >= 0)
            text = text[..end];

        return text.Trim();
    }

    private static Rectangle From(RECT rect) =>
        new(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
}
