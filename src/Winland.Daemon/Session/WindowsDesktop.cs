using System.Runtime.InteropServices;
using Microsoft.Win32;
using Winland.Install;

namespace Winland.Daemon;

/// <summary>
/// Snap windows and the precision touchpad swipes. The running Daemon does not use this.
/// <c>winland tweaks</c> and <c>winland uninstall</c> do.
/// Snap is <c>SPI_GETWINARRANGING</c> and <c>SPI_SETWINARRANGING</c> (0x0082 and 0x0083).
/// Setting it passes the BOOL as <c>pvParam</c> with <c>SPIF_UPDATEINIFILE | SPIF_SENDCHANGE</c>.
/// The swipe values are <c>ThreeFingerSlideEnabled</c> and <c>FourFingerSlideEnabled</c> under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad</c>. 0 means Nothing.
/// </summary>
sealed class WindowsDesktop : IWindowsDesktop
{
    private const uint GetWindowArranging = 0x0082;

    private const uint SetWindowArranging = 0x0083;

    private const uint UpdateAndNotify = 0x0001 | 0x0002;

    private const string TouchpadKey = @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";

    private const string ThreeFingerValue = "ThreeFingerSlideEnabled";

    private const string FourFingerValue = "FourFingerSlideEnabled";

    public bool WindowArranging
    {
        get
        {
            var value = 0;
            if (!GetParameter(GetWindowArranging, 0, ref value, 0))
                throw new InvalidOperationException("Could not read Snap windows.");

            return value != 0;
        }
        set
        {
            if (!SetParameter(SetWindowArranging, 0, value ? 1 : 0, UpdateAndNotify))
                throw new InvalidOperationException("Could not change Snap windows.");
        }
    }

    public int? ThreeFingerSlide
    {
        get => ReadDword(ThreeFingerValue);
        set => WriteDword(ThreeFingerValue, value);
    }

    public int? FourFingerSlide
    {
        get => ReadDword(FourFingerValue);
        set => WriteDword(FourFingerValue, value);
    }

    private static int? ReadDword(string name)
    {
        using var hive = User();
        using var key = hive.OpenSubKey(TouchpadKey);
        if (key is null)
            return null;

        var raw = key.GetValue(name);
        if (raw is null)
            return null;

        if (raw is int number)
            return number;

        throw new InvalidOperationException("The touchpad setting " + name + " is not a number. It was left unchanged.");
    }

    private static void WriteDword(string name, int? value)
    {
        using var hive = User();
        if (value is int number)
        {
            using var key = hive.CreateSubKey(TouchpadKey);
            key.SetValue(name, number, RegistryValueKind.DWord);
            return;
        }

        using var existing = hive.OpenSubKey(TouchpadKey, writable: true);
        existing?.DeleteValue(name, throwOnMissingValue: false);
    }

    private static RegistryKey User() =>
        RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool GetParameter(uint action, uint param, ref int value, uint winIni);

    // SPI_SETWINARRANGING takes the BOOL in pvParam, not a pointer to one.
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool SetParameter(uint action, uint param, nint value, uint winIni);
}
