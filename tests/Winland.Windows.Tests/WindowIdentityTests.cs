using Winland.Windows;

namespace Winland.Tests;

public class WindowIdentityTests
{
    [Fact]
    public void AClosedClientIsLeftOutAndTheOthersAreStillRecorded()
    {
        using var probe = new LimitProbeTests.LimitProbe();
        probe.Open();
        try
        {
            var open = new WindowId(probe.Anchor.ToInt64());
            var closed = new WindowId(0x7007);
            var snapshot = new RecoverySnapshot(
            [
                new RecoveredClient(closed, new WorkspaceId("1"), MonitorId.Primary, true, "", "", 0),
                new RecoveredClient(open, new WorkspaceId("2"), MonitorId.Primary, true, "", "", 0),
            ]);

            var annotated = WindowIdentity.Annotate(snapshot);

            var client = Assert.Single(annotated.Clients);
            Assert.Equal(open, client.Window);
            Assert.Equal(Environment.ProcessId, client.ProcessId);
            Assert.Equal("WinlandLimitProbe", client.Class);
        }
        finally
        {
            probe.Close();
        }
    }
}
