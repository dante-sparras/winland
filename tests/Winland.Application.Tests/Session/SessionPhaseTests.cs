
namespace Winland.Application.Tests;

public class SessionPhaseTests
{
    [Fact]
    public void TheFirstLayoutWaitsUntilTheShellIsReady()
    {
        var phase = new SessionPhase();

        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.DisplayChanged));
        Assert.Equal(SessionEffect.LayOut, phase.Notice(SessionSignal.ShellReady));
        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.ShellReady));
    }

    [Theory]
    [InlineData(SessionSignal.Locked, SessionSignal.Unlocked)]
    [InlineData(SessionSignal.Suspended, SessionSignal.Resumed)]
    [InlineData(SessionSignal.ConsoleDisconnected, SessionSignal.ConsoleConnected)]
    [InlineData(SessionSignal.DisplayOff, SessionSignal.DisplayOn)]
    public void TheSessionIsLiveExceptWhileItIsAway(SessionSignal away, SessionSignal back)
    {
        var phase = new SessionPhase();
        phase.Notice(SessionSignal.ShellReady);
        Assert.True(phase.Live);

        phase.Notice(away);
        Assert.False(phase.Live);

        phase.Notice(back);
        Assert.True(phase.Live);
    }

    [Fact]
    public void TheFirstLayoutRunsWhenTheShellWaitTimesOut()
    {
        var phase = new SessionPhase();

        Assert.Equal(SessionEffect.LayOut, phase.Notice(SessionSignal.ShellTimedOut));
        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.ShellTimedOut));
    }

    [Fact]
    public void AShellThatAppearsAfterTheTimeoutLaysOutAgain()
    {
        var phase = new SessionPhase();
        Assert.Equal(SessionEffect.LayOut, phase.Notice(SessionSignal.ShellTimedOut));

        Assert.Equal(SessionEffect.Reflow, phase.Notice(SessionSignal.ShellReady));
    }

    [Fact]
    public void SuspendHoldsTheFirstLayoutUntilResume()
    {
        var phase = new SessionPhase();

        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.Suspended));
        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.ShellReady));
        Assert.Equal(SessionEffect.LayOut, phase.Notice(SessionSignal.Resumed));
    }

    [Fact]
    public void ResumeAfterALayoutRecoversTheSession()
    {
        var phase = LaidOut();

        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.Suspended));
        Assert.Equal(SessionEffect.Recover, phase.Notice(SessionSignal.Resumed));
        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.Resumed));
    }

    [Fact]
    public void ResumeWhileLockedWaitsForUnlock()
    {
        var phase = LaidOut();
        phase.Notice(SessionSignal.Locked);
        phase.Notice(SessionSignal.Suspended);

        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.Resumed));
        Assert.Equal(SessionEffect.Recover, phase.Notice(SessionSignal.Unlocked));
    }

    [Fact]
    public void LockHidesChangesUntilUnlock()
    {
        var phase = LaidOut();

        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.Locked));
        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.DisplayChanged));
        Assert.Equal(SessionEffect.Recover, phase.Notice(SessionSignal.Unlocked));
    }

    [Fact]
    public void TheFirstLayoutWaitsForAnUnlock()
    {
        var phase = new SessionPhase();
        phase.Notice(SessionSignal.Locked);

        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.ShellReady));
        Assert.Equal(SessionEffect.LayOut, phase.Notice(SessionSignal.Unlocked));
    }

    [Fact]
    public void AShellTimeoutWhileLockedWaitsForUnlock()
    {
        var phase = new SessionPhase();
        phase.Notice(SessionSignal.Locked);

        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.ShellTimedOut));
        Assert.Equal(SessionEffect.LayOut, phase.Notice(SessionSignal.Unlocked));
    }

    [Fact]
    public void AnUnlockWithoutALockDoesNotLayOutAgain()
    {
        var phase = LaidOut();

        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.Unlocked));
    }

    [Fact]
    public void AConsoleReconnectRecoversTheSession()
    {
        var phase = LaidOut();

        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.ConsoleDisconnected));
        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.DisplayChanged));
        Assert.Equal(SessionEffect.Recover, phase.Notice(SessionSignal.ConsoleConnected));
        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.ConsoleConnected));
    }

    [Fact]
    public void ARemoteConnectRecoversTheSession()
    {
        var phase = LaidOut();

        Assert.Equal(SessionEffect.Recover, phase.Notice(SessionSignal.RemoteConnected));
        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.RemoteDisconnected));
        Assert.Equal(SessionEffect.Reflow, phase.Notice(SessionSignal.DisplayChanged));
    }

    [Fact]
    public void ARemoteDisconnectLeavesTheSessionUntilItReconnects()
    {
        var phase = LaidOut();
        Assert.Equal(SessionEffect.Recover, phase.Notice(SessionSignal.RemoteConnected));
        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.ConsoleDisconnected));

        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.RemoteDisconnected));
        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.DisplayChanged));
        Assert.Equal(SessionEffect.Recover, phase.Notice(SessionSignal.RemoteConnected));
    }

    [Fact]
    public void ADisplayChangeReflowsAfterTheFirstLayout()
    {
        var phase = new SessionPhase();
        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.DisplayChanged));
        phase.Notice(SessionSignal.ShellReady);

        Assert.Equal(SessionEffect.Reflow, phase.Notice(SessionSignal.DisplayChanged));
        Assert.Equal(SessionEffect.Reflow, phase.Notice(SessionSignal.DisplayChanged));
    }

    [Fact]
    public void DisplayPowerOnRecoversTheSession()
    {
        var phase = LaidOut();

        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.DisplayOff));
        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.DisplayChanged));
        Assert.Equal(SessionEffect.Recover, phase.Notice(SessionSignal.DisplayOn));
        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.DisplayOn));
    }

    [Fact]
    public void ResumeRecoversEvenIfTheDisplayWasOff()
    {
        var phase = LaidOut();
        phase.Notice(SessionSignal.DisplayOff);
        phase.Notice(SessionSignal.Suspended);

        Assert.Equal(SessionEffect.Recover, phase.Notice(SessionSignal.Resumed));
        Assert.Equal(SessionEffect.Recover, phase.Notice(SessionSignal.DisplayOn));
    }

    [Fact]
    public void ADisplayChangeWhileSuspendedWaitsForResume()
    {
        var phase = LaidOut();
        phase.Notice(SessionSignal.Suspended);

        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.DisplayChanged));
        Assert.Equal(SessionEffect.Recover, phase.Notice(SessionSignal.Resumed));
    }

    [Fact]
    public void ASecondSuspendDoesNotChangeThePhase()
    {
        var phase = LaidOut();

        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.Suspended));
        Assert.Equal(SessionEffect.None, phase.Notice(SessionSignal.Suspended));
        Assert.Equal(SessionEffect.Recover, phase.Notice(SessionSignal.Resumed));
    }

    [Fact]
    public void TheShellWaitIsBounded()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), SessionPhase.ShellWait);
    }

    private static SessionPhase LaidOut()
    {
        var phase = new SessionPhase();
        Assert.Equal(SessionEffect.LayOut, phase.Notice(SessionSignal.ShellReady));
        return phase;
    }
}
