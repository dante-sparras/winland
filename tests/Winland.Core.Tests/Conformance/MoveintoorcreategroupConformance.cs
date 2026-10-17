using System.Text.Json;

namespace Winland.Tests.Conformance;

/// <summary>The pinned release's moveintoorcreategroup cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class MoveintoorcreategroupConformance
{
    [Fact]
    public void MoveIntoOrCreateGroup()
    {
        var desk = new Desk("group:auto_group = false\n");
        desk.Dispatch("workspace", "name:moveintoorcreategroup");
        desk.Spawn("kitty_A");
        desk.Spawn("kitty_B");
        Assert.Equal(2, desk.Clients().Length);
        Assert.Contains(desk.Clients(), client => client.GetProperty("grouped").GetArrayLength() == 0);

        desk.Ok("focuswindow", "class:kitty_A");
        desk.Ok("moveintoorcreategroup", "r");
        Assert.All(desk.Clients(), client => Assert.True(client.TryGetProperty("grouped", out _)));
        Assert.Equal("kitty_A", desk.ActiveClass());

        KillAll(desk);
        Assert.Empty(desk.Clients());

        desk.Spawn("kitty_C");
        desk.Spawn("kitty_D");
        desk.Spawn("kitty_E");
        Assert.Equal(3, desk.Clients().Length);

        desk.Ok("focuswindow", "class:kitty_D");
        desk.Ok("togglegroup");
        desk.Ok("focuswindow", "class:kitty_E");
        desk.Ok("moveintoorcreategroup", "l");
        Assert.All(desk.Clients(), client => Assert.True(client.TryGetProperty("grouped", out _)));
        Assert.Equal("kitty_E", desk.ActiveClass());
    }

    [Fact]
    public void CrossMonitorGroupJoin()
    {
        var desk = new Desk("""
            group:auto_group = false
            workspace = name:groupcross-dst, monitor:HYPRTEST-CROSSGROUP
            """);
        desk.AddMonitor("HYPRTEST-CROSSGROUP", Desk.Second);

        desk.Ok("workspace", "name:groupcross-src");
        desk.Spawn("groupcross_srcA");
        desk.Spawn("groupcross_srcB");
        desk.Ok("focuswindow", "class:groupcross_srcB");
        desk.Ok("moveintoorcreategroup", "l");
        var source = MonitorOf(desk.ActiveWindow());

        desk.Ok("workspace", "name:groupcross-dst");
        desk.Spawn("groupcross_dstA");
        desk.Spawn("groupcross_dstB");
        desk.Ok("focuswindow", "class:groupcross_dstB");
        desk.Ok("moveintoorcreategroup", "r");
        var destination = MonitorOf(desk.ActiveWindow());

        Assert.NotEqual(source, destination);

        desk.Ok("focuswindow", "class:groupcross_srcA");
        desk.Ok("moveintoorcreategroup", "r");

        var active = desk.ActiveWindow();
        Assert.Equal("groupcross_srcA", Desk.ClassOf(active));
        Assert.Equal(destination, MonitorOf(active));

        KillAll(desk);
        desk.RemoveMonitor("HYPRTEST-CROSSGROUP");
    }

    private static string MonitorOf(JsonElement client) => client.GetProperty("monitor").ToString();

    // Closes every window, as the reference harness kills every kitty.
    private static void KillAll(Desk desk)
    {
        foreach (var client in desk.Clients())
        {
            var address = client.GetProperty("address").GetString() ?? "";
            desk.Handle(new WindowClosed(new WindowId(Convert.ToInt64(address[2..], 16))));
        }
    }
}
