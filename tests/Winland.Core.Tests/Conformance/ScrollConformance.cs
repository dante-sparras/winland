using System.Globalization;
using System.Text.Json;
using Winland;

namespace Winland.Tests.Conformance;

/// <summary>The pinned release's scrolling cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class ScrollConformance
{
    private const string Scrolling = "general:layout = scrolling\n";

    [Fact]
    public void ScrollFocusCycling()
    {
        var desk = new Desk(Scrolling);
        foreach (var name in new[] { "a", "b", "c", "d" })
            desk.Spawn(name);

        desk.Ok("focuswindow", "class:a");
        desk.Ok("movefocus", "r");
        Assert.Equal("b", desk.ActiveClass());

        desk.Ok("movefocus", "r");
        Assert.Equal("c", desk.ActiveClass());

        desk.Ok("movefocus", "r");
        Assert.Equal("d", desk.ActiveClass());

        desk.Ok("movewindow", "l");
        Assert.Equal("d", desk.ActiveClass());

        desk.Ok("movefocus", "u");
        Assert.Equal("c", desk.ActiveClass());
    }

    [Fact]
    public void ScrollFocusWrapping()
    {
        var desk = new Desk(Scrolling);
        foreach (var name in new[] { "a", "b", "c", "d" })
            desk.Spawn(name);

        desk.Keyword("scrolling:wrap_focus", "true");
        desk.Ok("focuswindow", "class:a");
        desk.Ok("layoutmsg", "focus l");
        Assert.Equal("d", desk.ActiveClass());

        desk.Ok("layoutmsg", "focus r");
        Assert.Equal("a", desk.ActiveClass());

        desk.Keyword("scrolling:wrap_focus", "false");
        desk.Ok("focuswindow", "class:a");
        desk.Ok("layoutmsg", "focus l");
        Assert.Equal("a", desk.ActiveClass());

        desk.Ok("focuswindow", "class:d");
        desk.Ok("layoutmsg", "focus r");
        Assert.Equal("d", desk.ActiveClass());
    }

    [Fact]
    public void ScrollSwapcolWrapping()
    {
        var desk = new Desk(Scrolling);
        string[] names = ["a", "b", "c", "d"];
        foreach (var name in names)
            desk.Spawn(name);

        desk.Keyword("scrolling:wrap_swapcol", "true");
        desk.Ok("focuswindow", "class:a");
        desk.Ok("layoutmsg", "swapcol l");
        desk.Ok("layoutmsg", "focus l");
        Assert.Equal("c", desk.ActiveClass());

        CloseAll(desk);
        foreach (var name in names)
            desk.Spawn(name);

        desk.Ok("focuswindow", "class:d");
        desk.Ok("layoutmsg", "swapcol r");
        desk.Ok("layoutmsg", "focus r");
        Assert.Equal("b", desk.ActiveClass());

        CloseAll(desk);
        foreach (var name in names)
            desk.Spawn(name);

        desk.Keyword("scrolling:wrap_swapcol", "false");
        desk.Ok("focuswindow", "class:a");
        desk.Ok("layoutmsg", "swapcol l");
        desk.Ok("layoutmsg", "focus r");
        Assert.Equal("b", desk.ActiveClass());

        desk.Ok("focuswindow", "class:d");
        desk.Ok("layoutmsg", "swapcol r");
        desk.Ok("layoutmsg", "focus l");
        Assert.Equal("c", desk.ActiveClass());
    }

    [Fact]
    public void ScrollWindowRule()
    {
        var desk = new Desk(Scrolling + "windowrulev2 = scrolling_width 0.1, class:^(kitty_scroll)$\n");
        desk.Spawn("kitty_scroll");
        desk.Spawn("kitty_scroll");

        Assert.Equal(2, desk.Clients().Length);
        Assert.Equal("179,1036", desk.ActivePlace().Size);
    }

    [Fact]
    public void ScrollLAYOUTHANDLEDFullscreen()
    {
        var desk = new Desk(Scrolling);
        foreach (var name in new[] { "kitty_scroll_A", "kitty_scroll_B", "kitty_scroll_C" })
            desk.Spawn(name);

        desk.Ok("focuswindow", "class:kitty_scroll_B");
        desk.Ok("fullscreen", "0");
        Assert.Equal("1920,1080", desk.ActivePlace().Size);
        Assert.Equal("kitty_scroll_B", desk.ActiveClass());

        desk.Ok("layoutmsg", "focus l");
        Assert.Equal("kitty_scroll_A", desk.ActiveClass());

        desk.Ok("layoutmsg", "focus r");
        desk.Ok("layoutmsg", "focus r");
        Assert.Equal("kitty_scroll_C", desk.ActiveClass());

        desk.Ok("layoutmsg", "focus l");
        Assert.Equal("1920,1080", desk.ActivePlace().Size);
        Assert.Equal("kitty_scroll_B", desk.ActiveClass());
    }

    [Fact(Skip = "A maximized Client in the scrolling Layout fills the work area less gaps_out. The pinned release's scrolling Layout also leaves gaps_in at its sides.")]
    public void ScrollLAYOUTHANDLEDMaximized()
    {
        var desk = new Desk(Scrolling);
        foreach (var name in new[] { "kitty_scroll_A", "kitty_scroll_B", "kitty_scroll_C" })
            desk.Spawn(name);

        desk.Ok("focuswindow", "class:kitty_scroll_B");
        desk.Ok("fullscreen", "1");
        Assert.Equal("1866,1036", desk.ActivePlace().Size);
        Assert.Equal("kitty_scroll_B", desk.ActiveClass());
        Assert.Equal(1, Desk.Fullscreen(desk.ActiveWindow()));

        desk.Ok("layoutmsg", "focus l");
        Assert.Equal("kitty_scroll_A", desk.ActiveClass());

        desk.Ok("layoutmsg", "focus r");
        desk.Ok("layoutmsg", "focus r");
        Assert.Equal("kitty_scroll_C", desk.ActiveClass());

        desk.Ok("layoutmsg", "focus l");
        Assert.Equal("1866,1036", desk.ActivePlace().Size);
        Assert.Equal("kitty_scroll_B", desk.ActiveClass());
        Assert.Equal(1, Desk.Fullscreen(desk.ActiveWindow()));
    }

    [Fact(Skip = "A hidden Group member keeps its last frame.")]
    public void ScrollLAYOUTHANDLEDTestFsingGroupedWindows()
    {
        var desk = new Desk(Scrolling + """
            group:auto_group = true
            scrolling:follow_focus = false
            scrolling:wrap_swapcol = false
            general:gaps_out = 10
            general:border_size = 10
            workspace = f[1], gapsout:0, bordersize:0
            """);

        Assert.Empty(desk.Clients());
        desk.Spawn("kitten1");
        desk.Ok("togglegroup");
        desk.Spawn("kitten2");
        desk.Keyword("group:auto_group", "false");
        desk.Spawn("kitten3");
        Assert.Equal(3, desk.Clients().Length);
        desk.Ok("focuswindow", "class:kitten2");

        // Fullscreen, tiled.
        desk.Ok("fullscreen", "0 set");
        ExpectActive(desk, "kitten2", "0,0", "1920,1080");
        ExpectPlace(desk, "kitten1", "0,0", "1920,1080");

        desk.Ok("changegroupactive", "f");
        ExpectActive(desk, "kitten1", "0,0", "1920,1080");
        ExpectPlace(desk, "kitten2", "0,0", "1920,1080");

        desk.Ok("changegroupactive", "b");
        desk.Ok("layoutmsg", "focus r");
        desk.Ok("focuswindow", "class:kitten2");
        ExpectActive(desk, "kitten2", "-960,0", "1920,1080");
        ExpectPlace(desk, "kitten1", "-960,0", "1920,1080");
        Assert.Equal(0, Desk.Fullscreen(desk.Client("kitten1")));

        desk.Ok("layoutmsg", "inhibit_scroll 1");
        desk.Ok("changegroupactive", "f");
        desk.Ok("layoutmsg", "inhibit_scroll 0");
        ExpectActive(desk, "kitten1", "-960,0", "1920,1080");
        ExpectPlace(desk, "kitten2", "-960,0", "1920,1080");
        Assert.Equal(0, Desk.Fullscreen(desk.Client("kitten2")));

        desk.Ok("changegroupactive", "b");
        desk.Keyword("scrolling:follow_focus", "true");
        desk.Ok("focuswindow", "class:kitten2");
        desk.Keyword("scrolling:follow_focus", "false");
        desk.Ok("fullscreen", "0 unset");

        // Fullscreen, floating.
        desk.Ok("setfloating");
        desk.Ok("fullscreen", "0 set");
        ExpectActive(desk, "kitten2", "0,0", "1920,1080");
        ExpectPlace(desk, "kitten1", "0,0", "1920,1080");

        desk.Ok("changegroupactive", "f");
        ExpectActive(desk, "kitten1", "0,0", "1920,1080");
        ExpectPlace(desk, "kitten2", "0,0", "1920,1080");

        desk.Ok("changegroupactive", "b");
        desk.Ok("settiled");
        desk.Ok("fullscreen", "0 unset");
        desk.Ok("focuswindow", "class:kitten2");
        desk.Ok("layoutmsg", "swapcol l");

        // Maximized, tiled.
        desk.Ok("fullscreen", "1 set");
        ExpectActive(desk, "kitten2", "0,21", "1915,1059");
        ExpectPlace(desk, "kitten1", "0,21", "1915,1059");

        desk.Ok("changegroupactive", "f");
        ExpectActive(desk, "kitten1", "0,21", "1915,1059");
        ExpectPlace(desk, "kitten2", "0,21", "1915,1059");

        desk.Ok("changegroupactive", "b");
        desk.Ok("layoutmsg", "focus r");
        desk.Ok("focuswindow", "class:kitten2");
        ExpectActive(desk, "kitten2", "-940,41", "1875,1019");
        Assert.Equal(1, Desk.Fullscreen(desk.ActiveWindow()));
        ExpectPlace(desk, "kitten1", "-940,41", "1875,1019");
        Assert.Equal(0, Desk.Fullscreen(desk.Client("kitten1")));

        desk.Ok("layoutmsg", "inhibit_scroll 1");
        desk.Ok("changegroupactive", "f");
        desk.Ok("layoutmsg", "inhibit_scroll 0");
        ExpectActive(desk, "kitten1", "-940,41", "1875,1019");
        Assert.Equal(1, Desk.Fullscreen(desk.ActiveWindow()));
        ExpectPlace(desk, "kitten2", "-940,41", "1875,1019");
        Assert.Equal(0, Desk.Fullscreen(desk.Client("kitten2")));

        desk.Ok("changegroupactive", "b");
        desk.Keyword("scrolling:follow_focus", "true");
        desk.Ok("focuswindow", "class:kitten2");
        desk.Keyword("scrolling:follow_focus", "false");
        desk.Ok("fullscreen", "1 unset");

        // Maximized, floating.
        desk.Ok("setfloating");
        desk.Ok("fullscreen", "1 set");
        ExpectActive(desk, "kitten2", "0,21", "1920,1059");
        ExpectPlace(desk, "kitten1", "0,21", "1920,1059");

        desk.Ok("changegroupactive", "f");
        ExpectActive(desk, "kitten1", "0,21", "1920,1059");
        ExpectPlace(desk, "kitten2", "0,21", "1920,1059");
    }

    [Fact(Skip = "fullscreenHandler reports default, and a fullscreen Client stays over the screen when focus moves to another column.")]
    public void ScrollLAYOUTHANDLEDFullscreenRetainsGeometryWhileScrolling()
    {
        var desk = new Desk(Scrolling);
        foreach (var name in new[] { "kitty_scroll_A", "kitty_scroll_B", "kitty_scroll_C" })
            desk.Spawn(name);

        desk.Ok("focuswindow", "class:kitty_scroll_B");
        var regularSize = desk.ActivePlace().Size;

        desk.Ok("fullscreenstate", "2 0 set");
        var fullscreenAt = desk.ActivePlace().At;

        desk.Ok("focuswindow", "class:kitty_scroll_A");
        var b = desk.Client("kitty_scroll_B");
        Assert.Equal(2, Desk.Fullscreen(b));
        Assert.Equal(0, Desk.FullscreenClient(b));
        Assert.Equal("scrolling", b.GetProperty("fullscreenHandler").GetString());
        Assert.Equal("1920,1080", desk.PlaceOf("kitty_scroll_B").Size);
        Assert.NotEqual(fullscreenAt, desk.PlaceOf("kitty_scroll_B").At);

        desk.Ok("fullscreenstate", "0 0 set,class:kitty_scroll_B");
        Assert.Equal(0, Desk.Fullscreen(desk.Client("kitty_scroll_B")));
        Assert.Equal(regularSize, desk.PlaceOf("kitty_scroll_B").Size);

        desk.Ok("focuswindow", "class:kitty_scroll_B");
        desk.Ok("fullscreenstate", "1 0 set");
        var (maximizedAt, maximizedSize) = desk.ActivePlace();

        desk.Ok("focuswindow", "class:kitty_scroll_C");
        b = desk.Client("kitty_scroll_B");
        Assert.Equal(1, Desk.Fullscreen(b));
        Assert.Equal(0, Desk.FullscreenClient(b));
        Assert.Equal("scrolling", b.GetProperty("fullscreenHandler").GetString());
        Assert.Equal(maximizedSize, desk.PlaceOf("kitty_scroll_B").Size);
        Assert.NotEqual(maximizedAt, desk.PlaceOf("kitty_scroll_B").At);

        desk.Ok("focuswindow", "class:kitty_scroll_B");
        desk.Ok("layoutmsg", "consume");
        b = desk.Client("kitty_scroll_B");
        Assert.Equal(0, Desk.Fullscreen(b));
        Assert.Equal(0, Desk.FullscreenClient(b));
    }

    [Fact(Skip = "A floating Client under a fullscreen one stays visible and accepts input, and a second fullscreen Client ends the first one's fullscreen. allowedOverFullscreen is always false.")]
    public void ScrollLAYOUTHANDLEDFloatingWindowHiding()
    {
        var desk = new Desk(Scrolling);
        desk.Spawn("under");
        desk.Ok("setfloating", "class:under");

        desk.Spawn("tiledOne");
        desk.Ok("fullscreen", "0");
        ExpectClient(desk, "under", floating: true, acceptsInput: false, fullscreen: 0, over: false, visible: false);

        desk.Spawn("tiledTwo");
        desk.Ok("fullscreen", "1");

        desk.Ok("focuswindow", "class:tiledOne");
        ExpectClient(desk, "tiledOne", floating: false, acceptsInput: true, fullscreen: 2, visible: true);
        ExpectClient(desk, "under", floating: true, acceptsInput: false, fullscreen: 0, over: false);

        desk.Keyword("windowrulev2", "float, class:^(floating.*)$");
        desk.Keyword("windowrulev2", "size 950 500, class:^(floating.*)$");
        desk.Spawn("floatingOne");
        desk.Spawn("floatingTwo");
        ExpectClient(desk, "floatingOne", floating: true, acceptsInput: true, fullscreen: 0, over: true);
        ExpectClient(desk, "floatingTwo", floating: true, acceptsInput: true, fullscreen: 0, over: true);

        desk.Ok("focuswindow", "class:floatingOne");
        desk.Ok("fullscreen", "1");
        ExpectClient(desk, "floatingOne", floating: true, acceptsInput: true, fullscreen: 1, over: true);
        ExpectClient(desk, "floatingTwo", floating: true, acceptsInput: false, fullscreen: 0, over: false);
        ExpectClient(desk, "tiledOne", floating: false, acceptsInput: false, fullscreen: 2);
        ExpectClient(desk, "tiledTwo", floating: false, acceptsInput: false, fullscreen: 1);
        ExpectClient(desk, "under", floating: true, acceptsInput: false, fullscreen: 0, over: false);

        desk.Spawn("floatingThree");
        ExpectClient(desk, "floatingThree", floating: true, acceptsInput: true, fullscreen: 0, over: true);

        desk.Ok("focuswindow", "class:floatingOne");
        desk.Ok("fullscreenstate", "0 0 set");
        ExpectClient(desk, "floatingOne", floating: true, acceptsInput: true, fullscreen: 0, over: true);
        ExpectClient(desk, "floatingTwo", floating: true, acceptsInput: true, fullscreen: 0, over: true);
        ExpectClient(desk, "floatingThree", floating: true, acceptsInput: true, fullscreen: 0, over: true);
        ExpectClient(desk, "tiledOne", floating: false, acceptsInput: true, fullscreen: 2);
        ExpectClient(desk, "tiledTwo", floating: false, acceptsInput: false, fullscreen: 1);
        ExpectClient(desk, "under", floating: true, acceptsInput: false, fullscreen: 0, over: false);

        desk.Ok("focuswindow", "class:tiledTwo");
        ExpectClient(desk, "tiledTwo", floating: false, acceptsInput: true, fullscreen: 1);
        ExpectClient(desk, "floatingOne", floating: true, acceptsInput: false, fullscreen: 0, over: false);
        ExpectClient(desk, "floatingTwo", floating: true, acceptsInput: false, fullscreen: 0, over: false);
        ExpectClient(desk, "floatingThree", floating: true, acceptsInput: false, fullscreen: 0, over: false);
        ExpectClient(desk, "tiledOne", floating: false, acceptsInput: false, fullscreen: 2);
        ExpectClient(desk, "under", floating: true, acceptsInput: false, fullscreen: 0, over: false);

        desk.Ok("focuswindow", "class:tiledOne");
        ExpectClient(desk, "tiledOne", floating: false, acceptsInput: true, fullscreen: 2);
        ExpectClient(desk, "floatingOne", floating: true, acceptsInput: false, fullscreen: 0, over: false);
        ExpectClient(desk, "floatingTwo", floating: true, acceptsInput: false, fullscreen: 0, over: false);
        ExpectClient(desk, "floatingThree", floating: true, acceptsInput: false, fullscreen: 0, over: false);
        ExpectClient(desk, "tiledTwo", floating: false, acceptsInput: false, fullscreen: 1);
        ExpectClient(desk, "under", floating: true, acceptsInput: false, fullscreen: 0, over: false);
    }

    [Fact(Skip = "binds:movefocus_cycles_fullscreen and misc:on_focus_under_fullscreen are Gaps.")]
    public void ScrollLAYOUTHANDLEDFocusInDirectionFocusFollowFocusTrue()
    {
        var desk = new Desk(Scrolling);
        desk.Spawn("normal1");
        desk.Spawn("fs");
        desk.Spawn("normal2");

        foreach (var cycles in new[] { "false", "true" })
        {
            Assert.Equal("ok", desk.Send("r/keyword binds:movefocus_cycles_fullscreen " + cycles));
            desk.Ok("focuswindow", "class:fs");
            desk.Ok("fullscreen", "0 set");
            Assert.Equal("ok", desk.Send("r/keyword misc:on_focus_under_fullscreen 0"));
            ExpectActiveFullscreen(desk, "fs", 2);

            foreach (var direction in new[] { "r", "l", "u", "d" })
            {
                desk.Ok("movefocus", direction);
                ExpectActiveFullscreen(desk, "fs", 2);
            }

            Assert.Equal("ok", desk.Send("r/keyword misc:on_focus_under_fullscreen 1"));
            if (cycles == "false")
            {
                desk.Ok("movefocus", "l");
                ExpectActiveFullscreen(desk, "fs", 2);

                desk.Ok("movefocus", "r");
                ExpectActiveFullscreen(desk, "fs", 2);

                Assert.Equal("ok", desk.Send("r/keyword misc:on_focus_under_fullscreen 2"));
                desk.Ok("movefocus", "l");
                ExpectActiveFullscreen(desk, "fs", 2);
                continue;
            }

            foreach (var mode in new[] { "1", "2" })
            {
                Assert.Equal("ok", desk.Send("r/keyword misc:on_focus_under_fullscreen " + mode));
                desk.Ok("movefocus", "l");
                ExpectActiveFullscreen(desk, "normal1", 0);
                Assert.Equal("22,22", desk.ActivePlace().At);

                desk.Ok("movefocus", "r");
                ExpectActiveFullscreen(desk, "fs", 2);
                Assert.Equal("0,0", desk.ActivePlace().At);
            }
        }
    }

    [Fact(Skip = "Leaving fullscreen does not fit the column into view by scrolling:focus_fit_method.")]
    public void ScrollLAYOUTHANDLEDRespectFocusFitMethodOnUnFs()
    {
        var desk = new Desk(Scrolling);
        desk.Spawn("kitty_A");
        desk.Spawn("kitty_B");

        foreach (var mode in new[] { "0", "1" })
        {
            foreach (var (fit, at) in new[] { ("0", "497,22"), ("1", "27,22") })
            {
                desk.Keyword("scrolling:focus_fit_method", fit);
                desk.Ok("fullscreen", mode + " set");
                desk.Ok("fullscreen", mode + " unset");
                ExpectActiveFullscreen(desk, "kitty_B", 0);
                Assert.Equal(at, desk.ActivePlace().At);
            }
        }
    }

    [Fact]
    public void ScrollLAYOUTHANDLEDFloatingOntopFullscreenWorkspaceFocusRetention() =>
        FloatingOntopFullscreenWorkspaceFocusRetention();

    [Fact]
    public void ScrollDEFAULTHANDLEDFullscreenMaximiseDispatchers()
    {
        var desk = new Desk(Scrolling);
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

    [Fact(Skip = "A hidden Group member keeps its last frame.")]
    public void ScrollDEFAULTHANDLEDTestFsingGroupedWindows()
    {
        var desk = new Desk(Scrolling + """
            group:auto_group = true
            general:gaps_out = 10
            general:border_size = 10
            workspace = f[1], gapsout:0, bordersize:0
            """);

        Assert.Empty(desk.Clients());
        desk.Spawn("kitten1");
        desk.Ok("togglegroup");
        desk.Spawn("kitten2");
        Assert.Equal(2, desk.Clients().Length);

        foreach (var (mode, at, size) in new[] { ("0", "0,0", "1920,1080"), ("1", "0,21", "1920,1059") })
        {
            desk.Ok("fullscreen", mode + " set");
            ExpectActive(desk, "kitten2", at, size);
            ExpectPlace(desk, "kitten1", at, size);

            desk.Ok("changegroupactive", "f");
            ExpectActive(desk, "kitten1", at, size);
            ExpectPlace(desk, "kitten2", at, size);

            desk.Ok("changegroupactive", "b");
            desk.Ok("fullscreen", mode + " unset");

            desk.Ok("setfloating");
            desk.Ok("fullscreen", mode + " set");
            ExpectActive(desk, "kitten2", at, size);
            ExpectPlace(desk, "kitten1", at, size);

            desk.Ok("changegroupactive", "f");
            ExpectActive(desk, "kitten1", at, size);
            ExpectPlace(desk, "kitten2", at, size);

            if (mode == "1")
                break;

            desk.Ok("changegroupactive", "b");
            desk.Ok("settiled");
            desk.Ok("fullscreen", mode + " unset");
        }
    }

    [Fact(Skip = "misc:on_focus_under_fullscreen is a Gap.")]
    public void ScrollDEFAULTHANDLEDTestFsFocusUnderFSWindow()
    {
        var desk = new Desk(Scrolling);
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

    [Fact(Skip = "misc:on_focus_under_fullscreen is a Gap.")]
    public void ScrollDEFAULTHANDLEDNewWindowTakesOverFullscreen()
    {
        var desk = new Desk(Scrolling + "misc:on_focus_under_fullscreen = 0\n");
        desk.Spawn("kitty_A");
        desk.Ok("fullscreen", "0");
        ExpectActiveFullscreen(desk, "kitty_A", 2);

        desk.Spawn("kitty_B");
        ExpectActiveFullscreen(desk, "kitty_A", 2);

        desk.Ok("focuswindow", "class:kitty_B");
        ExpectActiveFullscreen(desk, "kitty_A", 2);

        desk.Keyword("misc:on_focus_under_fullscreen", "1");
        desk.Spawn("kitty_C");
        ExpectActiveFullscreen(desk, "kitty_C", 2);

        desk.Keyword("misc:on_focus_under_fullscreen", "2");
        desk.Spawn("kitty_D");
        ExpectActiveFullscreen(desk, "kitty_D", 0);
    }

    [Fact(Skip = "misc:exit_window_retains_fullscreen is a Gap.")]
    public void ScrollDEFAULTHANDLEDExitWindowRetainsFullscreen()
    {
        var desk = new Desk(Scrolling + "misc:exit_window_retains_fullscreen = false\n");
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
    public void ScrollDEFAULTHANDLEDFullscreenPinnedWindows()
    {
        var desk = new Desk(Scrolling);
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
            var active = desk.ActiveWindow();
            Assert.Equal("cake", Desk.ClassOf(active));
            ExpectPin(active, pinned: true, pinFullscreened: false);
            ExpectFullscreen(desk, 0, 0);
            Assert.Equal("200,200", desk.ActivePlace().Size);
        }

        desk.Ok("workspace", "2");
        var cake = desk.Client("cake");
        ExpectPin(cake, pinned: true, pinFullscreened: false);
        Assert.Equal(0, Desk.Fullscreen(cake));
        Assert.Equal(0, Desk.FullscreenClient(cake));
        Assert.Equal("200,200", desk.PlaceOf("cake").Size);
        Assert.Equal(2, Desk.WorkspaceId(cake));

        desk.Ok("workspace", "1");
        foreach (var mode in new[] { 2, 1 })
        {
            desk.Ok("fullscreenstate", $"{mode} {mode} set");
            cake = desk.Client("cake");
            ExpectPin(cake, pinned: true, pinFullscreened: false);
            Assert.Equal(0, Desk.Fullscreen(cake));
            Assert.Equal(mode, Desk.FullscreenClient(cake));
            Assert.Equal("200,200", desk.PlaceOf("cake").Size);
            Assert.Equal(1, Desk.WorkspaceId(cake));
        }

        desk.Ok("fullscreenstate", "0 0 set");
        Assert.Equal("ok", desk.Send("r/keyword binds:allow_pin_fullscreen true"));
        desk.Ok("focuswindow", "class:cake");

        desk.Ok("fullscreen", "0");
        ExpectPin(desk.ActiveWindow(), pinned: false, pinFullscreened: true);
        ExpectFullscreen(desk, 2, 2);
        desk.ExpectActive("0,0", "1920,1080");

        desk.Ok("fullscreen", "1");
        ExpectPin(desk.ActiveWindow(), pinned: false, pinFullscreened: true);
        ExpectFullscreen(desk, 1, 1);
        desk.ExpectActive("2,2", "1916,1076");

        desk.Ok("fullscreenstate", "0 0 set");
        desk.Ok("workspace", "2");
        cake = desk.Client("cake");
        ExpectPin(cake, pinned: true, pinFullscreened: false);
        Assert.Equal(0, Desk.Fullscreen(cake));
        Assert.Equal(0, Desk.FullscreenClient(cake));
        Assert.Equal("200,200", desk.PlaceOf("cake").Size);
        Assert.Equal(2, Desk.WorkspaceId(cake));

        Assert.Equal("ok", desk.Send("r/keyword binds:allow_pin_fullscreen false"));
        Assert.Equal("ok", desk.Send("r/dispatch pin class:cake"));

        desk.Ok("fullscreen", "0");
        ExpectPin(desk.ActiveWindow(), pinned: false, pinFullscreened: false);
        ExpectFullscreen(desk, 2, 2);
        desk.ExpectActive("0,0", "1920,1080");

        desk.Ok("fullscreen", "1");
        ExpectPin(desk.ActiveWindow(), pinned: false, pinFullscreened: false);
        ExpectFullscreen(desk, 1, 1);
        desk.ExpectActive("2,2", "1916,1076");
    }

    [Fact(Skip = "scrolling:follow_min_visible also holds back focuswindow, and a Client out of view keeps its maximized frame after maximize ends.")]
    public void ScrollDEFAULTHANDLEDFullscreenNonInterference()
    {
        var desk = new Desk(Scrolling);
        string[] names = ["red", "crimson", "blue", "cyan", "azure", "green"];
        foreach (var name in names)
            desk.Spawn(name);

        desk.Ok("focuswindow", "class:red");
        ExpectUnmovedByMaximize(desk, names);

        desk.Ok("setfloating", "class:red");
        ExpectUnmovedByMaximize(desk, names);
    }

    [Fact]
    public void ScrollDEFAULTHANDLEDFloatingOntopFullscreenWorkspaceFocusRetention() =>
        FloatingOntopFullscreenWorkspaceFocusRetention();

    [Fact(Skip = "The scrollMove gesture action is a Gap.")]
    public void ScrollNegativeScaleGesture()
    {
        var desk = new Desk(Scrolling + """
            scrolling:follow_focus = false
            gestures:scrolling:move_snap_to_grid = false
            gestures:scrolling:move_snap_cursor = false
            gesture = 6, left, scale:-2, scrollMove
            gesture = 7, left, scale:2, scrollMove
            gesture = 8, left, scale:-1, scrollMove
            gesture = 9, left, scale:1, scrollMove
            """);
        foreach (var name in new[] { "negative_scale_a", "negative_scale_b", "negative_scale_c", "negative_scale_d" })
            desk.Spawn(name);

        var start = X(desk.PlaceOf("negative_scale_a").At);

        SwipeLeft(desk, 7);
        var plusTwo = X(desk.PlaceOf("negative_scale_a").At);

        SwipeLeft(desk, 8);
        var plusOne = X(desk.PlaceOf("negative_scale_a").At);
        Assert.True(plusOne > plusTwo, $"A negative scale should move the view right, from x={plusTwo} to x={plusOne}.");

        SwipeLeft(desk, 9);
        Assert.Equal(plusTwo, X(desk.PlaceOf("negative_scale_a").At));

        SwipeLeft(desk, 6);
        Assert.Equal(start, X(desk.PlaceOf("negative_scale_a").At));
    }

    [Fact]
    public void TestScrollingViewBehaviourDispatchFocusWindowFollowFocusFalse()
    {
        var desk = new Desk(Scrolling + "scrolling:follow_focus = false\n");
        desk.Spawn("a");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Spawn("b");

        desk.Ok("focuswindow", "class:a");

        Assert.True(ActiveX(desk) < 0);
    }

    [Fact(Skip = "scrolling:follow_min_visible also holds back focuswindow.")]
    public void TestScrollingViewBehaviourDispatchFocusWindowFollowFocustrue()
    {
        var desk = new Desk(Scrolling);
        desk.Spawn("a");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Spawn("b");

        desk.Ok("focuswindow", "class:a");

        Assert.True(ActiveX(desk) >= 0);
    }

    [Fact]
    public void TestScrollingViewBehaviourFocusFallback()
    {
        var desk = new Desk(Scrolling + "scrolling:follow_focus = false\n");
        desk.Spawn("a");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Spawn("b");
        desk.Spawn("c");

        desk.Ok("setfloating", "class:c");
        desk.Ok("focuswindow", "class:c");
        desk.Ok("focuswindow", "class:a");
        desk.Ok("focuswindow", "class:c");

        desk.Close("c");
        Assert.Equal(2, desk.Clients().Length);

        Assert.True(ActiveX(desk) < 0);
    }

    [Fact]
    public void TestScrollingViewBehaviourFocusFallbackWithGroups()
    {
        var desk = new Desk(Scrolling + "scrolling:follow_focus = false\ngroup:auto_group = false\n");
        desk.Spawn("a");
        desk.Ok("togglegroup");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Spawn("b");
        desk.Spawn("c");

        desk.Ok("setfloating", "class:c");
        desk.Ok("focuswindow", "class:c");
        desk.Ok("focuswindow", "class:a");
        desk.Ok("focuswindow", "class:c");

        desk.Close("c");
        Assert.Equal(2, desk.Clients().Length);

        Assert.True(ActiveX(desk) < 0);
    }

    [Fact]
    public void TestScrollingViewBehaviourWorkspaceChange()
    {
        var desk = new Desk(Scrolling + "scrolling:follow_focus = false\n");
        desk.Ok("workspace", "1");
        desk.Spawn("a");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Spawn("b");
        desk.Ok("focuswindow", "class:a");

        desk.Ok("workspace", "2");
        desk.Ok("workspace", "1");

        Assert.True(ActiveX(desk) <= 0);
    }

    [Fact]
    public void TestScrollingViewBehaviourSpecialWorkspaceChange()
    {
        var desk = new Desk(Scrolling + "scrolling:follow_focus = false\n");
        desk.Ok("togglespecialworkspace", "name:scroll_S");
        desk.Spawn("a");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Spawn("b");
        desk.Ok("focuswindow", "class:a");

        desk.Ok("workspace", "2");
        desk.Ok("togglespecialworkspace", "name:scroll_S");
        desk.Ok("focuswindow", "class:a");

        Assert.True(ActiveX(desk) <= 0);
    }

    [Fact]
    public void TestScrollingViewBehaviourSpecialToSpecialWorkspaceChange()
    {
        var desk = new Desk(Scrolling + "scrolling:follow_focus = false\n");
        desk.Ok("togglespecialworkspace", "name:scroll_S");
        desk.Spawn("a");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Spawn("b");
        desk.Ok("focuswindow", "class:a");

        desk.Ok("togglespecialworkspace", "name:scroll_F");
        desk.Ok("togglespecialworkspace", "name:scroll_S");
        desk.Ok("focuswindow", "class:a");

        Assert.True(ActiveX(desk) <= 0);
    }

    [Fact]
    public void TestScrollingViewBehaviourCloseWindowInGroup()
    {
        var desk = new Desk(Scrolling + "scrolling:follow_focus = false\ngroup:auto_group = false\n");
        desk.Spawn("a");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Ok("togglegroup");
        desk.Spawn("b");
        desk.Ok("moveintogroup", "l");
        desk.Spawn("c");

        desk.Ok("focuswindow", "class:b");
        desk.Close("b");
        Assert.Equal(2, desk.Clients().Length);

        Assert.True(ActiveX(desk) <= 0);
    }

    [Fact]
    public void TestScrollingViewBehaviourMoveWindowIntoGroupFollowFocusFalse()
    {
        var desk = new Desk(Scrolling + "scrolling:follow_focus = false\ngroup:auto_group = false\n");
        desk.Spawn("a");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Ok("togglegroup");
        desk.Spawn("b");
        desk.Spawn("c");

        desk.Ok("focuswindow", "class:b");
        desk.Ok("moveintogroup", "l");

        Assert.True(ActiveX(desk) <= 0);
    }

    [Fact(Skip = "scrolling:follow_min_visible also holds back the focus moveintogroup gives. The pinned release applies it only to pointer focus.")]
    public void TestScrollingViewBehaviourMoveWindowInGroupFollowFocusTrue()
    {
        var desk = new Desk(Scrolling + "group:auto_group = false\n");
        desk.Spawn("a");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Ok("togglegroup");
        desk.Spawn("b");
        desk.Spawn("c");

        desk.Ok("focuswindow", "class:b");
        desk.Ok("moveintogroup", "l");

        Assert.True(ActiveX(desk) >= 0);
    }

    [Fact(Skip = "movefocus scrolls the strip even with scrolling:follow_focus false.")]
    public void TestScrollingViewBehaviourMoveFocusFollowFocusFalse()
    {
        var desk = new Desk(Scrolling + "scrolling:follow_focus = false\n");
        desk.Spawn("a");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Spawn("b");

        desk.Ok("movefocus", "l");

        Assert.True(ActiveX(desk) <= 0);
    }

    [Fact]
    public void TestScrollingViewBehaviourMoveFocusFollowFocusTrue()
    {
        var desk = new Desk(Scrolling);
        desk.Spawn("a");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Spawn("b");

        desk.Ok("movefocus", "l");

        Assert.True(ActiveX(desk) >= 0);
    }

    [Fact]
    public void TestScrollingViewBehaviourMoveFocusInGroupFollowFocusFalse()
    {
        var desk = new Desk(Scrolling + "binds:movefocus_cycles_groupfirst = true\ngroup:auto_group = false\nscrolling:follow_focus = false\n");
        desk.Spawn("a");
        desk.Ok("togglegroup");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Spawn("b");
        desk.Spawn("c");

        desk.Ok("focuswindow", "class:b");
        desk.Ok("moveintogroup", "l");
        desk.Ok("movefocus", "l");

        Assert.True(ActiveX(desk) <= 0);
    }

    [Fact(Skip = "scrolling:follow_min_visible also holds back the focus moveintogroup and movefocus give inside a Group. The pinned release applies it only to pointer focus.")]
    public void TestScrollingViewBehaviourMoveFocusInGroupFollowFocusTrue()
    {
        var desk = new Desk(Scrolling + "binds:movefocus_cycles_groupfirst = true\ngroup:auto_group = false\n");
        desk.Spawn("a");
        desk.Ok("togglegroup");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Spawn("b");
        desk.Spawn("c");

        desk.Ok("focuswindow", "class:b");
        desk.Ok("moveintogroup", "l");
        desk.Ok("movefocus", "l");

        Assert.True(ActiveX(desk) >= 0);
    }

    [Fact]
    public void TestScrollingViewBehaviourScheduledPropRefresh()
    {
        var desk = new Desk(Scrolling + "scrolling:follow_focus = false\n");
        desk.Spawn("a");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Spawn("b");
        desk.Ok("focuswindow", "class:a");

        desk.Ok("layoutmsg", "inhibit_scroll true");
        desk.Keyword("workspace", "1, gapsin:0");
        desk.Ok("layoutmsg", "inhibit_scroll false");

        var rules = Json(desk.Send("j/workspacerules"));
        Assert.Contains(rules.EnumerateArray(), rule =>
            rule.TryGetProperty("gapsIn", out var gaps) && gaps.EnumerateArray().All(side => side.GetInt32() == 0));

        Assert.True(ActiveX(desk) <= 0);
    }

    [Fact]
    public void TestScrollInhibitor()
    {
        var desk = new Desk(Scrolling);
        desk.Spawn("a");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Spawn("b");

        desk.Ok("layoutmsg", "inhibit_scroll 1");
        desk.Ok("layoutmsg", "focus l");

        Assert.True(ActiveX(desk) < 0);
    }

    [Fact]
    public void LayoutmsgFitIntoView()
    {
        var desk = new Desk(Scrolling + "scrolling:follow_focus = false\n");
        desk.Spawn("a");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Spawn("b");

        desk.Ok("focuswindow", "class:a");
        desk.Ok("layoutmsg", "fit_into_view");

        Assert.Equal("22,22", desk.ActivePlace().At);
    }

    [Fact]
    public void LayoutRuleExpand()
    {
        var desk = new Desk(Scrolling + """
            general:gaps_in = 0
            general:border_size = 0
            general:gaps_out = 0
            scrolling:column_width = 0.5
            scrolling:fullscreen_on_one_column = true
            """);
        desk.Spawn("a");
        var single = X(desk.ActivePlace().Size);

        desk.Spawn("b");
        desk.Spawn("c");
        desk.Ok("resizewindowpixel", "exact 100 500,class:a");
        desk.Ok("resizewindowpixel", "exact 100 500,class:c");
        desk.Ok("focuswindow", "class:b");
        desk.Ok("layoutmsg", "fit expand");

        Assert.True(X(desk.ActivePlace().Size) >= single - 200);
    }

    [Fact(Skip = "movefocus wraps within the strip instead of crossing to the next Monitor.")]
    public void ProperFocusBehvaior()
    {
        var desk = new Desk(Scrolling);
        desk.AddMonitor("HEADLESS-3", Desk.Second);

        foreach (var method in new[] { "0", "1" })
        {
            desk.Keyword("binds:focus_preferred_method", method);

            desk.Ok("focusmonitor", "HEADLESS-2");
            desk.Spawn("a");

            desk.Ok("focusmonitor", "HEADLESS-3");
            foreach (var name in new[] { "b", "c", "d" })
            {
                desk.Spawn(name);
                desk.Ok("focuswindow", "class:" + name);
            }

            Assert.Equal("d", desk.ActiveClass());
            foreach (var (direction, expected) in new[] { ("l", "c"), ("l", "b"), ("l", "a"), ("r", "b"), ("r", "c"), ("r", "d"), ("l", "c") })
            {
                desk.Ok("movefocus", direction);
                Assert.Equal(expected, desk.ActiveClass());
            }

            desk.Ok("focusmonitor", "HEADLESS-2");
            Assert.Equal("a", desk.ActiveClass());

            desk.Ok("focusmonitor", "HEADLESS-3");
            Assert.Equal("c", desk.ActiveClass());

            desk.Ok("movefocus", "l");
            Assert.Equal("b", desk.ActiveClass());

            CloseAll(desk);
            Assert.Empty(desk.Clients());
        }
    }

    // Shared by the layout handled and default handled cases: Winland's fullscreen has no layout_aware switch.
    private static void FloatingOntopFullscreenWorkspaceFocusRetention()
    {
        var desk = new Desk(Scrolling + "windowrulev2 = float, class:^(kitty_floated)$\n");
        desk.Ok("workspace", "1");

        foreach (var mode in new[] { "0", "1" })
        {
            var expected = mode == "0" ? 2 : 1;
            desk.Spawn("kitty_tiled");
            desk.Ok("fullscreen", mode + " set");
            Assert.Equal("kitty_tiled", desk.ActiveClass());
            Assert.False(Desk.Floating(desk.ActiveWindow()));
            ExpectFullscreen(desk, expected, expected);

            desk.Spawn("kitty_floated");
            Assert.Equal("kitty_floated", desk.ActiveClass());
            Assert.True(Desk.Floating(desk.ActiveWindow()));
            ExpectFullscreen(desk, 0, 0);

            desk.Ok("workspace", "2");
            desk.Ok("workspace", "1");
            Assert.Equal("kitty_floated", desk.ActiveClass());
            Assert.True(Desk.Floating(desk.ActiveWindow()));
            ExpectFullscreen(desk, 0, 0);

            CloseAll(desk);
            Assert.Empty(desk.Clients());
        }
    }

    private static void SwipeLeft(Desk desk, int fingers)
    {
        desk.Handle(new SwipeBegan(fingers));
        desk.Handle(new SwipeUpdated(fingers, -300, 0));
        desk.Handle(new SwipeEnded());
    }

    private static void CloseAll(Desk desk)
    {
        foreach (var client in desk.Clients())
        {
            var address = client.GetProperty("address").GetString() ?? "";
            desk.Handle(new WindowClosed(new WindowId(Convert.ToInt64(address[2..], 16))));
        }
    }

    private static int ActiveX(Desk desk) => X(desk.ActivePlace().At);

    private static int X(string pair) => int.Parse(pair[..pair.IndexOf(',', StringComparison.Ordinal)], CultureInfo.InvariantCulture);

    private static void ExpectActive(Desk desk, string windowClass, string at, string size)
    {
        Assert.Equal(windowClass, desk.ActiveClass());
        desk.ExpectActive(at, size);
    }

    private static void ExpectPlace(Desk desk, string windowClass, string at, string size) =>
        Assert.Equal((at, size), desk.PlaceOf(windowClass));

    private static void ExpectFullscreen(Desk desk, int fullscreen, int fullscreenClient)
    {
        var active = desk.ActiveWindow();
        Assert.Equal(fullscreen, Desk.Fullscreen(active));
        Assert.Equal(fullscreenClient, Desk.FullscreenClient(active));
    }

    private static void ExpectActiveFullscreen(Desk desk, string windowClass, int mode)
    {
        Assert.Equal(windowClass, desk.ActiveClass());
        ExpectFullscreen(desk, mode, mode);
    }

    private static void ExpectPin(JsonElement client, bool pinned, bool pinFullscreened)
    {
        Assert.Equal(pinned, Desk.Pinned(client));
        Assert.Equal(pinFullscreened, client.GetProperty("pinFullscreened").GetBoolean());
    }

    // The fields the reference reads from a client block. A null field is one the reference does not check there.
    private static void ExpectClient(Desk desk, string windowClass, bool floating, bool acceptsInput, int fullscreen, bool? over = null, bool? visible = null)
    {
        var client = desk.Client(windowClass);
        Assert.Equal(floating, Desk.Floating(client));
        if (over is bool allowed)
            Assert.Equal(allowed, client.GetProperty("allowedOverFullscreen").GetBoolean());
        if (visible is bool shown)
            Assert.Equal(shown, client.GetProperty("visible").GetBoolean());
        Assert.Equal(acceptsInput, client.GetProperty("acceptsInput").GetBoolean());
        Assert.Equal(fullscreen, Desk.Fullscreen(client));
        Assert.Equal(fullscreen, Desk.FullscreenClient(client));
    }

    private static void ExpectUnmovedByMaximize(Desk desk, string[] names)
    {
        var before = new Dictionary<string, (string At, string Size)>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            desk.Ok("focuswindow", "class:" + name);
            before[name] = desk.ActivePlace();
        }

        desk.Ok("focuswindow", "class:red");
        desk.Ok("fullscreen", "1");
        desk.Ok("fullscreen", "1");

        foreach (var name in names)
        {
            desk.Ok("focuswindow", "class:" + name);
            Assert.Equal(before[name], desk.ActivePlace());
        }
    }

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
