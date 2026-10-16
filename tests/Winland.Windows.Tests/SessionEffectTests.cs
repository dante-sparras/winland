using Winland;

namespace Winland.Windows.Tests;

public class SessionEffectTests
{
    [Fact]
    public void ProcessTerminationUsesTheStandIn()
    {
        var fake = new FakeProcesses();

        Assert.False(ProcessTerminator.TryTerminate(0, fake));
        Assert.True(ProcessTerminator.TryTerminate(4242, fake));

        Assert.Equal([4242L], fake.Pids);
    }

    [Fact]
    public void AKillEndsTheProcessThatOwnsTheWindowNow()
    {
        static string Exe(long pid) => pid switch
        {
            10 => "ApplicationFrameHost.exe",
            20 => "Calculator.exe",
            _ => "app.exe",
        };

        Assert.Equal(20L, ProcessTerminator.Target(stored: 10, live: 20, Exe));
        Assert.Equal(30L, ProcessTerminator.Target(stored: 30, live: null, Exe));
        Assert.Null(ProcessTerminator.Target(stored: 30, live: 0, Exe));
        Assert.Null(ProcessTerminator.Target(stored: 10, live: null, Exe));
        Assert.Null(ProcessTerminator.Target(stored: 30, live: 10, Exe));
    }

    [Fact]
    public void DisplayPowerUsesTheStandIn()
    {
        var fake = new FakePower();

        Assert.True(DisplayPowerAdapter.TryApply(new DisplayPowerRequest(@"\\.\DISPLAY1", false), fake));
        Assert.False(DisplayPowerAdapter.TryApply(new DisplayPowerRequest("", true), new FakePower { Accepts = false }));

        Assert.Equal([false], fake.States);
    }

    [Fact]
    public void DisplayPowerOffPostsTheCommandAndOnAlsoNudgesInput()
    {
        var posted = new List<nint>();
        var nudges = 0;
        var power = new MonitorPowerSignals(state =>
        {
            posted.Add(state);
            return true;
        }, () => nudges++);

        Assert.True(power.TrySet(false));
        Assert.Equal([MonitorPowerSignals.Off], posted);
        Assert.Equal(0, nudges);

        Assert.True(power.TrySet(true));
        Assert.Equal([MonitorPowerSignals.Off, MonitorPowerSignals.On], posted);
        Assert.Equal(1, nudges);
    }

    [Fact]
    public void HaltInvokesTheInstalledStop()
    {
        var stopped = 0;
        SessionHalt.Stop = () => stopped++;
        try
        {
            SessionHalt.Request();
            Assert.Equal(1, stopped);
        }
        finally
        {
            SessionHalt.Stop = null;
        }
    }

    private sealed class FakeProcesses : IProcessTerminator
    {
        public List<long> Pids { get; } = [];

        public bool TryTerminate(long processId)
        {
            Pids.Add(processId);
            return true;
        }
    }

    private sealed class FakePower : IMonitorPower
    {
        public bool Accepts { get; init; } = true;

        public List<bool> States { get; } = [];

        public bool TrySet(bool on)
        {
            if (!Accepts)
                return false;

            States.Add(on);
            return true;
        }
    }
}
