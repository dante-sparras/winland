namespace Winland.Application.Tests;

/// <summary>The dwindle Layout, driven through pipe lines on a <see cref="TestDesktop"/>.</summary>
public class DwindleLayoutTests
{
    [Fact]
    public void DwindleFloatClamp()
    {
        var desk = new TestDesktop();
        foreach (var name in new[] { "a", "b", "c" })
            desk.Spawn(name);

        desk.Ok("focuswindow", "class:c");
        desk.Ok("togglefloating", "class:c");
        desk.Place("c", 360, 90, 1200, 900);
        desk.Ok("togglefloating", "class:c");
        desk.Ok("togglefloating", "class:c");

        Assert.Equal("1200,900", desk.PlaceOf("c").Size);
    }

    [Theory]
    [InlineData(300, true)]
    [InlineData(1600, false)]
    public void DwindleRetileSplitsAtThePointer(int x, bool left)
    {
        var desk = new TestDesktop();
        desk.Spawn("a");
        desk.Spawn("b");
        desk.Ok("togglefloating", "class:a");
        desk.MovePointer(x, 540);

        desk.Ok("togglefloating", "class:a");

        Assert.Equal(left, desk.Placed("a").X < desk.Placed("b").X);
    }

    [Theory]
    [InlineData("a", "-100 0", -100, 0)]
    [InlineData("a", "100 0", 100, 0)]
    [InlineData("b", "-100 0", -100, 0)]
    [InlineData("b", "100 0", 100, 0)]
    public void DwindleResizeActiveChangesTheActiveTilesOwnSize(string active, string delta, int width, int height)
    {
        var desk = new TestDesktop();
        desk.Spawn("a");
        desk.Spawn("b");
        desk.Ok("focuswindow", $"class:{active}");
        var before = desk.Placed(active);

        desk.Ok("resizeactive", delta);

        var after = desk.Placed(active);
        Assert.Equal((width, height), (after.Width - before.Width, after.Height - before.Height));
    }

    [Fact]
    public void DwindleResizeActiveExactGivesASecondTileThatSize()
    {
        var desk = new TestDesktop();
        desk.Spawn("a");
        desk.Spawn("b");
        desk.Ok("focuswindow", "class:b");
        var height = desk.Placed("b").Height;

        desk.Ok("resizeactive", "exact 600 " + height);

        Assert.Equal(600, desk.Placed("b").Width);
    }

    [Fact]
    public void DwindleIssue13349()
    {
        var desk = new TestDesktop();
        foreach (var name in new[] { "a", "b", "c" })
            desk.Spawn(name);

        desk.Ok("focuswindow", "class:c");
        desk.ExpectActive("967,547", "931,511");

        desk.Ok("movewindow", "l");
        desk.ExpectActive("22,547", "931,511");

        desk.Ok("movewindow", "r");
        desk.ExpectActive("967,547", "931,511");
    }

    [Fact]
    public void DwindleSplit()
    {
        var desk = new TestDesktop();
        desk.Spawn("a");

        Assert.NotEqual("ok", desk.Dispatch("layoutmsg", "swapsplit"));
        Assert.NotEqual("ok", desk.Dispatch("layoutmsg", "splitratio 1 exact"));

        desk.Spawn("b");
        desk.Ok("focuswindow", "class:a");
        desk.Ok("layoutmsg", "splitratio -0.2");
        desk.ExpectActive("22,22", "743,1036");

        desk.Ok("layoutmsg", "splitratio 1.6 exact");
        desk.ExpectActive("22,22", "1495,1036");

        foreach (var bad in new[] { "splitratio fhne exact", "splitratio exact", "splitratio -....9", "splitratio ..9", "splitratio" })
            Assert.NotEqual("ok", desk.Dispatch("layoutmsg", bad));

        desk.Ok("layoutmsg", "togglesplit");
        desk.ExpectActive("22,22", "1876,823");

        desk.Ok("layoutmsg", "swapsplit");
        desk.ExpectActive("22,859", "1876,199");
    }

