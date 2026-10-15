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
    public void DisplayPowerUsesTheStandIn()
    {
        var fake = new FakePower();

        Assert.True(DisplayPowerAdapter.TryApply(new DisplayPowerRequest(@"\\.\DISPLAY1", false), fake));
        Assert.False(DisplayPowerAdapter.TryApply(new DisplayPowerRequest("", true), new FakePower { Accepts = false }));

        Assert.Equal([false], fake.States);
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
