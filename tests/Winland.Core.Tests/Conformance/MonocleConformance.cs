namespace Winland.Tests.Conformance;

/// <summary>The pinned release's monocle cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class MonocleConformance
{
    private const string Monocle = "general:layout = monocle\n";

    [Fact(Skip = "Closing the shown Client in monocle shows the first Client, not the one focused last.")]
    public void MonocleWorkspaceRule()
    {
        // Monocle shows one tiled Client at a time, so w[tv1] matches whichever is shown.
        var desk = new Desk(Monocle + "workspace = w[tv1], gapsout:40\n");

        foreach (var name in new[] { "neutral_cat", "happy_cat", "angry_cat" })
        {
            desk.Spawn(name);
            ExpectCat(desk, name);
        }

        desk.Close("angry_cat");
        ExpectCat(desk, "happy_cat");

        desk.Spawn("angry_cat");
        ExpectCat(desk, "angry_cat");

        desk.Close("angry_cat");
        desk.Close("happy_cat");
        ExpectCat(desk, "neutral_cat");
    }

    [Fact]
    public void MonocleFullscreenMaximiseDispatchers()
    {
        var desk = new Desk(Monocle);
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

        desk.Ok("fullscreenstate", "2 2 set");
        ExpectFullscreen(desk, 2, 2);

        desk.Ok("fullscreenstate", "2 2 set");
        ExpectFullscreen(desk, 2, 2);

        desk.Ok("fullscreenstate", "2 2 toggle");
        ExpectFullscreen(desk, 0, 0);

        desk.Ok("fullscreenstate", "2 2 toggle");
        ExpectFullscreen(desk, 2, 2);
    }

    [Fact]
    public void MonocleTestFsFocusUnderFSWindow()
    {
        var desk = new Desk(Monocle);
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
    public void MonocleNewWindowTakesOverFullscreen()
    {
        var desk = new Desk(Monocle);
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
    public void MonocleExitWindowRetainsFullscreen()
    {
        var desk = new Desk(Monocle);
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

    [Fact(Skip = "binds:allow_pin_fullscreen is a Gap.")]
    public void MonocleFullscreenPinnedWindows()
    {
        var desk = new Desk(Monocle);
        desk.Spawn("cake");
        desk.Ok("setfloating", "class:cake");
        desk.Ok("resizewindowpixel", "exact 200 200,class:cake");
        desk.Ok("workspace", "1");
        Assert.Equal("ok", desk.Send("r/dispatch pin class:cake"));

        Assert.Equal("ok", desk.Send("r/keyword binds:allow_pin_fullscreen false"));
        desk.Ok("focuswindow", "class:cake");
        foreach (var mode in new[] { "0", "1" })
        {
            desk.Ok("fullscreen", mode);
            Assert.True(Desk.Pinned(desk.ActiveWindow()));
            ExpectFullscreen(desk, 0, 0);
            Assert.Equal("200,200", desk.ActivePlace().Size);
        }

        desk.Ok("workspace", "2");
        Assert.Equal(2, Desk.WorkspaceId(desk.Client("cake")));
        desk.Ok("workspace", "1");

        desk.Ok("fullscreenstate", "2 2 set");
        Assert.Equal(0, Desk.Fullscreen(desk.Client("cake")));
        Assert.Equal(2, Desk.FullscreenClient(desk.Client("cake")));

        desk.Ok("fullscreenstate", "1 1 set");
        Assert.Equal(0, Desk.Fullscreen(desk.Client("cake")));
        Assert.Equal(1, Desk.FullscreenClient(desk.Client("cake")));

        desk.Ok("fullscreenstate", "0 0 set");
        Assert.Equal("ok", desk.Send("r/keyword binds:allow_pin_fullscreen true"));
        desk.Ok("focuswindow", "class:cake");

        desk.Ok("fullscreen", "0");
        Assert.False(Desk.Pinned(desk.ActiveWindow()));
        ExpectFullscreen(desk, 2, 2);
        desk.ExpectActive("0,0", "1920,1080");

        desk.Ok("fullscreen", "1");
        ExpectFullscreen(desk, 1, 1);
        desk.ExpectActive("2,2", "1916,1076");

        desk.Ok("fullscreenstate", "0 0 set");
        desk.Ok("workspace", "2");
        var cake = desk.Client("cake");
        Assert.True(Desk.Pinned(cake));
        Assert.Equal(0, Desk.Fullscreen(cake));
        Assert.Equal("200,200", desk.PlaceOf("cake").Size);
        Assert.Equal(2, Desk.WorkspaceId(cake));

        Assert.Equal("ok", desk.Send("r/keyword binds:allow_pin_fullscreen false"));
        Assert.Equal("ok", desk.Send("r/dispatch pin class:cake"));

        desk.Ok("fullscreen", "0");
        Assert.False(Desk.Pinned(desk.ActiveWindow()));
        ExpectFullscreen(desk, 2, 2);
        desk.ExpectActive("0,0", "1920,1080");

        desk.Ok("fullscreen", "1");
        ExpectFullscreen(desk, 1, 1);
        desk.ExpectActive("2,2", "1916,1076");
    }

    [Fact]
    public void MonocleFullscreenNonInterference()
    {
        var desk = new Desk(Monocle);
        string[] names = ["red", "crimson", "blue", "cyan", "azure", "green"];
        foreach (var name in names)
            desk.Spawn(name);

        desk.Ok("focuswindow", "class:red");
        ExpectUnmovedByMaximize(desk, names);

        desk.Ok("setfloating", "class:red");
        ExpectUnmovedByMaximize(desk, names);
    }

    private static void ExpectCat(Desk desk, string name)
    {
        Assert.Equal(name, desk.ActiveClass());
        desk.ExpectActive("42,42", "1836,996");
    }

    private static void ExpectFullscreen(Desk desk, int fullscreen, int fullscreenClient)
    {
        var active = desk.ActiveWindow();
        Assert.Equal(fullscreen, Desk.Fullscreen(active));
        Assert.Equal(fullscreenClient, Desk.FullscreenClient(active));
    }

    // The reference reads each place by focusing the Client first, which in monocle shows it.
    private static void ExpectUnmovedByMaximize(Desk desk, string[] names)
    {
        var before = names.ToDictionary(name => name, name => FocusedPlace(desk, name));
        desk.Ok("focuswindow", "class:red");
        desk.Ok("fullscreen", "1");
        desk.Ok("fullscreen", "1");
        foreach (var name in names)
            Assert.Equal(before[name], FocusedPlace(desk, name));
    }

    private static (string At, string Size) FocusedPlace(Desk desk, string name)
    {
        desk.Ok("focuswindow", "class:" + name);
        return desk.ActivePlace();
    }
}
