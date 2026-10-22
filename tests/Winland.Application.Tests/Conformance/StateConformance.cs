namespace Winland.Application.Tests.Conformance;

/// <summary>Winland's state case. The test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class StateConformance
{
    private const string MonitorNotHonored = "error: Monitor settings other than a reserved area are not honored.";

    [Fact]
    public void State()
    {
        var desk = new Desk();

        desk.Ok("focusmonitor", "HEADLESS-2");
        desk.Ok("workspace", "9");
        desk.Spawn("fallback-workspace-a");

        // A monitor keyword cannot disable a display. Windows reports that.
        foreach (var monitor in new[] { "HEADLESS-1", "HEADLESS-2", "HEADLESS-3", "HEADLESS-4" })
            Assert.Equal(MonitorNotHonored, desk.Send($"keyword monitor {monitor}, disable"));

        Assert.DoesNotContain("FALLBACK", desk.Send("j/monitors"), StringComparison.Ordinal);
        Assert.Equal("HEADLESS-2", MonitorOf(desk, 9));

        desk.ReportEnabled(false);
        Assert.Contains("FALLBACK", desk.Send("j/monitors"), StringComparison.Ordinal);
        // The occupied Workspace becomes a Guest. FALLBACK had none of its own, so the Guest is shown as Workspace 1.
        Assert.Equal("FALLBACK", desk.ActiveWorkspace().GetProperty("monitor").GetString());
        Assert.Equal(1, desk.ActiveWorkspace().GetProperty("id").GetInt32());

        // The reference also asks its test plugin about workspace lifecycle state here, which has no counterpart.
        desk.Ok("workspace", "9");
        desk.Spawn("fallback-workspace-b");

        Assert.Equal("ok", desk.Send("reload"));
        Assert.Equal("FALLBACK", MonitorOf(desk, 9));

        desk.ReportEnabled(true);
        Assert.Equal("HEADLESS-2", MonitorOf(desk, 9));
    }

    private static string? MonitorOf(Desk desk, int workspace) =>
        desk.Workspaces().Single(item => item.GetProperty("id").GetInt32() == workspace).GetProperty("monitor").GetString();
}
