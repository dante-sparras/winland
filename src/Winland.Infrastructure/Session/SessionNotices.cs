namespace Winland.Infrastructure;

internal static class SessionNotices
{
    // GUID_CONSOLE_DISPLAY_STATE. 0 is off, 1 is on, 2 is dimmed.
    internal static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    // GUID_MONITOR_POWER_ON. 0 is off, 1 is on.
    internal static readonly Guid MonitorPowerOn = new("02731015-4510-4526-99e6-e5a17ebd1aea");

    internal const uint PowerMessage = 0x0218;
    internal const uint SessionMessage = 0x02B1;
    internal const uint DisplayMessage = 0x007E;

    internal const nuint Suspend = 0x0004;
    internal const nuint ResumeAutomatic = 0x0012;
    internal const nuint PowerSettingChange = 0x8013;

    internal const uint QueryEndSessionMessage = 0x0011;
    internal const uint EndSessionMessage = 0x0016;

    /// <summary>
    /// True only for WM_ENDSESSION with wParam TRUE, when the session really ends. WM_QUERYENDSESSION only asks,
    /// and a shutdown that another program cancels leaves the session running.
    /// </summary>
    internal static bool EndsSession(uint message, nuint wParam) => message == EndSessionMessage && wParam != 0;

    internal static bool TryFromMessage(uint message, nuint wParam, out SessionSignal signal)
    {
        if (message == DisplayMessage)
        {
            signal = SessionSignal.DisplayChanged;
            return true;
        }

        if (message == PowerMessage)
            return TryPower(wParam, out signal);

        if (message == SessionMessage)
            return TrySession(wParam, out signal);

        signal = default;
        return false;
    }

    internal static bool TryPower(nuint code, out SessionSignal signal)
    {
        if (code == Suspend)
        {
            signal = SessionSignal.Suspended;
            return true;
        }

        if (code == ResumeAutomatic)
        {
            signal = SessionSignal.Resumed;
            return true;
        }

        signal = default;
        return false;
    }

    internal static bool TrySession(nuint reason, out SessionSignal signal)
    {
        switch (reason)
        {
            case 1:
                signal = SessionSignal.ConsoleConnected;
                return true;
            case 2:
                signal = SessionSignal.ConsoleDisconnected;
                return true;
            case 3:
                signal = SessionSignal.RemoteConnected;
                return true;
            case 4:
                signal = SessionSignal.RemoteDisconnected;
                return true;
            case 7:
                signal = SessionSignal.Locked;
                return true;
            case 8:
                signal = SessionSignal.Unlocked;
                return true;
            default:
                signal = default;
                return false;
        }
    }

    internal static bool TryDisplayPower(Guid setting, int value, out SessionSignal signal)
    {
        if (setting == ConsoleDisplayState)
        {
            if (value is not (0 or 1 or 2))
            {
                signal = default;
                return false;
            }

            // A dimmed screen is still on, so undimming it is not a wake that needs the windows put back.
            signal = value == 0 ? SessionSignal.DisplayOff : SessionSignal.DisplayOn;
            return true;
        }

        if (setting == MonitorPowerOn && value is 0 or 1)
        {
            signal = value == 1 ? SessionSignal.DisplayOn : SessionSignal.DisplayOff;
            return true;
        }

        signal = default;
        return false;
    }
}
