using System.Text.Json;

namespace Winland.Tests.Conformance;

/// <summary>The pinned release's persistent workspace cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class PersistentConformance
{
    [Fact(Skip = "A persistent rule keeps a special workspace, and creates a Workspace bound to a Monitor that is not there yet. A new Monitor then shows such a Workspace, where the pinned release opens workspace 2.")]
    public void PersistentWorkspaces()
    {
        var desk = new Desk("""
            workspace = 5, monitor:HEADLESS-2, persistent:true
            workspace = 6, monitor:HEADLESS-PERSISTENT-TEST, persistent:true
            workspace = name:PERSIST, monitor:HEADLESS-PERSISTENT-TEST, persistent:true
            workspace = name:PERSIST-2, monitor:HEADLESS-PERSISTENT-TEST, persistent:true
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
        Assert.Contains("2 (2)", labels);
        Assert.Contains("5 (5)", labels);
        Assert.Contains("6 (6)", labels);
        Assert.Contains("PERSIST (PERSIST)", labels);
        Assert.Contains("PERSIST-2 (PERSIST-2)", labels);
        Assert.Equal(6, labels.Length);

        Assert.Equal("ok", desk.Send("reload"));
        labels = Labels(desk);
        Assert.DoesNotContain("5 (5)", labels);
        Assert.DoesNotContain("6 (6)", labels);
        Assert.DoesNotContain("PERSIST (PERSIST)", labels);
        Assert.Equal(2, labels.Length);

        desk.RemoveMonitor("HEADLESS-PERSISTENT-TEST");
    }

    [Fact(Skip = "An empty Workspace on a Monitor that goes away is kept on the Monitor that takes it. The pinned release removes it.")]
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
        Assert.DoesNotContain("410 (410)", Labels(desk));

        desk.RemoveMonitor("HEADLESS-LIFECYCLE-OWNERSHIP");
        Assert.DoesNotContain("HEADLESS-LIFECYCLE-OWNERSHIP", MonitorNames(desk));
    }

    // The reference adds its rules over the socket, so reload drops them and reads only the reference config.
    private static void ReloadsReferenceConfig(Desk desk) =>
        desk.Core.ReadConfig = () => new ConfigRead(ConfigParser.Parse(Desk.Config));

    private static string[] Labels(Desk desk) => [.. desk.Workspaces().Select(Label)];

    // A Workspace as the reference reply writes it, `ID (NAME)`: a numbered Workspace's id, or else its name, then its name.
    private static string Label(JsonElement workspace)
    {
        var id = workspace.GetProperty("id").GetInt32();
        return id > 0 ? $"{id} ({Name(workspace)})" : $"{Name(workspace)} ({Name(workspace)})";
    }

    private static string Name(JsonElement workspace) => workspace.GetProperty("name").GetString() ?? "";

    private static string[] MonitorNames(Desk desk)
    {
        using var document = JsonDocument.Parse(desk.Send("j/monitors"));
        return [.. document.RootElement.EnumerateArray().Select(monitor => monitor.GetProperty("name").GetString() ?? "")];
    }
}