    [Fact]
    public void DwindleRotateSplit()
    {
        var desk = new TestDesktop();
        Assert.Equal("ok", desk.Send("keyword general:gaps_in 0"));
        Assert.Equal("ok", desk.Send("keyword general:gaps_out 0"));
        Assert.Equal("ok", desk.Send("keyword general:border_size 0"));
        desk.Spawn("a");
        desk.Spawn("b");

        ExpectAny(desk, "0,0", "960,1080");

        desk.Ok("layoutmsg", "rotatesplit");
        ExpectAny(desk, "0,0", "1920,540");

        desk.Ok("layoutmsg", "rotatesplit");
        ExpectAny(desk, "960,0", "960,1080");

        desk.Ok("layoutmsg", "rotatesplit");
        ExpectAny(desk, "0,540", "1920,540");

        desk.Ok("layoutmsg", "rotatesplit");
        ExpectAny(desk, "0,0", "960,1080");

        desk.Ok("layoutmsg", "rotatesplit 180");
        ExpectAny(desk, "960,0", "960,1080");

        desk.Ok("layoutmsg", "rotatesplit 270");
        ExpectAny(desk, "0,540", "1920,540");

        desk.Ok("layoutmsg", "rotatesplit 360");
        ExpectAny(desk, "0,0", "1920,540");

        desk.Ok("layoutmsg", "rotatesplit -90");
        ExpectAny(desk, "0,0", "960,1080");

        desk.Ok("layoutmsg", "rotatesplit -180");
        ExpectAny(desk, "960,0", "960,1080");
    }

    [Fact]
    public void DwindleMoveTowardTheEdgeItIsAtMovesNothing()
    {
        var desk = new TestDesktop();
        desk.Spawn("a");
        desk.Spawn("b");
        desk.Spawn("c");
        var before = new[] { desk.Placed("a"), desk.Placed("b"), desk.Placed("c") };

        desk.Ok("focuswindow", "class:a");
        desk.Dispatch("movewindow", "l");

        Assert.Equal(before, new[] { desk.Placed("a"), desk.Placed("b"), desk.Placed("c") });
    }

    [Fact]
    public void DwindleMoveAcrossToggledSplit()
    {
        var desk = new TestDesktop();
        desk.Spawn("a");
        desk.Spawn("b");
        desk.Ok("layoutmsg", "togglesplit");

        var b = desk.ActivePlace();
        desk.Ok("focuswindow", "class:a");
        desk.Ok("movewindow", "d");

        Assert.Equal(b, desk.ActivePlace());
    }

    [Fact]
    public void DwindleFullscreenMaximiseDispatchers()
    {
        var desk = new TestDesktop();
        desk.Keyword("general:layout", "dwindle");
        desk.Spawn("kitty_A");
        desk.Spawn("kitty_B");

        desk.Ok("focuswindow", "class:kitty_A");
        desk.Ok("fullscreen", "0 set");
        ExpectFullscreen(desk, 2, 2);

        desk.Ok("fullscreen", "0 unset");
        ExpectFullscreen(desk, 0, 0);

        desk.Ok("fullscreen", "1 toggle");
        ExpectFullscreen(desk, 1, 1);

        desk.Ok("fullscreen", "1 toggle");
        ExpectFullscreen(desk, 0, 0);
    }

    [Fact]
    public void DwindleTestFsFocusUnderFSWindow()
    {
        var desk = new TestDesktop();
        foreach (var name in new[] { "one", "two", "three" })
            desk.Spawn(name);

        desk.Ok("focuswindow", "class:one");
        desk.Ok("fullscreen", "1");
        desk.ExpectActive("22,22", "1876,1036");
        Assert.Equal("one", desk.ActiveClass());

        desk.Spawn("erstarrwashere");
        Assert.Equal("erstarrwashere", desk.ActiveClass());
        Assert.Equal(0, TestDesktop.Fullscreen(desk.ActiveWindow()));
    }

