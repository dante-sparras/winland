using Winland.Core;

namespace Winland.Windows.Tests;

public class SessionNoticeTests
{
    // WM_QUERYENDSESSION only asks, and the shutdown can still be cancelled. WM_ENDSESSION with wParam TRUE is the end.
    [Fact]
    public void OnlyAnEndSessionThatIsTrueEndsTheSession()
    {
        Assert.True(SessionNotices.EndsSession(SessionNotices.EndSessionMessage, 1));
        Assert.False(SessionNotices.EndsSession(SessionNotices.EndSessionMessage, 0));
        Assert.False(SessionNotices.EndsSession(SessionNotices.QueryEndSessionMessage, 1));
    }

    [Fact]
    public void PowerBroadcastMapsSuspendAndAutomaticResume()
    {
        Assert.Equal(SessionSignal.Suspended, Power(SessionNotices.Suspend));
        Assert.Equal(SessionSignal.Resumed, Power(SessionNotices.ResumeAutomatic));
        Assert.False(SessionNotices.TryPower(0x0007, out _));
        Assert.False(SessionNotices.TryFromMessage(SessionNotices.PowerMessage, SessionNotices.PowerSettingChange, out _));
    }

    [Fact]
    public void SessionChangeMapsLockUnlockConsoleAndRemote()
    {
        Assert.Equal(SessionSignal.ConsoleConnected, Session(1));
        Assert.Equal(SessionSignal.ConsoleDisconnected, Session(2));
        Assert.Equal(SessionSignal.RemoteConnected, Session(3));
        Assert.Equal(SessionSignal.RemoteDisconnected, Session(4));
        Assert.Equal(SessionSignal.Locked, Session(7));
        Assert.Equal(SessionSignal.Unlocked, Session(8));
        Assert.False(SessionNotices.TrySession(5, out _));
        Assert.False(SessionNotices.TrySession(6, out _));
    }

    [Fact]
    public void DisplayPowerMapsTheConsoleAndTheMonitor()
    {
        Assert.Equal(SessionSignal.DisplayOff, Display(SessionNotices.ConsoleDisplayState, 0));
        Assert.Equal(SessionSignal.DisplayOn, Display(SessionNotices.ConsoleDisplayState, 1));
        Assert.Equal(SessionSignal.DisplayOff, Display(SessionNotices.ConsoleDisplayState, 2));
        Assert.Equal(SessionSignal.DisplayOff, Display(SessionNotices.MonitorPowerOn, 0));
        Assert.Equal(SessionSignal.DisplayOn, Display(SessionNotices.MonitorPowerOn, 1));
        Assert.False(SessionNotices.TryDisplayPower(SessionNotices.ConsoleDisplayState, 3, out _));
        Assert.False(SessionNotices.TryDisplayPower(SessionNotices.MonitorPowerOn, 2, out _));
        Assert.False(SessionNotices.TryDisplayPower(Guid.Empty, 1, out _));
    }

    [Fact]
    public void ADisplayChangeMessageAsksForAReflow()
    {
        Assert.True(SessionNotices.TryFromMessage(SessionNotices.DisplayMessage, 0, out var signal));
        Assert.Equal(SessionSignal.DisplayChanged, signal);
    }

    private static SessionSignal Power(nuint code)
    {
        Assert.True(SessionNotices.TryPower(code, out var signal));
        return signal;
    }

    private static SessionSignal Session(nuint reason)
    {
        Assert.True(SessionNotices.TrySession(reason, out var signal));
        return signal;
    }

    private static SessionSignal Display(Guid setting, int value)
    {
        Assert.True(SessionNotices.TryDisplayPower(setting, value, out var signal));
        return signal;
    }
}
