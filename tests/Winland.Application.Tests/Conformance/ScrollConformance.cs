using System.Globalization;
using System.Text.Json;

namespace Winland.Application.Tests.Conformance;

/// <summary>Winland's scrolling cases. Each test is named after its case, and sends the same commands.</summary>
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

        desk.Ok("focuswindow", "class:a");
        desk.Ok("layoutmsg", "focus l");
        Assert.Equal("d", desk.ActiveClass());

        desk.Ok("layoutmsg", "focus r");
        Assert.Equal("a", desk.ActiveClass());
    }

    [Fact]
    public void ScrollSwapcolWrapping()
    {
        var desk = new Desk(Scrolling);
        string[] names = ["a", "b", "c", "d"];
        foreach (var name in names)
            desk.Spawn(name);

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
    }

    [Fact]
    public void ScrollWindowRule()
    {
        var desk = new Desk(Scrolling + "windowrule = scrolling_width 0.1, class:^(kitty_scroll)$\n");
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

    [Fact]
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

    [Fact]
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

        desk.Keyword("windowrule", "float, class:^(floating.*)$");
        desk.Keyword("windowrule", "size 950 500, class:^(floating.*)$");
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
        desk.Ok("fullscreen", "1 unset");
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

    [Fact]
    public void ScrollLAYOUTHANDLEDFocusInDirectionFocusFollowFocusTrue()
    {
        var desk = new Desk(Scrolling);
        desk.Spawn("normal1");
        desk.Spawn("fs");
        desk.Spawn("normal2");

        desk.Ok("focuswindow", "class:fs");
        desk.Ok("fullscreen", "0 set");
        ExpectActiveFullscreen(desk, "fs", 2);

        foreach (var direction in new[] { "r", "l", "u", "d" })
        {
            desk.Ok("movefocus", direction);
            ExpectActiveFullscreen(desk, "fs", 2);
        }
    }

    [Fact]
    public void ScrollLAYOUTHANDLEDFloatingOntopFullscreenWorkspaceFocusRetention() =>
        FloatingOntopFullscreenWorkspaceFocusRetention("");

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
    }

    [Fact]
    public void ScrollDEFAULTHANDLEDTestFsFocusUnderFSWindow()
    {
        var desk = new Desk(Scrolling);
        foreach (var name in new[] { "one", "two", "three" })
            desk.Spawn(name);

        desk.Ok("focuswindow", "class:one");
        desk.Ok("fullscreen", "1 default");
        desk.ExpectActive("22,22", "1876,1036");
        Assert.Equal("one", desk.ActiveClass());

        desk.Spawn("erstarrwashere");
        Assert.Equal("erstarrwashere", desk.ActiveClass());
        Assert.Equal(0, Desk.Fullscreen(desk.ActiveWindow()));
    }

    [Fact]
    public void ScrollDEFAULTHANDLEDNewWindowTakesOverFullscreen()
    {
        var desk = new Desk(Scrolling);
        desk.Spawn("kitty_A");
        desk.Ok("fullscreen", "0 default");
        ExpectActiveFullscreen(desk, "kitty_A", 2);

        desk.Spawn("kitty_B");
        ExpectActiveFullscreen(desk, "kitty_B", 0);
    }

    [Fact]
    public void ScrollDEFAULTHANDLEDExitWindowDoesNotRetainFullscreen()
    {
        var desk = new Desk(Scrolling);
        desk.Spawn("kitty_A");
        desk.Spawn("kitty_B");

        desk.Ok("fullscreen", "0 default");
        ExpectFullscreen(desk, 2, 2);

        desk.CloseActive();
        ExpectFullscreen(desk, 0, 0);
    }

    [Fact]
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
        FloatingOntopFullscreenWorkspaceFocusRetention(" default");

    [Fact]
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
    public void TestScrollingViewBehaviourMoveWindowInGroupFollowFocusTrue()
    {
        var desk = new Desk(Scrolling + "");
        desk.Spawn("a");
        desk.Ok("layoutmsg", "colresize 0.8");
        desk.Spawn("b");
        desk.Spawn("c");
        desk.Ok("focuswindow", "class:a");
        desk.Ok("togglegroup");

        desk.Ok("focuswindow", "class:b");
        desk.Ok("moveintogroup", "l");

        Assert.True(ActiveX(desk) >= 0);
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
    public void TestScrollingViewBehaviourMoveFocusInGroupFollowFocusTrue()
    {
        var desk = new Desk(Scrolling + "binds:movefocus_cycles_groupfirst = true\n");
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
        var desk = new Desk(Scrolling);
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

    [Fact]
    public void ProperFocusBehvaior()
    {
        var desk = new Desk(Scrolling);
        desk.AddMonitor("HEADLESS-3", Desk.Second);

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

    // Shared by the layout handled and default handled cases. The default handled one asks for the default fullscreen.
    private static void FloatingOntopFullscreenWorkspaceFocusRetention(string handler)
    {
        var desk = new Desk(Scrolling + "windowrule = float, class:^(kitty_floated)$\n");
        desk.Ok("workspace", "1");

        foreach (var mode in new[] { "0", "1" })
        {
            var expected = mode == "0" ? 2 : 1;
            desk.Spawn("kitty_tiled");
            desk.Ok("fullscreen", mode + " set" + handler);
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
        desk.Ok("fullscreen", "1 default");
        desk.Ok("fullscreen", "1 default");

        foreach (var name in names)
        {
            desk.Ok("focuswindow", "class:" + name);
            Assert.Equal(before[name], desk.ActivePlace());
        }
    }
}