    [Fact]
    public void DwindleNewWindowTakesOverFullscreen()
    {
        var desk = new TestDesktop();
        desk.Spawn("kitty_A");
        desk.Ok("fullscreen", "0");
        ExpectFullscreen(desk, 2, 2);
        Assert.Equal("kitty_A", desk.ActiveClass());

        desk.Spawn("kitty_B");
        ExpectFullscreen(desk, 0, 0);
        Assert.Equal("kitty_B", desk.ActiveClass());
    }

    [Fact]
    public void DwindleExitWindowDoesNotRetainFullscreen()
    {
        var desk = new TestDesktop();
        desk.Spawn("kitty_A");
        desk.Spawn("kitty_B");

        desk.Ok("fullscreen", "0");
        ExpectFullscreen(desk, 2, 2);

        desk.CloseActive();
        ExpectFullscreen(desk, 0, 0);
    }

    [Fact]
    public void DwindleFullscreenNonInterference()
    {
        var desk = new TestDesktop();
        string[] names = ["red", "crimson", "blue", "cyan", "azure", "green"];
        foreach (var name in names)
            desk.Spawn(name);

        desk.Ok("focuswindow", "class:red");
        ExpectUnmovedByMaximize(desk, names);

        desk.Ok("togglefloating", "class:red");
        ExpectUnmovedByMaximize(desk, names);
    }

    [Fact]
    public void DefaultHandledFsfocusInDirection()
    {
        var desk = new TestDesktop();
        desk.Spawn("normal1");
        desk.Spawn("fs");
        desk.Spawn("normal2");

        desk.Ok("focuswindow", "class:fs");
        desk.Ok("fullscreen", "0 set");

        foreach (var direction in new[] { "r", "l", "u", "d" })
        {
            desk.Ok("movefocus", direction);
            Assert.Equal("fs", desk.ActiveClass());
            ExpectFullscreen(desk, 2, 2);
        }
    }

    [Fact]
    public void DwindleFloatingOntopFullscreenWorkspaceFocusRetention()
    {
        foreach (var mode in new[] { "0", "1" })
        {
            var desk = new TestDesktop("windowrule = float, class:^(kitty_floated)$\n");
            desk.Ok("workspace", "1");
            desk.Spawn("kitty_tiled");

            desk.Ok("fullscreen", mode + " set");
            Assert.Equal("kitty_tiled", desk.ActiveClass());
            Assert.False(TestDesktop.Floating(desk.ActiveWindow()));
            ExpectFullscreen(desk, mode == "0" ? 2 : 1, mode == "0" ? 2 : 1);

            desk.Spawn("kitty_floated");
            Assert.Equal("kitty_floated", desk.ActiveClass());
            Assert.True(TestDesktop.Floating(desk.ActiveWindow()));
            ExpectFullscreen(desk, 0, 0);

            desk.Ok("workspace", "2");
            desk.Ok("workspace", "1");
            Assert.Equal("kitty_floated", desk.ActiveClass());
            Assert.True(TestDesktop.Floating(desk.ActiveWindow()));
            ExpectFullscreen(desk, 0, 0);
        }
    }

    private static void ExpectAny(TestDesktop desk, string at, string size)
    {
        var places = desk.Clients().Select(client => desk.PlaceOf(TestDesktop.ClassOf(client))).ToList();
        Assert.Contains(places, place => place.At == at);
        Assert.Contains(places, place => place.Size == size);
    }

    private static void ExpectFullscreen(TestDesktop desk, int fullscreen, int fullscreenClient)
    {
        var active = desk.ActiveWindow();
        Assert.Equal(fullscreen, TestDesktop.Fullscreen(active));
        Assert.Equal(fullscreenClient, TestDesktop.FullscreenClient(active));
    }

    private static void ExpectUnmovedByMaximize(TestDesktop desk, string[] names)
    {
        var before = names.ToDictionary(name => name, desk.PlaceOf);
        desk.Ok("focuswindow", "class:red");
        desk.Ok("fullscreen", "1");
        desk.Ok("fullscreen", "1");
        foreach (var name in names)
            Assert.Equal(before[name], desk.PlaceOf(name));
    }
}
