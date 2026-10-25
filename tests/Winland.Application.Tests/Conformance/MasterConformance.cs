namespace Winland.Application.Tests.Conformance;

/// <summary>Winland's master cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class MasterConformance
{
    private const string Master = "general:layout = master\n";

    [Fact]
    public void FocusMasterPrevious()
    {
        var desk = new Desk(Master);
        foreach (var name in new[] { "slave1", "slave2", "slave3", "master" })
            desk.Spawn(name);

        desk.Ok("layoutmsg", "focusmaster master");
        Assert.Equal("master", desk.ActiveClass());

        desk.Ok("layoutmsg", "focusmaster previous");
        Assert.Equal("slave1", desk.ActiveClass());

        desk.Ok("layoutmsg", "cyclenext noloop");
        Assert.Equal("slave2", desk.ActiveClass());
        desk.Ok("layoutmsg", "focusmaster previous");
        Assert.Equal("master", desk.ActiveClass());

        desk.Ok("layoutmsg", "focusmaster previous");
        Assert.Equal("slave2", desk.ActiveClass());

        desk.Ok("layoutmsg", "focusmaster previous");
        Assert.Equal("master", desk.ActiveClass());

        Assert.Equal("ok", desk.Send("r/keyword master:orientation top"));
        ExpectActiveWidth(desk, "22,22", 1876);

        // The orientations cycle top, right, bottom, center, left.
        desk.Ok("layoutmsg", "orientationnext");
        desk.ExpectActive("873,22", "1025,1036");

        desk.Ok("layoutmsg", "orientationnext");
        ExpectActiveWidth(desk, "22,495", 1876);

        desk.Ok("layoutmsg", "orientationnext");
        desk.ExpectActive("450,22", "1020,1036");

        desk.Ok("layoutmsg", "orientationnext");
        desk.ExpectActive("22,22", "1025,1036");
    }

    [Fact]
    public void MasterTestFsFocusUnderFSWindow()
    {
        var desk = new Desk(Master);
        foreach (var name in new[] { "master", "slave1", "slave2" })
            desk.Spawn(name);

        desk.Ok("focuswindow", "class:master");
        desk.Ok("fullscreen", "1");
        desk.ExpectActive("22,22", "1876,1036");
        Assert.Equal("master", desk.ActiveClass());

        desk.Keyword("misc:on_focus_under_fullscreen", "1");
        desk.Spawn("new_master");
        desk.ExpectActive("22,22", "1876,1036");
        Assert.Equal("new_master", desk.ActiveClass());
        Assert.Equal(1, Desk.Fullscreen(desk.ActiveWindow()));

        desk.Keyword("misc:on_focus_under_fullscreen", "0");
        desk.Spawn("ignored");
        desk.ExpectActive("22,22", "1876,1036");
        Assert.Equal("new_master", desk.ActiveClass());
        Assert.Equal(1, Desk.Fullscreen(desk.ActiveWindow()));

        desk.Keyword("misc:on_focus_under_fullscreen", "2");
        desk.Spawn("vaxwashere");
        Assert.Equal("vaxwashere", desk.ActiveClass());
        Assert.Equal(0, Desk.Fullscreen(desk.ActiveWindow()));
    }

    [Fact]
    public void MasterFullscreenMaximiseDispatchers()
    {
        var desk = new Desk(Master);
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
    public void MasterNewWindowTakesOverFullscreen()
    {
        var desk = new Desk(Master);
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
    public void MasterExitWindowRetainsFullscreen()
    {
        var desk = new Desk(Master);
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
    public void MasterFullscreenNonInterference()
    {
        var desk = new Desk(Master);
        string[] names = ["red", "crimson", "blue", "cyan", "azure", "green"];
        foreach (var name in names)
            desk.Spawn(name);

        desk.Ok("focuswindow", "class:red");
        ExpectUnmovedByMaximize(desk, names);

        desk.Ok("setfloating", "class:red");
        ExpectUnmovedByMaximize(desk, names);
    }

    [Fact]
    public void RollFocus()
    {
        var desk = new Desk(Master);
        var windows = new List<string> { "slave1", "slave2", "slave3", "master" };
        foreach (var name in windows)
            desk.Spawn(name);

        desk.Ok("layoutmsg", "focusmaster master");
        Assert.Equal("master", desk.ActiveClass());

        // Focus follows the windows as they rotate, the way a list rotates left by one for rollnext.
        void Roll(string direction)
        {
            desk.Ok("layoutmsg", direction);
            var pivot = direction == "rollnext" ? 1 : windows.Count - 1;
            windows = [.. windows.Skip(pivot), .. windows.Take(pivot)];
            Assert.Equal(windows[^1], desk.ActiveClass());
        }

        for (var i = 0; i < 20; i++)
            Roll("rollnext");

        for (var i = 0; i < 20; i++)
            Roll("rollprev");

        for (var i = 0; i < 10; i++)
        {
            for (var j = 0; j < 5; j++)
                Roll("rollnext");
            Roll("rollprev");
        }

        for (var i = 0; i < 20; i++)
            Roll(i % 2 == 0 ? "rollnext" : "rollprev");

        for (var i = 0; i < 20; i++)
            Roll(i / 5 % 2 == 0 ? "rollnext" : "rollprev");
    }

    [Fact]
    public void FocusMasterClose()
    {
        var desk = new Desk(Master + "master:focus_master_on_close = true\n");
        foreach (var name in new[] { "slave1", "slave2", "slave3", "master" })
            desk.Spawn(name);

        foreach (var slave in new[] { "slave1", "slave2", "slave3" })
        {
            desk.Ok("focuswindow", "class:" + slave);
            desk.Ok("closewindow", "class:" + slave);
            desk.Close(slave);
            Assert.Equal("master", desk.ActiveClass());
        }
    }

    [Fact]
    public void CenterMasterColumnResize()
    {
        var desk = new Desk(Master + """
            master:orientation = center
            master:center_master_fallback = left
            master:slave_count_for_center_master = 2
            """);
        foreach (var name in new[] { "slave1", "slave2", "slave3", "master" })
            desk.Spawn(name);

        // With the left fallback slave1 and slave3 share the left column, and slave2 is alone on the right.
        ExpectColumnResizes(desk, "slave1", "slave3", "slave2");

        Assert.Equal("ok", desk.Send("r/keyword master:smart_resizing false"));
        ExpectColumnResizes(desk, "slave1", "slave3", "slave2");
        Assert.Equal("ok", desk.Send("r/keyword master:smart_resizing true"));

        Assert.Equal("ok", desk.Send("r/keyword master:center_master_fallback right"));
        ExpectColumnResizes(desk, "slave1", "slave3", "slave2");

        // A fourth slave makes two columns of two, and the left pair still resizes.
        Assert.Equal("ok", desk.Send("r/keyword master:center_master_fallback left"));
        desk.Spawn("extra");
        ExpectColumnResizes(desk, "slave1", "slave3", "slave2");

        // With two slaves each column holds one window, so a vertical resize changes nothing.
        foreach (var name in new[] { "slave1", "slave2", "slave3", "master", "extra" })
            desk.Close(name);
        foreach (var name in new[] { "slave1", "slave2", "master" })
            desk.Spawn(name);
        var before = HeightOf(desk, "slave1");
        desk.Ok("resizewindowpixel", "0 80,class:slave1");
        Assert.InRange(HeightOf(desk, "slave1"), before - 2, before + 2);
    }

    [Fact]
    public void MasterCenterDropAtCursor()
    {
        ExpectCenterDrop("L", false, "L", "M", "R");
        ExpectCenterDrop("L", true, "R", "M", "L");
        ExpectCenterDrop("M", false, "M", "L", "R");
        ExpectCenterDrop("M", true, "R", "L", "M");
        ExpectCenterDrop("R", false, "R", "M", "L");
        ExpectCenterDrop("R", true, "L", "M", "R");
    }

    [Fact]
    public void MasterDropAtCursor()
    {
        ExpectTripleDragSwap("slave", false, 1, 2, 2, 1, 3);
        ExpectTripleDragSwap("slave", false, 1, 3, 2, 3, 1);
        ExpectTripleDragSwap("slave", false, 3, 1, 3, 1, 2);
        ExpectTripleDragSwap("slave", false, 3, 2, 1, 3, 2);
        ExpectTripleDragSwap("slave", true, 1, 2, 2, 1, 3);
        ExpectTripleDragSwap("slave", true, 1, 3, 2, 3, 1);
        ExpectTripleDragSwap("slave", true, 3, 1, 3, 2, 1);
        ExpectTripleDragSwap("slave", true, 3, 2, 1, 3, 2);

        ExpectTripleDragSwap("master", false, 1, 2, 2, 1, 3);
        ExpectTripleDragSwap("master", false, 1, 3, 2, 3, 1);
        ExpectTripleDragSwap("master", false, 3, 1, 3, 2, 1);
        ExpectTripleDragSwap("master", false, 3, 2, 1, 3, 2);
        ExpectTripleDragSwap("master", true, 1, 2, 2, 1, 3);
        ExpectTripleDragSwap("master", true, 1, 3, 2, 3, 1);
        ExpectTripleDragSwap("master", true, 3, 1, 3, 1, 2);
        ExpectTripleDragSwap("master", true, 3, 2, 1, 3, 2);

        ExpectTripleDragSwap("inherit", false, 1, 2, 2, 1, 3);
        ExpectTripleDragSwap("inherit", false, 1, 3, 2, 3, 1);
        ExpectTripleDragSwap("inherit", false, 3, 1, 3, 2, 1);
        ExpectTripleDragSwap("inherit", false, 3, 2, 1, 3, 2);
        ExpectTripleDragSwap("inherit", true, 1, 2, 2, 1, 3);
        ExpectTripleDragSwap("inherit", true, 1, 3, 2, 3, 1);
        ExpectTripleDragSwap("inherit", true, 3, 1, 3, 1, 2);
        ExpectTripleDragSwap("inherit", true, 3, 2, 1, 3, 2);
    }

    // Each phase starts from a fresh set of three windows, as the reference kills every window first.
    private static void ExpectCenterDrop(string pick, bool dropRight, string left, string center, string right)
    {
        var desk = new Desk(Master + """
            master:orientation = center
            master:new_status = slave
            master:drop_at_cursor = true
            master:slave_count_for_center_master = 0
            master:center_master_fallback = left
            """);
        foreach (var name in new[] { "w1", "w2", "w3" })
            desk.Spawn(name);

        var initial = Arrangement(desk, ["w1", "w2", "w3"]);
        var role = new Dictionary<string, string> { ["L"] = initial[0], ["M"] = initial[1], ["R"] = initial[2] };

        DragWindow(desk, role[pick], dropRight ? 1800 : 100, 540);

        var final = Arrangement(desk, ["w1", "w2", "w3"]);
        Assert.Equal([role[left], role[center], role[right]], final);
    }

    // Drags window `pick` to where window `place` was, numbered 1 for the master and 2 and 3 for the stack from the top.
    private static void ExpectTripleDragSwap(string status, bool onTop, int pick, int place, int first, int second, int third)
    {
        var desk = new Desk(Master + $"""
            master:drop_at_cursor = true
            master:new_status = {status}
            master:new_on_top = {(onTop ? "true" : "false")}
            """);
        string[] classes = ["w1", "w2", "w3"];
        foreach (var name in classes)
            desk.Spawn(name);

        var initial = Arrangement(desk, classes);
        desk.Ok("focuswindow", "class:" + initial[place - 1]);
        var drop = desk.Placed(desk.ActiveId());

        DragWindow(desk, initial[pick - 1], drop.X, drop.Y);

        Assert.Equal([initial[first - 1], initial[second - 1], initial[third - 1]], Arrangement(desk, classes));
    }

    // The classes ordered left to right, then top to bottom, as the reference sorts their `at`.
    private static string[] Arrangement(Desk desk, string[] classes)
    {
        var places = new List<(int X, int Y, string Class)>();
        foreach (var name in classes)
        {
            desk.Dispatch("focuswindow", "class:" + name);
            var at = desk.Placed(desk.ActiveId());
            places.Add((at.X, at.Y, name));
        }

        return [.. places.OrderBy(place => place.X).ThenBy(place => place.Y).Select(place => place.Class)];
    }

    // The reference test plugin picks the window up with the pointer and drops it at x,y.
    private static void DragWindow(Desk desk, string windowClass, int x, int y)
    {
        var from = desk.Placed(windowClass);
        var start = new Point(from.X + (from.Width / 2), from.Y + (from.Height / 2));
        var drop = new Point(x, y);
        desk.MovePointer(start.X, start.Y);
        desk.Handle(new PointerGesture("movewindow", start, PointerDragPhase.Begin, "mouse:272"));
        desk.Handle(new PointerGesture("movewindow", drop, PointerDragPhase.Move, "mouse:272"));
        desk.Handle(new PointerGesture("movewindow", drop, PointerDragPhase.End, "mouse:272"));
        desk.MovePointer(x, y);
    }

    // `top` and `bottom` share one column, so growing `top` shrinks `bottom` by as much, and `single` in the other column keeps its height.
    private static void ExpectColumnResizes(Desk desk, string top, string bottom, string single)
    {
        Assert.InRange(LeftOf(desk, bottom), LeftOf(desk, top) - 1, LeftOf(desk, top) + 1);
        Assert.True(Math.Abs(LeftOf(desk, single) - LeftOf(desk, top)) > 100);

        var (top0, bottom0, single0) = (HeightOf(desk, top), HeightOf(desk, bottom), HeightOf(desk, single));
        desk.Ok("resizewindowpixel", $"0 80,class:{top}");

        var (top1, bottom1, single1) = (HeightOf(desk, top), HeightOf(desk, bottom), HeightOf(desk, single));
        Assert.True(top1 > top0);
        Assert.True(bottom1 < bottom0);
        Assert.InRange(top1 + bottom1, top0 + bottom0 - 4, top0 + bottom0 + 4);
        Assert.InRange(single1, single0 - 2, single0 + 2);

        desk.Ok("resizewindowpixel", $"0 -80,class:{top}");
    }

    private static int LeftOf(Desk desk, string windowClass)
    {
        desk.Dispatch("focuswindow", "class:" + windowClass);
        return desk.Placed(desk.ActiveId()).X;
    }

    private static int HeightOf(Desk desk, string windowClass)
    {
        desk.Dispatch("focuswindow", "class:" + windowClass);
        return desk.Placed(desk.ActiveId()).Height;
    }

    // The reference checks only that the size starts with the width.
    private static void ExpectActiveWidth(Desk desk, string at, int width)
    {
        var (activeAt, size) = desk.ActivePlace();
        Assert.Equal(at, activeAt);
        Assert.StartsWith(width + ",", size, StringComparison.Ordinal);
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
