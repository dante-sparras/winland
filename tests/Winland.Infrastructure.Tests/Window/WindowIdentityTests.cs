namespace Winland.Infrastructure.Tests;

// One test opens real windows, so the class runs alone with the other desktop tests.
[Collection(DesktopCollection.Name)]
[Trait("Category", DesktopCollection.Name)]
public class WindowIdentityTests
{
    [Fact]
    public void ProcessAndClassMakeTheSameWindowAndTheTitleTheSameClient()
    {
        var client = new RecoveredClient(new WindowId(5), WorkspaceId.Initial, MonitorId.Primary, false, "Notepad", "a.txt", 42);

        Assert.Equal(WindowIdentity.Recognition.SameClient, WindowIdentity.Recognize(client, 42, "Notepad", "a.txt"));
        Assert.Equal(WindowIdentity.Recognition.SameWindow, WindowIdentity.Recognize(client, 42, "Notepad", "b.txt"));
        Assert.Equal(WindowIdentity.Recognition.Other, WindowIdentity.Recognize(client, 43, "Notepad", "a.txt"));
        Assert.Equal(WindowIdentity.Recognition.Other, WindowIdentity.Recognize(client, 42, "Edit", "a.txt"));
    }

    // A hidden Client left behind by a crash must come back even if its title changed while it was hidden.
    [Fact]
    public void AHiddenClientIsTheSameClientWhateverItsTitle()
    {
        var client = new RecoveredClient(new WindowId(5), WorkspaceId.Initial, MonitorId.Primary, true, "Chrome_WidgetWin_1", "Inbox (3)", 42);

        Assert.Equal(WindowIdentity.Recognition.SameClient, WindowIdentity.Recognize(client, 42, "Chrome_WidgetWin_1", "Inbox (4)"));
        Assert.Equal(WindowIdentity.Recognition.Other, WindowIdentity.Recognize(client, 43, "Chrome_WidgetWin_1", "Inbox (3)"));
    }

    [DesktopFact]
    public void AClosedClientIsLeftOutAndTheOthersAreStillRecorded()
    {
        using var probe = new LimitProbe();
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
