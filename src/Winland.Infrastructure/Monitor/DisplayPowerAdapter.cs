using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winland.Infrastructure;

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
    /// Posts a monitor-power command to Winland's own session window. Tests do not call this.
    /// </summary>
    public static bool TryApplyToSystem(DisplayPowerRequest request) =>
        TryApply(request, SystemMonitorPower.Shared);

    private static class SystemMonitorPower
    {
        private const uint SystemCommand = 0x0112;
        private const nuint MonitorPower = 0xF170;
        private static readonly HWND Broadcast = new(0xFFFF);

        internal static readonly MonitorPowerSignals Shared = new(Post, Nudge);

        // DefWindowProc carries out SC_MONITORPOWER for any top-level window, so it goes to Winland's own
        // window. Posting returns at once: a broadcast SendMessage waited on every window, hung ones too.
        private static bool Post(nint state)
        {
            if (SessionWindow.TryPost(SystemCommand, MonitorPower, state))
                return true;

            return PInvoke.PostMessage(Broadcast, SystemCommand, new WPARAM(MonitorPower), new LPARAM(state));
        }

        // A mouse move of zero is input, and input turns the displays back on.
        private static void Nudge()
        {
            var move = new INPUT { type = INPUT_TYPE.INPUT_MOUSE };
            move.Anonymous.mi.dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE;
            ReadOnlySpan<INPUT> inputs = [move];
            if (PInvoke.SendInput(inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
                Console.Error.WriteLine($"The display wake was not sent ({Marshal.GetLastPInvokeError()}).");
        }
    }
}

/// <summary>
/// SC_MONITORPOWER with 2 turns the displays off. Its "on" value, -1, is not reliably honored on Windows 8 and
/// later, so turning them on also sends a zero mouse move, which wakes them.
/// </summary>
internal sealed class MonitorPowerSignals : IMonitorPower
{
    internal const nint Off = 2;
    internal const nint On = -1;

    private readonly Func<nint, bool> _post;
    private readonly Action _nudge;

    internal MonitorPowerSignals(Func<nint, bool> post, Action nudge)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(nudge);
        _post = post;
        _nudge = nudge;
    }

    public bool TrySet(bool on)
    {
        var posted = _post(on ? On : Off);
        if (!on)
            return posted;

        _nudge();
        return true;
    }
}
