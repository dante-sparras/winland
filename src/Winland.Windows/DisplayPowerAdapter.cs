using System.Runtime.InteropServices;

namespace Winland.Windows;

/// <summary>
/// Turns display power on or off. The session calls <see cref="DisplayPowerAdapter.TryApplyToSystem"/>;
/// tests pass a stand-in and do not call that method.
/// </summary>
public interface IMonitorPower
{
    bool TrySet(bool on);
}

public static class DisplayPowerAdapter
{
    public static bool TryApply(DisplayPowerRequest request, IMonitorPower power)
    {
        ArgumentNullException.ThrowIfNull(power);
        return power.TrySet(request.On);
    }

    /// <summary>
    /// Broadcasts a monitor-power command. Tests do not call this.
    /// </summary>
    public static bool TryApplyToSystem(DisplayPowerRequest request) =>
        TryApply(request, SystemMonitorPower.Shared);

    private sealed class SystemMonitorPower : IMonitorPower
    {
        internal static readonly SystemMonitorPower Shared = new();

        public bool TrySet(bool on)
        {
            const int Broadcast = 0xFFFF;
            const uint SystemCommand = 0x0112;
            const nint MonitorPower = 0xF170;
            SendMessage(Broadcast, SystemCommand, MonitorPower, on ? -1 : 2);
            return true;
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    }
}
