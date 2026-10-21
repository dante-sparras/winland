using System.Runtime.InteropServices;
using Microsoft.Win32;
using Winland.Install;

namespace Winland.Daemon;

sealed class WindowsInstallRegistry : IRegistry
{
    public string? GetString(RegistryRoot root, string key, string name)
    {
        using var hive = Open(root);
        using var sub = hive.OpenSubKey(key);
        return sub?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void SetString(RegistryRoot root, string key, string name, string value, bool expandable)
    {
        using var hive = Open(root);
        using var sub = hive.CreateSubKey(key);
        sub.SetValue(name, value, expandable ? RegistryValueKind.ExpandString : RegistryValueKind.String);
        if (name.Equals(InstallLocations.PathValue, StringComparison.OrdinalIgnoreCase))
            BroadcastEnvironment();
    }

    public void SetDword(RegistryRoot root, string key, string name, int value)
    {
        using var hive = Open(root);
        using var sub = hive.CreateSubKey(key);
        sub.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void DeleteKey(RegistryRoot root, string key)
    {
        using var hive = Open(root);
        hive.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
    }

    public void DeleteValue(RegistryRoot root, string key, string name)
    {
        using var hive = Open(root);
        using var sub = hive.OpenSubKey(key, writable: true);
        sub?.DeleteValue(name, throwOnMissingValue: false);
    }

    private static RegistryKey Open(RegistryRoot root)
    {
        var hive = root == RegistryRoot.Machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        return RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
    }

    // A new terminal reads PATH when it starts. Explorer learns the value from this broadcast.
    private static void BroadcastEnvironment()
    {
        _ = SendMessageTimeout(new IntPtr(0xffff), 0x001A, UIntPtr.Zero, "Environment", 0x0002, 1000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window,
        uint message,
        UIntPtr wParam,
        string lParam,
        uint flags,
        uint timeout,
        out UIntPtr result);
}
