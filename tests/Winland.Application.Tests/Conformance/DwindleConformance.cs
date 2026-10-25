namespace Winland.Application.Tests.Conformance;

/// <summary>Winland's dwindle cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class DwindleConformance
{
    [Fact]
    public void DwindleFloatClamp()
    {
        var desk = new Desk();
        foreach (var name in new[] { "a", "b", "c" })
            desk.Spawn(name);

        desk.Keyword("dwindle:force_split", "2");
        desk.Ok("focuswindow", "class:c");
        desk.Ok("setfloating", "class:c");
        desk.Ok("resizewindowpixel", "exact 1200 900,class:c");
        desk.Ok("settiled", "class:c");
        desk.Ok("setfloating", "class:c");

        Assert.Equal("1200,900", desk.PlaceOf("c").Size);
    }

    [Fact]
    public void DwindleIssue13349()
    {
        var desk = new Desk();
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
        var desk = new Desk();
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
        var desk = new Desk();
        Assert.Equal("ok", desk.Send("r/keyword general:gaps_in 0"));
        Assert.Equal("ok", desk.Send("r/keyword general:gaps_out 0"));
        Assert.Equal("ok", desk.Send("r/keyword general:border_size 0"));
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
    public void DwindleForceSplitOnMoveToWorkspace()
    {
        var desk = new Desk();
        desk.Ok("workspace", "2");
        desk.Spawn("kitty");

        desk.Ok("workspace", "1");
        desk.Spawn("kitty2");
        var before = desk.ActivePlace().At;

        desk.Keyword("dwindle:force_split", "2");
        desk.Ok("movecursortocorner", "3");
        desk.Ok("movetoworkspace", "2");

        Assert.NotEqual(before, desk.ActivePlace().At);
    }

    [Fact]
    public void DwindleMoveAcrossToggledSplit()
    {
        var desk = new Desk();
        desk.Keyword("dwindle:force_split", "2");
        desk.Spawn("a");
        desk.Spawn("b");
        desk.Ok("layoutmsg", "togglesplit");

        var b = desk.ActivePlace();
        desk.Ok("focuswindow", "class:a");
        desk.Ok("movewindow", "d");

        Assert.Equal(b, desk.ActivePlace());
    }

    [Fact]
    public void DwindleMoveSmallWindowAcrossSplit()
    {
        var desk = new Desk();
        desk.Keyword("dwindle:force_split", "1");
        desk.Keyword("dwindle:default_split_ratio", "1.2");
        desk.Spawn("a");
        desk.Spawn("b");

        var before = desk.ActivePlace().At;
        desk.Ok("movewindow", "r");

        Assert.NotEqual(before, desk.ActivePlace().At);
    }

    [Fact]
    public void DwindleFullscreenMaximiseDispatchers()
    {
        var desk = new Desk();
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
        var desk = new Desk();
        foreach (var name in new[] { "one", "two", "three" })
            desk.Spawn(name);

        desk.Ok("focuswindow", "class:one");
        desk.Ok("fullscreen", "1");
        desk.ExpectActive("22,22", "1876,1036");
        Assert.Equal("one", desk.ActiveClass());

        desk.Keyword("misc:on_focus_under_fullscreen", "1");
        desk.Spawn("four");
        desk.ExpectActive("22,22", "1876,1036");
        Assert.Equal("four", desk.ActiveClass());
        Assert.Equal(1, Desk.Fullscreen(desk.ActiveWindow()));

        desk.Keyword("misc:on_focus_under_fullscreen", "0");
        desk.Spawn("ignored");
        desk.ExpectActive("22,22", "1876,1036");
        Assert.Equal("four", desk.ActiveClass());
        Assert.Equal(1, Desk.Fullscreen(desk.ActiveWindow()));

        desk.Keyword("misc:on_focus_under_fullscreen", "2");
        desk.Spawn("erstarrwashere");
        Assert.Equal("erstarrwashere", desk.ActiveClass());
        Assert.Equal(0, Desk.Fullscreen(desk.ActiveWindow()));
    }

    [Fact]
    public void DwindleNewWindowTakesOverFullscreen()
    {
        var desk = new Desk();
        desk.Keyword("misc:on_focus_under_fullscreen", "0");
        desk.Spawn("kitty_A");
        desk.Ok("fullscreen", "0");
        ExpectFullscreen(desk, 2, 2);
        Assert.Equal("kitty_A", desk.ActiveClass());

        desk.Spawn("kitty_B");
        ExpectFullscreen(desk, 2, 2);
        Assert.Equal("kitty_A", desk.ActiveClass());

        desk.Ok("focuswindow", "class:kitty_B");
        ExpectFullscreen(desk, 2, 2);
        Assert.Equal("kitty_A", desk.ActiveClass());

        desk.Keyword("misc:on_focus_under_fullscreen", "1");
        desk.Spawn("kitty_C");
        ExpectFullscreen(desk, 2, 2);
        Assert.Equal("kitty_C", desk.ActiveClass());

        desk.Keyword("misc:on_focus_under_fullscreen", "2");
        desk.Spawn("kitty_D");
        ExpectFullscreen(desk, 0, 0);
        Assert.Equal("kitty_D", desk.ActiveClass());
    }

    [Fact]
    public void DwindleExitWindowRetainsFullscreen()
    {
        var desk = new Desk();
        desk.Keyword("misc:exit_window_retains_fullscreen", "false");
        desk.Spawn("kitty_A");
        desk.Spawn("kitty_B");

        desk.Ok("fullscreen", "0");
        ExpectFullscreen(desk, 2, 2);

        desk.CloseActive();
        ExpectFullscreen(desk, 0, 0);

        desk.Spawn("kitty_B");
        desk.Ok("fullscreen", "0");
        desk.Keyword("misc:exit_window_retains_fullscreen", "true");

        desk.CloseActive();
        ExpectFullscreen(desk, 2, 2);
    }

    [Fact]
    public void DwindleFullscreenNonInterference()
    {
        var desk = new Desk();
        string[] names = ["red", "crimson", "blue", "cyan", "azure", "green"];
        foreach (var name in names)
            desk.Spawn(name);

        desk.Ok("focuswindow", "class:red");
        ExpectUnmovedByMaximize(desk, names);

        desk.Ok("setfloating", "class:red");
        ExpectUnmovedByMaximize(desk, names);
    }

    [Fact]
    public void DefaultHandledFsfocusInDirection()
    {
        var desk = new Desk();
        desk.Spawn("normal1");
        desk.Spawn("fs");
        desk.Spawn("normal2");

        Assert.Equal("ok", desk.Send("r/keyword binds:movefocus_cycles_fullscreen false"));
        desk.Ok("focuswindow", "class:fs");
        desk.Ok("fullscreen", "0 set");
        Assert.Equal("ok", desk.Send("r/keyword misc:on_focus_under_fullscreen 0"));

        foreach (var direction in new[] { "r", "l", "u", "d" })
        {
            desk.Ok("movefocus", direction);
            Assert.Equal("fs", desk.ActiveClass());
            ExpectFullscreen(desk, 2, 2);
        }

        Assert.Equal("ok", desk.Send("r/keyword binds:movefocus_cycles_fullscreen true"));
        Assert.Equal("ok", desk.Send("r/keyword misc:on_focus_under_fullscreen 1"));
        desk.Ok("movefocus", "l");
        Assert.Equal("normal1", desk.ActiveClass());
        ExpectFullscreen(desk, 2, 2);

        desk.Ok("movefocus", "r");
        Assert.Equal("fs", desk.ActiveClass());

        Assert.Equal("ok", desk.Send("r/keyword misc:on_focus_under_fullscreen 2"));
        desk.Ok("movefocus", "l");
        Assert.Equal("normal1", desk.ActiveClass());
        ExpectFullscreen(desk, 0, 0);
    }

    [Fact]
    public void DwindleFloatingOntopFullscreenWorkspaceFocusRetention()
    {
        foreach (var mode in new[] { "0", "1" })
        {
            var desk = new Desk("windowrule = float, class:^(kitty_floated)$\n");
            desk.Ok("workspace", "1");
            desk.Spawn("kitty_tiled");

            desk.Ok("fullscreen", mode + " set");
            Assert.Equal("kitty_tiled", desk.ActiveClass());
            Assert.False(Desk.Floating(desk.ActiveWindow()));
            ExpectFullscreen(desk, mode == "0" ? 2 : 1, mode == "0" ? 2 : 1);

            desk.Spawn("kitty_floated");
            Assert.Equal("kitty_floated", desk.ActiveClass());
            Assert.True(Desk.Floating(desk.ActiveWindow()));
            ExpectFullscreen(desk, 0, 0);

            desk.Ok("workspace", "2");
            desk.Ok("workspace", "1");
            Assert.Equal("kitty_floated", desk.ActiveClass());
            Assert.True(Desk.Floating(desk.ActiveWindow()));
            ExpectFullscreen(desk, 0, 0);
        }
    }

    private static void ExpectAny(Desk desk, string at, string size)
    {
        var places = desk.Clients().Select(client => desk.PlaceOf(Desk.ClassOf(client))).ToList();
        Assert.Contains(places, place => place.At == at);
        Assert.Contains(places, place => place.Size == size);
    }

    private static void ExpectFullscreen(Desk desk, int fullscreen, int fullscreenClient)
    {
        var active = desk.ActiveWindow();
        Assert.Equal(fullscreen, Desk.Fullscreen(active));
        Assert.Equal(fullscreenClient, Desk.FullscreenClient(active));
    }

    private static void ExpectUnmovedByMaximize(Desk desk, string[] names)
    {
        var before = names.ToDictionary(name => name, desk.PlaceOf);
        desk.Ok("focuswindow", "class:red");
        desk.Ok("fullscreen", "1");
        desk.Ok("fullscreen", "1");
        foreach (var name in names)
            Assert.Equal(before[name], desk.PlaceOf(name));
    }
}
