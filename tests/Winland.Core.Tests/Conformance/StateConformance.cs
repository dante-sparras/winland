namespace Winland.Tests.Conformance;

/// <summary>Winland's state case. The test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class StateConformance
{
    [Fact]
    public void State()
    {
        var desk = new Desk();

        desk.Ok("focusmonitor", "HEADLESS-2");
        desk.Ok("workspace", "9");
        desk.Spawn("fallback-workspace-a");

        foreach (var monitor in new[] { "HEADLESS-1", "HEADLESS-2", "HEADLESS-3", "HEADLESS-4" })
            desk.Keyword("monitor", $"{monitor}, disable");

        Assert.Contains("FALLBACK", desk.Send("j/monitors"), StringComparison.Ordinal);
        Assert.Equal("FALLBACK", MonitorOf(desk, 9));

        // The reference also asks its test plugin about workspace lifecycle state here, which has no counterpart.
        desk.Ok("workspace", "9");
        desk.Spawn("fallback-workspace-b");

        Assert.Equal("ok", desk.Send("reload"));
        Assert.Equal("HEADLESS-2", MonitorOf(desk, 9));
    }

    private static string? MonitorOf(Desk desk, int workspace) =>
        desk.Workspaces().Single(item => item.GetProperty("id").GetInt32() == workspace).GetProperty("monitor").GetString();
}
