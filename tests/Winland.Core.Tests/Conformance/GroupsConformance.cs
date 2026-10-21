using System.Text.Json;
using Winland.Core;

namespace Winland.Core.Tests.Conformance;

/// <summary>Winland's group cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class GroupsConformance
{
    [Fact]
    public void GroupDimensions()
    {
        var desk = new Desk();
        desk.Ok("workspace", "2");
        desk.Spawn("kittyA");

        Assert.Equal(1, CountAt(desk, "22,22"));
        Assert.Equal(1, CountSize(desk, "1876,1036"));
        Assert.Equal(1, desk.Clients().Count(client => Desk.Fullscreen(client) == 0));

        desk.Spawn("kittyB");
        desk.Ok("focuswindow", "class:kittyB");
        desk.Ok("togglegroup");
        desk.Ok("focuswindow", "class:kittyA");
        desk.Ok("togglegroup");

        Assert.Equal("931,1015", desk.ActivePlace().Size);
        Assert.Equal(0, Desk.Fullscreen(desk.ActiveWindow()));

        desk.Spawn("kittyC");
        Assert.Equal("931,1015", desk.ActivePlace().Size);
        Assert.Equal(0, Desk.Fullscreen(desk.ActiveWindow()));

        desk.Ok("movewindoworgroup", "r");
        Assert.Equal("931,1015", desk.ActivePlace().Size);
        Assert.Equal(0, Desk.Fullscreen(desk.ActiveWindow()));

        KillAll(desk);
        desk.Spawn("a");
        Assert.Single(desk.Clients());
        Assert.Equal(1, CountAt(desk, "22,22"));
        Assert.Equal(1, CountSize(desk, "1876,1036"));
        Assert.Equal(1, desk.Clients().Count(client => Desk.Fullscreen(client) == 0));

        desk.Ok("togglegroup");
        desk.Keyword("group:groupbar:enabled", "1");
        Assert.Equal(("22,43", "1876,1015"), desk.PlaceOf("a"));

        desk.Keyword("group:groupbar:enabled", "0");
        Assert.Equal(("22,22", "1876,1036"), desk.PlaceOf("a"));

        KillAll(desk);
        desk.Spawn("a");
        desk.Ok("togglegroup");
        Assert.Equal(("22,22", "1876,1036"), desk.PlaceOf("a"));
    }

    [Fact]
    public void GroupNext()
    {
        var desk = new Desk();
        desk.Spawn("a");
        desk.Ok("togglegroup");
        desk.Spawn("b");
        Assert.Equal(2, desk.Clients().Length);

        var last = desk.ActiveId();
        desk.Ok("changegroupactive", "f");
        Assert.NotEqual(last, desk.ActiveId());

        desk.Dispatch("changegroupactive", "f");
        Assert.Equal(last, desk.ActiveId());
    }

    [Fact]
    public void GroupMoveWindow()
    {
        var desk = new Desk();
        desk.Spawn("a");
        desk.Ok("togglegroup");
        desk.Spawn("b");
        Assert.Equal(2, desk.Clients().Length);

        var before = desk.ActiveId();
        desk.Ok("movegroupwindow", "f");
        Assert.Equal(before, desk.ActiveId());

        before = desk.ActiveId();
        desk.Ok("movegroupwindow", "b");
        Assert.Equal(before, desk.ActiveId());
    }

    [Fact]
    public void AutoGroupInsertAfterCurrent()
    {
        var desk = new Desk("group:groupbar:enabled = false\n");
        desk.Spawn("a");
        desk.Ok("togglegroup");
        desk.Spawn("b");
        Assert.Equal(2, desk.Clients().Length);

        desk.Keyword("group:auto_group", "false");
        desk.Keyword("dwindle:force_split", "2");
        desk.Spawn("c");
        Assert.Equal(3, desk.Clients().Length);
        Assert.Equal(2, CountAt(desk, "22,22"));

        desk.Keyword("dwindle:force_split", "0");
        desk.Ok("movefocus", "l");
        desk.Ok("changegroupactive", "1");
        desk.Keyword("group:auto_group", "true");
        desk.Keyword("group:insert_after_current", "false");
        var d = desk.Spawn("d");
        Assert.Equal(4, desk.Clients().Length);

        desk.Ok("changegroupactive", "3");
        Assert.Equal(d, desk.ActiveId());
    }

    [Fact]
    public void MoveWindowOrGroup()
    {
        var desk = new Desk("group:groupbar:enabled = false\n");
        desk.Spawn("e");
        desk.Ok("togglegroup");
        desk.Spawn("f");
        Assert.Equal(2, desk.Clients().Length);
        Assert.Equal(2, CountAt(desk, "22,22"));

        desk.Ok("movewindoworgroup", "r");
        Assert.Equal(1, CountAt(desk, "22,22"));

        desk.Ok("moveintogroup", "l");
        desk.Ok("movewindoworgroup", "d");
        Assert.Equal(1, CountAt(desk, "22,22"));
    }

    [Fact]
    public void GroupLock()
    {
        var desk = new Desk("group:groupbar:enabled = false\n");
        desk.Spawn("unlocked");
        desk.Ok("togglegroup");
        desk.Spawn("top");
        Assert.Equal(2, CountAt(desk, "22,22"));

        KillAll(desk);
        Assert.Empty(desk.Clients());

        desk.Spawn("locked");
        desk.Ok("togglegroup");
        desk.Ok("focuswindow", $"pid:{desk.Client("locked").GetProperty("pid").GetInt64()}");
        desk.Ok("lockactivegroup", "lock");
        desk.Spawn("top");
        Assert.Equal(1, CountAt(desk, "22,22"));

        desk.CloseActive();
        Assert.Single(desk.Clients());

        desk.Ok("lockactivegroup", "unlock");
        desk.Ok("lockactivegroup", "lock");
        desk.Spawn("top");
        Assert.Equal(1, CountAt(desk, "22,22"));

        KillAll(desk);
        Assert.Empty(desk.Clients());

        desk.Keyword("windowrulev2", "group set always lock invade, class:^locked|invade$");
        desk.Spawn("locked");
        desk.Spawn("invade");
        Assert.Equal(2, CountAt(desk, "22,22"));
    }

    [Fact]
    public void GroupsNoCrash()
    {
        var desk = new Desk();
        desk.Spawn("kittyA");
        desk.Ok("togglegroup");
        desk.Ok("togglegroup");

        Assert.Equal("kittyA", desk.ActiveClass());
    }

    [Fact]
    public void GroupsDisableWhenOnly()
    {
        var desk = new Desk();
        desk.Spawn("kittyA");
        Assert.Single(desk.Clients());
        desk.Ok("focuswindow", "class:kittyA");
        desk.ExpectActive("22,22", "1876,1036");

        desk.Ok("togglegroup");
        desk.Keyword("group:groupbar:disable_when_only", "true");
        desk.ExpectActive("22,22", "1876,1036");

        desk.Spawn("kittyB");
        Assert.Equal(2, desk.Clients().Length);
        desk.Ok("focuswindow", "class:kittyB");
        desk.ExpectActive("22,43", "1876,1015");

        desk.CloseActive();
        Assert.Single(desk.Clients());
        desk.Ok("focuswindow", "class:kittyA");
        desk.ExpectActive("22,22", "1876,1036");

        desk.Keyword("group:groupbar:disable_when_only", "false");
        desk.ExpectActive("22,43", "1876,1015");
    }

    [Fact]
    public void GroupbarDisableWhenOnlyDimensions()
    {
        var desk = new Desk("group:groupbar:disable_when_only = true\n");
        desk.Spawn("kittenA");
        desk.Ok("togglegroup");
        desk.ExpectActive("22,22", "1876,1036");

        // The first member gets its tab bar when a second one joins.
        desk.Spawn("kittenB");
        Assert.Equal(("22,43", "1876,1015"), desk.PlaceOf("kittenA"));

        // And loses it when the other member leaves.
        desk.Ok("moveoutofgroup");
        Assert.Equal(("22,22", "931,1036"), desk.PlaceOf("kittenA"));
    }

    [Fact]
    public void AutoGroupFloatedIntoTiled()
    {
        var desk = new Desk("""
            windowrulev2 = tile, class:kitty_tiled
            windowrulev2 = float, class:kitty_floated
            """);
        desk.Spawn("kitty_tiled");
        desk.Ok("togglegroup");
        desk.Spawn("kitty_floated");
        Assert.Equal(2, desk.Clients().Length);

        var floated = desk.Client("kitty_floated");
        Assert.True(Desk.Floating(floated));
        Assert.Equal(0, Grouped(floated));
    }

    [Fact]
    public void AutoGroupFloatedintoFloated()
    {
        var desk = new Desk("""
            windowrulev2 = float, class:kitty_floated_A
            windowrulev2 = float, class:kitty_floated_B
            """);
        desk.Spawn("kitty_floated_A");
        desk.Ok("togglegroup");
        desk.Spawn("kitty_floated_B");
        Assert.Equal(2, desk.Clients().Length);

        var b = desk.Client("kitty_floated_B");
        Assert.True(Desk.Floating(b));
        Assert.NotEqual(0, Grouped(b));
    }

    [Fact]
    public void AutoGroupTiledIntoFloated()
    {
        var desk = new Desk("""
            windowrulev2 = float, class:kitty_floated_A
            windowrulev2 = tile, class:kitty_tiled_B
            """);
        desk.Spawn("kitty_floated_A");
        Assert.True(Desk.Floating(desk.ActiveWindow()));

        desk.Ok("togglegroup");
        desk.Spawn("kitty_tiled_B");
        Assert.Equal(2, desk.Clients().Length);

        var b = desk.Client("kitty_tiled_B");
        Assert.True(Desk.Floating(b));
        Assert.NotEqual(0, Grouped(b));
    }

    [Fact]
    public void GroupedExitWindowRetainsFullscreen()
    {
        var desk = new Desk("misc:exit_window_retains_fullscreen = 3\n");
        desk.Spawn("kitty_A");
        desk.Ok("togglegroup");
        desk.Spawn("kitty_B");

        desk.Ok("fullscreen", "0");
        ExpectFullscreen(desk, 2, 2);

        desk.CloseActive();
        Assert.Single(desk.Clients());
        ExpectFullscreen(desk, 0, 0);

        desk.Spawn("kitty_B");
        desk.Ok("fullscreen", "0");
        desk.Keyword("misc:exit_window_retains_fullscreen", "2");

        desk.CloseActive();
        Assert.Single(desk.Clients());
        ExpectFullscreen(desk, 2, 2);
    }

    // How many clients are placed at x,y, as the reference counts "at: x,y" in its clients reply.
    private static int CountAt(Desk desk, string at) =>
        desk.Clients().Count(client => desk.PlaceOf(Desk.ClassOf(client)).At == at);

    private static int CountSize(Desk desk, string size) =>
        desk.Clients().Count(client => desk.PlaceOf(Desk.ClassOf(client)).Size == size);

    private static int Grouped(JsonElement client) => client.GetProperty("grouped").GetArrayLength();

    // Closes every window, as the reference harness kills every kitty.
    private static void KillAll(Desk desk)
    {
        foreach (var client in desk.Clients())
        {
            var address = client.GetProperty("address").GetString() ?? "";
            desk.Handle(new WindowClosed(new WindowId(Convert.ToInt64(address[2..], 16))));
        }
    }

    private static void ExpectFullscreen(Desk desk, int fullscreen, int fullscreenClient)
    {
        var active = desk.ActiveWindow();
        Assert.Equal(fullscreen, Desk.Fullscreen(active));
        Assert.Equal(fullscreenClient, Desk.FullscreenClient(active));
    }
}
