using System.Text.Json;

namespace Winland.Application.Tests;

/// <summary>Groups, driven through pipe lines on a <see cref="TestDesktop"/>.</summary>
public class GroupBehaviorTests
{
    [Fact]
    public void GroupDimensions()
    {
        var desk = new TestDesktop();
        desk.Ok("workspace", "2");
        desk.Spawn("kittyA");

        Assert.Equal(1, CountAt(desk, "22,22"));
        Assert.Equal(1, CountSize(desk, "1876,1036"));
        Assert.Equal(1, desk.Clients().Count(client => TestDesktop.Fullscreen(client) == 0));

        desk.Spawn("kittyB");
        desk.Ok("focuswindow", "class:kittyB");
        desk.Ok("togglegroup");
        desk.Ok("focuswindow", "class:kittyA");
        desk.Ok("togglegroup");

        Assert.Equal("931,1015", desk.ActivePlace().Size);
        Assert.Equal(0, TestDesktop.Fullscreen(desk.ActiveWindow()));

        desk.Spawn("kittyC");
        Assert.Equal("931,1015", desk.ActivePlace().Size);
        Assert.Equal(0, TestDesktop.Fullscreen(desk.ActiveWindow()));

        KillAll(desk);
        desk.Spawn("a");
        Assert.Single(desk.Clients());
        Assert.Equal(1, CountAt(desk, "22,22"));
        Assert.Equal(1, CountSize(desk, "1876,1036"));
        Assert.Equal(1, desk.Clients().Count(client => TestDesktop.Fullscreen(client) == 0));

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
        var desk = new TestDesktop();
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
        var desk = new TestDesktop();
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
    public void GroupsNoCrash()
    {
        var desk = new TestDesktop();
        desk.Spawn("kittyA");
        desk.Ok("togglegroup");
        desk.Ok("togglegroup");

        Assert.Equal("kittyA", desk.ActiveClass());
    }

    [Fact]
    public void GroupsDisableWhenOnly()
    {
        var desk = new TestDesktop();
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
        var desk = new TestDesktop("group:groupbar:disable_when_only = true\n");
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
        var desk = new TestDesktop("""
            windowrule = tile, class:kitty_tiled
            windowrule = float, class:kitty_floated
            """);
        desk.Spawn("kitty_tiled");
        desk.Ok("togglegroup");
        desk.Spawn("kitty_floated");
        Assert.Equal(2, desk.Clients().Length);

        var floated = desk.Client("kitty_floated");
        Assert.True(TestDesktop.Floating(floated));
        Assert.Equal(0, Grouped(floated));
    }

    [Fact]
    public void AutoGroupFloatedintoFloated()
    {
        var desk = new TestDesktop("""
            windowrule = float, class:kitty_floated_A
            windowrule = float, class:kitty_floated_B
            """);
        desk.Spawn("kitty_floated_A");
        desk.Ok("togglegroup");
        desk.Spawn("kitty_floated_B");
        Assert.Equal(2, desk.Clients().Length);

        var b = desk.Client("kitty_floated_B");
        Assert.True(TestDesktop.Floating(b));
        Assert.NotEqual(0, Grouped(b));
    }

    [Fact]
    public void AutoGroupTiledIntoFloated()
    {
        var desk = new TestDesktop("""
            windowrule = float, class:kitty_floated_A
            windowrule = tile, class:kitty_tiled_B
            """);
        desk.Spawn("kitty_floated_A");
        Assert.True(TestDesktop.Floating(desk.ActiveWindow()));

        desk.Ok("togglegroup");
        desk.Spawn("kitty_tiled_B");
        Assert.Equal(2, desk.Clients().Length);

        var b = desk.Client("kitty_tiled_B");
        Assert.True(TestDesktop.Floating(b));
        Assert.NotEqual(0, Grouped(b));
    }

    [Fact]
    public void GroupedExitWindowDoesNotRetainFullscreen()
    {
        var desk = new TestDesktop();
        desk.Spawn("kitty_A");
        desk.Ok("togglegroup");
        desk.Spawn("kitty_B");

        desk.Ok("fullscreen", "0");
        ExpectFullscreen(desk, 2, 2);

        desk.CloseActive();
        Assert.Single(desk.Clients());
        ExpectFullscreen(desk, 0, 0);
    }

    // How many clients are placed at x,y, as the reference counts "at: x,y" in its clients reply.
    private static int CountAt(TestDesktop desk, string at) =>
        desk.Clients().Count(client => desk.PlaceOf(TestDesktop.ClassOf(client)).At == at);

    private static int CountSize(TestDesktop desk, string size) =>
        desk.Clients().Count(client => desk.PlaceOf(TestDesktop.ClassOf(client)).Size == size);

    private static int Grouped(JsonElement client) => client.GetProperty("grouped").GetArrayLength();

    // Closes every window, as the reference harness kills every kitty.
    private static void KillAll(TestDesktop desk)
    {
        foreach (var client in desk.Clients())
        {
            var address = client.GetProperty("address").GetString() ?? "";
            desk.Handle(new WindowClosed(new WindowId(Convert.ToInt64(address[2..], 16))));
        }
    }

    private static void ExpectFullscreen(TestDesktop desk, int fullscreen, int fullscreenClient)
    {
        var active = desk.ActiveWindow();
        Assert.Equal(fullscreen, TestDesktop.Fullscreen(active));
        Assert.Equal(fullscreenClient, TestDesktop.FullscreenClient(active));
    }
}
