namespace Winland.Application;

/// <summary>
/// Decides when the first layout may run, and when a later session change must
/// lay the Workspaces out again. The shell wait is bounded so a missing tray
/// cannot block startup.
/// </summary>
public sealed class SessionPhase
{
    // Long enough for the shell to create its tray after logon, and short enough
    // that a missing tray cannot hold the first layout.
    public static readonly TimeSpan ShellWait = TimeSpan.FromSeconds(15);

    private bool _shellReady;
    private bool _shellTimedOut;
    private bool _suspended;
    private bool _locked;
    private bool _consoleConnected = true;
    private bool _remoteConnected;
    private bool _displayAwake = true;
    private bool _laidOut;

    private bool ShellSettled => _shellReady || _shellTimedOut;

    private bool Interactive => !_suspended && !_locked && (_consoleConnected || _remoteConnected);

    public bool Locked => _locked;

    /// <summary>
    /// False while the session is locked, suspended, disconnected or has its display off. Windows can look
    /// closed then, so what the windows show is not taken as the newest state.
    /// </summary>
    public bool Live => Interactive && _displayAwake;

    public SessionEffect Notice(SessionSignal signal)
    {
        switch (signal)
        {
            case SessionSignal.ShellReady:
                return ShellArrived();
            case SessionSignal.ShellTimedOut:
                if (ShellSettled)
                    return SessionEffect.None;

                _shellTimedOut = true;
                return FirstLayout();
            case SessionSignal.Suspended:
                if (_suspended)
                    return SessionEffect.None;

                _suspended = true;
                return SessionEffect.None;
            case SessionSignal.Resumed:
                if (!_suspended)
                    return SessionEffect.None;

                _suspended = false;
                return BecomeActive();
            case SessionSignal.Locked:
                if (_locked)
                    return SessionEffect.None;

                _locked = true;
                return SessionEffect.None;
            case SessionSignal.Unlocked:
                if (!_locked)
                    return SessionEffect.None;

                _locked = false;
                return BecomeActive();
            case SessionSignal.ConsoleConnected:
                if (_consoleConnected)
                    return SessionEffect.None;

                _consoleConnected = true;
                return BecomeActive();
            case SessionSignal.ConsoleDisconnected:
                if (!_consoleConnected)
                    return SessionEffect.None;

                _consoleConnected = false;
                return SessionEffect.None;
            case SessionSignal.RemoteConnected:
                if (_remoteConnected)
                    return SessionEffect.None;

                _remoteConnected = true;
                return BecomeActive();
            case SessionSignal.RemoteDisconnected:
                if (!_remoteConnected)
                    return SessionEffect.None;

                _remoteConnected = false;
                return SessionEffect.None;
            case SessionSignal.DisplayChanged:
                if (!_laidOut || !Interactive || !_displayAwake)
                    return SessionEffect.None;

                return SessionEffect.Reflow;
            case SessionSignal.DisplayOff:
                if (!_displayAwake)
                    return SessionEffect.None;

                _displayAwake = false;
                return SessionEffect.None;
            case SessionSignal.DisplayOn:
                return DisplayWoke();
            default:
                throw new ArgumentOutOfRangeException(nameof(signal), signal, "No handler for this signal.");
        }
    }

    private SessionEffect ShellArrived()
    {
        if (_shellReady)
            return SessionEffect.None;

        var late = _shellTimedOut && _laidOut;
        _shellReady = true;
        _shellTimedOut = false;
        if (late)
            return Interactive ? SessionEffect.Reflow : SessionEffect.None;

        return FirstLayout();
    }

    private SessionEffect DisplayWoke()
    {
        if (_displayAwake)
            return SessionEffect.None;

        _displayAwake = true;
        if (!Interactive || !ShellSettled)
            return SessionEffect.None;

        if (!_laidOut)
            return FirstLayout();

        return SessionEffect.Recover;
    }

    private SessionEffect BecomeActive()
    {
        if (!Interactive || !ShellSettled)
            return SessionEffect.None;

        if (!_laidOut)
            return FirstLayout();

        return SessionEffect.Recover;
    }

    private SessionEffect FirstLayout()
    {
        if (!ShellSettled || !Interactive || _laidOut)
            return SessionEffect.None;

        _laidOut = true;
        return SessionEffect.LayOut;
    }
}

public enum SessionSignal
{
    ShellReady,
    ShellTimedOut,
    Suspended,
    Resumed,
    Locked,
    Unlocked,
    ConsoleConnected,
    ConsoleDisconnected,
    RemoteConnected,
    RemoteDisconnected,
    DisplayChanged,
    DisplayOff,
    DisplayOn,
}

public enum SessionEffect
{
    None,
    LayOut,
    Reflow,
    Recover,
}
