using System.Text.Json;

namespace Winland.Application.Tests.Conformance;

/// <summary>Winland's persistent workspace cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class PersistentConformance
{
    [Fact]
    public void PersistentWorkspaces()
    {
        var desk = new Desk("""
            workspace = 5, monitor:HEADLESS-2, persistent:true
            workspace = 6, monitor:HEADLESS-PERSISTENT-TEST, persistent:true
            workspace = 7, monitor:HEADLESS-PERSISTENT-TEST, persistent:true
            workspace = 8, monitor:HEADLESS-PERSISTENT-TEST, persistent:true
            workspace = special:PERSIST-SPECIAL, persistent:true
            """);
        ReloadsReferenceConfig(desk);
        desk.Dispatch("workspace", "1");

        Assert.DoesNotContain("special:PERSIST-SPECIAL (special:PERSIST-SPECIAL)", Labels(desk));
        desk.Ok("togglespecialworkspace", "PERSIST-SPECIAL");
        Assert.False(desk.Workspaces().Single(workspace => Name(workspace) == "special:PERSIST-SPECIAL").GetProperty("ispersistent").GetBoolean());
        desk.Ok("togglespecialworkspace", "PERSIST-SPECIAL");
        Assert.DoesNotContain("special:PERSIST-SPECIAL (special:PERSIST-SPECIAL)", Labels(desk));

        Assert.Contains("5 (5)", Labels(desk));
        Assert.Equal(2, Labels(desk).Length);

        // The reference's test plugin also checks that the new monitor's Workspace is shown at once, with no animation.
        desk.AddMonitor("HEADLESS-PERSISTENT-TEST", Desk.Second);
        Assert.Contains("HEADLESS-PERSISTENT-TEST", MonitorNames(desk));

        desk.Ok("focusmonitor", "HEADLESS-PERSISTENT-TEST");
        var labels = Labels(desk);
        // The new Monitor shows its own Workspace 1, beside HEADLESS-2's Workspace 1.
        Assert.Equal(2, labels.Count(label => label == "1 (1)"));
        Assert.Contains("5 (5)", labels);
        Assert.Contains("6 (6)", labels);
        Assert.Contains("7 (7)", labels);
        Assert.Contains("8 (8)", labels);
        Assert.Equal(6, labels.Length);

        Assert.Equal("ok", desk.Send("reload"));
        labels = Labels(desk);
        Assert.DoesNotContain("5 (5)", labels);
        Assert.DoesNotContain("6 (6)", labels);
        Assert.DoesNotContain("7 (7)", labels);
        Assert.DoesNotContain("8 (8)", labels);
        Assert.Equal(2, labels.Length);

        desk.RemoveMonitor("HEADLESS-PERSISTENT-TEST");
    }

    [Fact]
    public void WorkspaceLifecycleReleasesEmptyWorkspaces()
    {
        var desk = new Desk();

        desk.AddMonitor("HEADLESS-LIFECYCLE-OWNERSHIP", Desk.Second);
        Assert.Contains("HEADLESS-LIFECYCLE-OWNERSHIP", MonitorNames(desk));
        desk.Ok("focusmonitor", "HEADLESS-LIFECYCLE-OWNERSHIP");
        desk.Ok("workspace", "410");
        Assert.Equal("410 (410)", Label(desk.ActiveWorkspace()));

        desk.RemoveMonitor("HEADLESS-LIFECYCLE-OWNERSHIP");
        Assert.DoesNotContain("HEADLESS-LIFECYCLE-OWNERSHIP", MonitorNames(desk));
        Assert.DoesNotContain("410 (410)", Labels(desk));

        desk.AddMonitor("HEADLESS-LIFECYCLE-OWNERSHIP", Desk.Second);
        Assert.Contains("HEADLESS-LIFECYCLE-OWNERSHIP", MonitorNames(desk));
        // The empty Workspace it was showing comes back under that number.
        Assert.Contains("410 (410)", Labels(desk));
        desk.Ok("focusmonitor", "HEADLESS-LIFECYCLE-OWNERSHIP");
        Assert.Equal("410 (410)", Label(desk.ActiveWorkspace()));

        desk.RemoveMonitor("HEADLESS-LIFECYCLE-OWNERSHIP");
        Assert.DoesNotContain("HEADLESS-LIFECYCLE-OWNERSHIP", MonitorNames(desk));
    }

    // The reference adds its rules over the socket, so reload drops them and reads only the reference config.
    private static void ReloadsReferenceConfig(Desk desk) =>
        desk.WindowManager.ReadConfig = () => new ConfigRead(ConfigParser.Parse(Desk.Config));

    private static string[] Labels(Desk desk) => [.. desk.Workspaces().Select(Label)];

    // A Workspace as the reference reply writes it, `ID (NAME)`: a numbered Workspace's id, or else its name, then its name.
    private static string Label(JsonElement workspace)
    {
        var id = workspace.TryGetProperty("id", out var number) ? number.GetInt32() : 0;
        return id > 0 ? $"{id} ({Name(workspace)})" : $"{Name(workspace)} ({Name(workspace)})";
    }

    private static string Name(JsonElement workspace) => workspace.GetProperty("name").GetString() ?? "";

    private static string[] MonitorNames(Desk desk)
    {
        using var document = JsonDocument.Parse(desk.Send("j/monitors"));
        return [.. document.RootElement.EnumerateArray().Select(monitor => monitor.GetProperty("name").GetString() ?? "")];
    }
}
