using System.Security;
using Microsoft.Win32;

namespace Winland.Infrastructure;

/// <summary>The EDID block Windows stores for a display-config target, read from its device path.</summary>
internal static class MonitorEdid
{
    public static byte[]? Read(string? devicePath)
    {
        var key = RegistryKey(devicePath);
        if (key is null)
            return null;

        try
        {
            using var opened = Registry.LocalMachine.OpenSubKey(key);
            return opened?.GetValue("EDID") as byte[];
        }
        catch (Exception exception) when (exception
            is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>\\?\DISPLAY#VEN#instance#{guid}</c> names
    /// <c>SYSTEM\CurrentControlSet\Enum\DISPLAY\VEN\instance\Device Parameters</c>.
    /// </summary>
    internal static string? RegistryKey(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
            return null;

        var path = devicePath.Trim();
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            path = path[4..];

        var guid = path.LastIndexOf("#{", StringComparison.Ordinal);
        if (guid >= 0)
            path = path[..guid];

        path = path.Replace('#', '\\');
        if (!path.StartsWith(@"DISPLAY\", StringComparison.OrdinalIgnoreCase))
            return null;

        return @"SYSTEM\CurrentControlSet\Enum\" + path + @"\Device Parameters";
    }
}
