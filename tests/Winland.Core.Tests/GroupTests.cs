using System.Text.Json;
using Winland;

namespace Winland.Tests;

public class GroupTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);
    private static readonly WindowId First = new(1);
    private static readonly WindowId Second = new(2);
    private static readonly WindowId Third = new(3);

    // gaps_out 2 above and below, indicator 3, title row 14.
    private const int BarHeight = 21;

    [Fact]
    public void ToggleGroupMakesAGroupOfOneWithABar()
    {
        var core = Opened(auto: true, First);

        var made = core.Execute("dispatch togglegroup");

        Assert.Equal("ok", made.Text);
        Assert.Equal(["togglegroup>>1,1"], Lines(made));
        Assert.Equal([First], Assert.Single(made.Effect.State.Groups));
        var bar = Assert.Single(made.Effect.GroupBars);
        Assert.Equal(First, bar.Shown);
        Assert.Equal(new GroupTab(First, "one", true), Assert.Single(bar.Tabs));
        var tile = ClientOf(made.Effect, First).Bounds;
        Assert.Equal(new Rectangle(tile.X, tile.Y, tile.Width, BarHeight), bar.Bounds);
        Assert.Equal(
            new Rectangle(tile.X, tile.Y + BarHeight, tile.Width, tile.Height - BarHeight),
            Assert.Single(made.Effect.Placements).Bounds);
    }

    [Fact]
    public void AGroupIsOneTileWithSeveralClients()
    {
        var core = Opened(auto: true, First);
        var alone = ClientOf(core.Execute("dispatch togglegroup").Effect, First).Bounds;

        var joined = core.Handle(Appear(Second, "two"));

        Assert.Equal([First, Second], Assert.Single(joined.State.Groups));
        Assert.Contains(First, joined.Hide);
        Assert.Contains(joined.State.Hidden, client => client.Window == First);
        Assert.DoesNotContain(joined.State.Hidden, client => client.Window == Second);
        // The hidden member takes the shown member's frame.
        var placement = Assert.Single(joined.Placements, candidate => candidate.Client == new ClientId(Second.Value));
        Assert.Equal(placement.Bounds, Assert.Single(joined.Placements, candidate => candidate.Client == new ClientId(First.Value)).Bounds);
        Assert.Equal(alone, ClientOf(joined, Second).Bounds);
        Assert.Equal(alone, ClientOf(joined, First).Bounds);
        Assert.Equal(["one", "two"], Assert.Single(joined.GroupBars).Tabs.Select(tab => tab.Title));
    }

    [Fact]
    public void ChangeGroupActiveShowsTheNextMemberAndKeepsFocus()
    {
        var core = Grouped();

        var back = core.Execute("dispatch changegroupactive b");

        Assert.Equal("ok", back.Text);
        Assert.Equal(First, back.Effect.Focus);
        Assert.Contains(Second, back.Effect.Hide);
        Assert.Contains(First, back.Effect.Show);
        var shown = Assert.Single(back.Effect.Placements, placement => placement.Client == new ClientId(First.Value));
        Assert.Equal(shown.Bounds, Assert.Single(back.Effect.Placements, placement => placement.Client == new ClientId(Second.Value)).Bounds);

        var forward = core.Execute("dispatch changegroupactive f");
        Assert.Equal(Second, forward.Effect.Focus);

        var indexed = core.Execute("dispatch changegroupactive 1");
        Assert.Equal(First, indexed.Effect.Focus);

        var last = core.Execute("dispatch changegroupactive 0");
        Assert.Equal(Second, last.Effect.Focus);
    }

    [Fact]
    public void GroupDispatchersRejectAWindowThatIsNotGrouped()
    {
        var core = Opened(auto: true, First);

        Assert.Equal("error: window is not in a group", core.Execute("dispatch changegroupactive f").Text);
        Assert.Equal("error: window not in a group", core.Execute("dispatch moveoutofgroup").Text);
        Assert.Equal("error: window not in a group", core.Execute("dispatch lockactivegroup lock").Text);
        Assert.Equal("error: window not in a group", core.Execute("dispatch movegroupwindow").Text);

        core.Execute("dispatch togglegroup");
        Assert.Equal("error: only one window in group", core.Execute("dispatch changegroupactive f").Text);
        Assert.Equal("error: unsupported direction: x", core.Execute("dispatch moveintogroup x").Text);
    }

    [Fact]
    public void MoveOutOfGroupGivesTheWindowItsOwnTile()
    {
        var core = Grouped();

        var left = core.Execute("dispatch moveoutofgroup");

        Assert.Equal("ok", left.Text);
        Assert.Equal(["moveoutofgroup>>2"], Lines(left));
        Assert.Equal([First], Assert.Single(left.Effect.State.Groups));
        Assert.Contains(First, left.Effect.Show);
        Assert.Empty(left.Effect.State.Hidden);
        Assert.Equal(Second, left.Effect.Focus);
        Assert.NotEqual(ClientOf(left.Effect, First).Bounds, ClientOf(left.Effect, Second).Bounds);
    }

    [Fact]
    public void FocusRemovedWindowOffFocusesTheGroup()
    {
        var core = Grouped("group:focus_removed_window = false\n");

        var left = core.Execute("dispatch moveoutofgroup");

        Assert.Equal(First, left.Effect.Focus);
    }

    [Fact]
    public void MoveIntoGroupJoinsTheGroupInThatDirection()
    {
        var core = Opened(auto: false, First, Second);
        core.Execute("dispatch focuswindow address:0x1");
        core.Execute("dispatch togglegroup");
        core.Execute("dispatch focuswindow address:0x2");

        var moved = core.Execute("dispatch moveintogroup l");

        Assert.Equal("ok", moved.Text);
        Assert.Equal(["moveintogroup>>2"], Lines(moved));
        Assert.Equal([First, Second], Assert.Single(moved.Effect.State.Groups));
        Assert.Equal(Second, moved.Effect.Focus);
        Assert.Contains(First, moved.Effect.Hide);
        Assert.Equal(2, moved.Effect.Placements.Count);
        Assert.Single(moved.Effect.Placements.Select(placement => placement.Bounds).Distinct());
    }

    [Fact]
    public void MoveIntoOrCreateGroupMakesTheGroupFirst()
    {
        var core = Opened(auto: false, First, Second);

        var moved = core.Execute("dispatch moveintoorcreategroup l");

        Assert.Equal(["togglegroup>>1,1", "moveintogroup>>2"], Lines(moved));
        Assert.Equal([First, Second], Assert.Single(moved.Effect.State.Groups));
    }

    [Fact]
    public void ALockedGroupRefusesNewMembers()
    {
        var core = Opened(auto: false, First, Second);
        core.Execute("dispatch focuswindow address:0x1");
        core.Execute("dispatch togglegroup");
        core.Execute("dispatch lockactivegroup lock");
        core.Execute("dispatch focuswindow address:0x2");

        var refused = core.Execute("dispatch moveintogroup l");
        Assert.Equal([First], Assert.Single(refused.Effect.State.Groups));
        Assert.Contains(Assert.Single(refused.Effect.GroupBars).Tabs, tab => tab.Window == First);

        var ignored = Opened(auto: false, "binds:ignore_group_lock = true\n", First, Second);
        ignored.Execute("dispatch focuswindow address:0x1");
        ignored.Execute("dispatch togglegroup");
        ignored.Execute("dispatch lockactivegroup lock");
        ignored.Execute("dispatch focuswindow address:0x2");
        Assert.Equal([First, Second], Assert.Single(ignored.Execute("dispatch moveintogroup l").Effect.State.Groups));
    }

    [Fact]
    public void LockGroupsEmitsTheEventAndStopsAutoGroup()
    {
        var core = Opened(auto: true, First);
        core.Execute("dispatch togglegroup");

        var locked = core.Execute("dispatch lockgroups lock");
        var opened = core.Handle(Appear(Second, "two"));
        var toggled = core.Execute("dispatch lockgroups toggle");

        Assert.Equal(["lockgroups>>1"], Lines(locked));
        Assert.Equal([First], Assert.Single(opened.State.Groups));
        Assert.Equal(["lockgroups>>0"], Lines(toggled));
    }

    [Fact]
    public void ClosingTheShownMemberShowsTheNextOne()
    {
        var core = Grouped();

        var closed = core.Handle(new WindowClosed(Second));

        Assert.Equal(First, closed.Focus);
        Assert.Contains(First, closed.Show);
        Assert.Equal([First], Assert.Single(closed.State.Groups));
        Assert.Empty(closed.State.Hidden);
    }

    [Fact]
    public void ClosingAHiddenMemberLeavesTheTileAlone()
    {
        var core = Grouped();

        var closed = core.Handle(new WindowClosed(First));

        Assert.Empty(closed.Show);
        Assert.Empty(closed.Hide);
        Assert.Equal([Second], Assert.Single(closed.State.Groups));
        Assert.Contains(closed.Events, stream => stream is ClientClosed { Window.Value: 1 });
    }

    [Fact]
    public void HiddenMembersAreRecordedForRecovery()
    {
        var core = Grouped();

        var recovered = core.Recovery();

        Assert.True(recovered.Clients.Single(client => client.Window == First).Hidden);
        Assert.False(recovered.Clients.Single(client => client.Window == Second).Hidden);
    }

    [Fact]
    public void ToggleGroupAgainEndsTheGroup()
    {
        var core = Grouped();
        core.Handle(Appear(Third, "three"));

        var ended = core.Execute("dispatch togglegroup");

        Assert.Equal(["togglegroup>>0,1"], Lines(ended));
        Assert.Empty(ended.Effect.State.Groups);
        Assert.Empty(ended.Effect.GroupBars);
        Assert.Empty(ended.Effect.State.Hidden);
        Assert.Equal(3, ended.Effect.Placements.Count);
        Assert.Equal(3, ended.Effect.Placements.Select(placement => placement.Bounds).Distinct().Count());
    }

    [Fact]
    public void InsertAfterCurrentPlacesNewMembers()
    {
        var after = Grouped();
        after.Execute("dispatch changegroupactive 1");
        Assert.Equal([First, Third, Second], after.Handle(Appear(Third, "three")).State.Groups.Single());

        var tail = Grouped("group:insert_after_current = false\n");
        tail.Execute("dispatch changegroupactive 1");
        Assert.Equal([First, Second, Third], tail.Handle(Appear(Third, "three")).State.Groups.Single());
    }

    [Fact]
    public void MoveGroupWindowReordersTheMembers()
    {
        var core = Grouped();

        var moved = core.Execute("dispatch movegroupwindow b");

        Assert.Equal([Second, First], moved.Effect.State.Groups.Single());
        Assert.Empty(moved.Effect.Show);
        Assert.Empty(moved.Effect.Hide);
    }

    [Fact]
    public void FocusWindowShowsAHiddenMember()
    {
        var core = Grouped();

        var focused = core.Execute("dispatch focuswindow address:0x1");

        Assert.Equal(First, focused.Effect.Focus);
        Assert.Contains(First, focused.Effect.Show);
        Assert.Contains(Second, focused.Effect.Hide);
        Assert.Contains(focused.Effect.State.Hidden, client => client.Window == Second);
    }

    [Fact]
    public void MovingTheShownMemberCarriesTheGroup()
    {
        var core = Grouped();

        var moved = core.Execute("dispatch movetoworkspacesilent 2");

        Assert.All(moved.Effect.State.Clients, client => Assert.Equal(new WorkspaceId("2"), client.Workspace));
        Assert.Equal([First, Second], Assert.Single(moved.Effect.State.Groups));
        Assert.Empty(moved.Effect.GroupBars);

        var back = core.Execute("dispatch workspace 2");
        Assert.Contains(Second, back.Effect.Show);
        Assert.DoesNotContain(First, back.Effect.Show);
        Assert.Single(back.Effect.GroupBars);
    }

    [Fact]
    public void FollowingTheGroupToAnotherWorkspaceKeepsItBelowItsBar()
    {
        var core = Grouped();

        var moved = core.Execute("dispatch movetoworkspace 2");

        var bar = Assert.Single(moved.Effect.GroupBars);
        var placement = moved.Effect.Placements.Single(placement => placement.Client == new ClientId(Second.Value));
        Assert.Equal(bar.Bounds.Y + BarHeight, placement.Bounds.Y);
        Assert.Equal(new WorkspaceId("2"), moved.Effect.State.ActiveWorkspace);
    }

    [Fact]
    public void GroupRuleOpensTheWindowAsAGroup()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = group set lock, class:^(tabs)$\n"));

        var opened = core.Handle(new WindowAppeared(First, Area) { Traits = WindowTraits.Normal with { Class = "tabs", Title = "one" } });

        Assert.Equal([First], Assert.Single(opened.State.Groups));
        Assert.True(Assert.Single(opened.GroupBars).Locked);
        Assert.Contains(opened.Events, stream => stream is GroupToggled { Created: true });
    }

    [Fact]
    public void BarredAndDeniedWindowsDoNotJoinTheFocusedGroup()
    {
        var core = new Core(ConfigParser.Parse("""
            windowrulev2 = group barred, class:^(alone)$
            windowrulev2 = group deny, class:^(never)$
            """));
        core.Handle(Appear(First, "one"));
        core.Execute("dispatch togglegroup");

        var barred = core.Handle(new WindowAppeared(Second, Area) { Traits = WindowTraits.Normal with { Class = "alone" } });
        core.Execute("dispatch focuswindow address:0x1");
        var denied = core.Handle(new WindowAppeared(Third, Area) { Traits = WindowTraits.Normal with { Class = "never" } });

        Assert.Equal([First], Assert.Single(barred.State.Groups));
        Assert.Equal([First], Assert.Single(denied.State.Groups));
    }

    [Fact]
    public void GroupedWindowsUseTheGroupBorderColors()
    {
        var core = Grouped("""
            group:col.border_active = rgb(102030)
            group:col.border_locked_active = rgb(405060)
            """);

        var grouped = core.Execute("dispatch changegroupactive 2");
        Assert.Equal(new BorderColor(0xFF, 0x10, 0x20, 0x30), Decoration(grouped.Effect, Second).Border);

        var locked = core.Execute("dispatch lockactivegroup lock");
        Assert.Equal(new BorderColor(0xFF, 0x40, 0x50, 0x60), Decoration(locked.Effect, Second).Border);
    }

    [Fact]
    public void DisabledGroupBarTakesNoSpace()
    {
        var core = Opened(auto: true, "group:groupbar:enabled = false\n", First);
        var tile = ClientOf(core.Execute("dispatch workspace 1").Effect, First).Bounds;

        var made = core.Execute("dispatch togglegroup");

        Assert.Empty(made.Effect.GroupBars);
        Assert.Equal(tile, Assert.Single(made.Effect.Placements).Bounds);
    }

    [Fact]
    public void GroupBarOptionsShapeTheBar()
    {
        var core = Opened(auto: true, """
            group:groupbar:height = 20
            group:groupbar:indicator_height = 4
            group:groupbar:keep_upper_gap = false
            group:groupbar:gaps_out = 1
            group:groupbar:render_titles = false
            """, First);

        var bar = Assert.Single(core.Execute("dispatch togglegroup").Effect.GroupBars);

        Assert.Equal(1 + 4, bar.Bounds.Height);
        Assert.False(bar.Style.RenderTitles);
    }

    [Fact]
    public void GroupBarBlurIsOffByDefaultAndReachesTheBar()
    {
        Assert.False(GroupBarStyle.Documented.Blur);

        var document = ConfigParser.Parse("group:groupbar:blur = true\n");
        Assert.Empty(document.Warnings);
        Assert.True(GroupOptions.From(document).Bar.Blur);

        var core = Opened(auto: true, "group:groupbar:blur = true\n", First);
        var bar = Assert.Single(core.Execute("dispatch togglegroup").Effect.GroupBars);
        Assert.True(bar.Style.Blur);
        Assert.False(bar.SameAs(bar with { Style = bar.Style with { Blur = false } }));
    }

    [Fact]
    public void TabAtFindsTheTabUnderThePointer()
    {
        var bar = new GroupBar(
            First,
            new Rectangle(100, 100, 302, 21),
            [new GroupTab(First, "a", false), new GroupTab(Second, "b", true)],
            Locked: false,
            GroupBarStyle.Documented);

        Assert.Equal(150, bar.TabWidth);
        Assert.Equal(0, bar.TabAt(0, 10));
        Assert.Equal(-1, bar.TabAt(151, 10));
        Assert.Equal(1, bar.TabAt(152, 10));
        Assert.Equal(-1, bar.TabAt(400, 10));
        Assert.Equal(new Rectangle(152, 16, 150, 3), bar.IndicatorBounds(1));
        Assert.Equal(new Rectangle(152, 2, 150, 14), bar.TitleBounds(1));
    }

    [Fact]
    public void ClientsQueryListsTheGroup()
    {
        var core = Grouped();

        using var clients = JsonDocument.Parse(core.Execute("j/clients").Text);

        foreach (var client in clients.RootElement.EnumerateArray())
            Assert.Equal(["0x1", "0x2"], client.GetProperty("grouped").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void MoveFocusCyclesTheGroupFirstWhenAsked()
    {
        var plain = Grouped();
        Assert.DoesNotContain(plain.Execute("dispatch movefocus l").Effect.Show, window => window == First);

        var cycling = Grouped("binds:movefocus_cycles_groupfirst = true\n");
        var back = cycling.Execute("dispatch movefocus l");
        Assert.Equal(First, back.Effect.Focus);
        Assert.Contains(First, back.Effect.Show);

        var around = cycling.Execute("dispatch movefocus l");
        Assert.Equal(Second, around.Effect.Focus);
    }

    [Fact]
    public void MoveWindowOrGroupLeavesTheGroupWhenANeighborIsThere()
    {
        var core = Opened(auto: false, First, Second);
        core.Execute("dispatch moveintoorcreategroup l");
        core.Handle(Appear(Third, "three"));
        core.Execute("dispatch focuswindow address:0x2");

        var left = core.Execute("dispatch movewindoworgroup r");

        Assert.Equal(["moveoutofgroup>>2"], Lines(left));
        Assert.Equal([First], Assert.Single(left.Effect.State.Groups));
    }

    [Fact]
    public void GroupKeywordsAreHonored()
    {
        var document = ConfigParser.Parse("""
            group:auto_group = false
            group:groupbar:font_size = 10
            group:drag_into_group = 2
            group:groupbar:priority = 2
            """);

        Assert.Empty(document.Warnings);
        var options = GroupOptions.From(document);
        Assert.False(options.AutoGroup);
        Assert.Equal(10, options.Bar.FontSize);
        Assert.Equal(2, options.DragIntoGroup);
        Assert.Equal(2, options.Bar.Priority);
    }

    [Theory]
    [InlineData("dwindle")]
    [InlineData("master")]
    [InlineData("monocle")]
    [InlineData("scrolling")]
    public void SubstituteKeepsTheSlot(string name)
    {
        var layout = LayoutFactory.Fallback(name).Layout;
        layout.Add(First, Area, null, false, default, WorkspaceId.Initial, "one", "one");
        layout.Add(Second, Area, null, false, default, WorkspaceId.Initial, "two", "two");
        Assert.True(layout.TryBounds(Second, out var slot));

        var handed = layout.Substitute(Second, Third, new Taken(false, false, default, "three", "three"), out var outgoing);

        Assert.True(handed);
        Assert.Equal("two", outgoing.Class);
        Assert.Equal([First, Third], layout.Clients.Select(client => client.Window));
        Assert.Equal("three", layout.Clients[1].Class);
        Assert.True(layout.TryBounds(Third, out var kept));
        Assert.Equal(slot, kept);
        Assert.False(layout.TryBounds(Second, out _));
        Assert.False(layout.Substitute(First, Third, default, out _));
    }

    [Fact]
    public void ToggleGroupOnAMaximizedClientReadsTheWorkspaceRulesAgain()
    {
        var core = Opened(auto: true, "workspace = f[1], defaultName:Covered\n", First);
        var maximized = core.Handle(new Dispatch("fullscreen", "1"));
        Assert.Equal("Covered", maximized.State.Workspaces.Single(workspace => workspace.Id == new WorkspaceId("1")).DisplayName);

        var grouped = core.Execute("dispatch togglegroup").Effect;

        Assert.Equal(FullscreenMode.None, ClientOf(grouped, First).Fullscreen);
        Assert.Null(grouped.State.Workspaces.Single(workspace => workspace.Id == new WorkspaceId("1")).DisplayName);
    }

    [Fact]
    public void GroupAddPutsAWindowAtTheIndexAndShowsIt()
    {
        var core = Opened(auto: false, First, Second, Third);
        core.Execute("dispatch focuswindow address:0x1");
        core.Execute("dispatch togglegroup");

        var appended = core.Execute("groupadd address:0x1 address:0x2");
        var first = core.Execute("groupadd address:0x1 address:0x3 1");

        Assert.Equal("ok", appended.Text);
        Assert.Equal([First, Second], Assert.Single(appended.Effect.State.Groups));
        Assert.Equal("ok", first.Text);
        Assert.Equal([Third, First, Second], Assert.Single(first.Effect.State.Groups));
        Assert.DoesNotContain(first.Effect.State.Hidden, client => client.Window == Third);
        Assert.Contains(first.Effect.State.Hidden, client => client.Window == First);
        Assert.Contains(first.Effect.State.Hidden, client => client.Window == Second);
        Assert.Contains(Second, first.Effect.Hide);
        // The Group is the only tile left, and the hidden members take its frame.
        Assert.Equal(Area, ClientOf(first.Effect, Third).Bounds);
        Assert.Equal(Area, ClientOf(first.Effect, First).Bounds);
    }

    [Fact]
    public void GroupAddBringsTheMembersOfAnotherGroupInOrder()
    {
        var core = Opened(auto: false, First, Second, Third);
        core.Execute("dispatch focuswindow address:0x1");
        core.Execute("dispatch togglegroup");
        core.Execute("dispatch focuswindow address:0x2");
        core.Execute("dispatch togglegroup");
        core.Execute("groupadd address:0x2 address:0x3");

        var merged = core.Execute("groupadd address:0x1 address:0x2 1");

        Assert.Equal("ok", merged.Text);
        Assert.Equal([Second, Third, First], Assert.Single(merged.Effect.State.Groups));
        Assert.DoesNotContain(merged.Effect.State.Hidden, client => client.Window == Third);
    }

    [Fact]
    public void GroupAddRefusesAnIndexOutOfRangeAndALockedOrDeniedGroup()
    {
        var core = Opened(auto: false, First, Second);
        core.Execute("dispatch focuswindow address:0x1");
        core.Execute("dispatch togglegroup");

        Assert.Equal("error: index out of range", core.Execute("groupadd address:0x1 address:0x2 3").Text);
        Assert.Equal("error: index out of range", core.Execute("groupadd address:0x1 address:0x2 0").Text);
        Assert.Equal("error: window not in a group", core.Execute("groupadd address:0x2 address:0x1").Text);

        core.Execute("dispatch lockactivegroup lock");
        Assert.Equal("error: window cannot be added to group", core.Execute("groupadd address:0x1 address:0x2").Text);

        core.Execute("dispatch lockactivegroup unlock");
        core.Execute("dispatch denywindowfromgroup on");
        var denied = core.Execute("groupadd address:0x1 address:0x2");

        Assert.Equal("error: target group is denied", denied.Text);
        Assert.Equal([First], Assert.Single(denied.Effect.State.Groups));
    }

    [Fact]
    public void GroupAddEndsTheWindowsFullscreenAndGivesItTheGroups()
    {
        var core = Opened(auto: false, First, Second, Third);
        core.Execute("dispatch focuswindow address:0x1");
        core.Execute("dispatch togglegroup");
        core.Execute("dispatch focuswindow address:0x2");
        core.Execute("dispatch fullscreen");

        var added = core.Execute("groupadd address:0x1 address:0x2").Effect;

        Assert.Equal(FullscreenMode.None, ClientOf(added, Second).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(added, First).Fullscreen);

        core.Execute("dispatch focuswindow address:0x2");
        core.Execute("dispatch fullscreen");
        var taken = core.Execute("groupadd address:0x1 address:0x3").Effect;

        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(taken, Third).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(taken, Second).Fullscreen);
        Assert.Equal(Third, taken.Focus);
    }

    [Fact]
    public void GroupAddMovesAWindowFromAnotherWorkspaceToTheGroups()
    {
        var core = Opened(auto: false, First);
        core.Execute("dispatch togglegroup");
        core.Execute("dispatch workspace 2");
        core.Handle(Appear(Second, "two"));

        var added = core.Execute("groupadd address:0x1 address:0x2").Effect;

        Assert.Equal(new WorkspaceId("1"), ClientOf(added, Second).Workspace);
        Assert.Equal([First, Second], Assert.Single(added.State.Groups));
        Assert.Contains(added.State.Hidden, client => client.Window == Second);
        Assert.Equal(new WorkspaceId("2"), added.State.ActiveWorkspace);
        Assert.Null(added.Focus);
    }

    [Fact]
    public void GroupRemoveReturnsAMemberToTheLayoutAndKeepsTheFocus()
    {
        var core = Opened(auto: false, First, Second, Third);
        core.Execute("dispatch focuswindow address:0x1");
        core.Execute("dispatch togglegroup");
        core.Execute("groupadd address:0x1 address:0x2");
        core.Execute("dispatch focuswindow address:0x3");

        var removed = core.Execute("groupremove address:0x1");

        Assert.Equal("ok", removed.Text);
        Assert.Equal([Second], Assert.Single(removed.Effect.State.Groups));
        Assert.DoesNotContain(removed.Effect.State.Hidden, client => client.Window == First);
        Assert.Contains(First, removed.Effect.Show);
        Assert.Null(removed.Effect.Focus);
        Assert.Equal("0x3", JsonDocument.Parse(core.Execute("j/activewindow").Text).RootElement.GetProperty("address").GetString());
        Assert.Equal("error: window not in a group", core.Execute("groupremove address:0x3").Text);
    }

    private static Core Grouped(string config = "")
    {
        var core = Opened(auto: true, config, First);
        core.Execute("dispatch togglegroup");
        core.Handle(Appear(Second, "two"));
        return core;
    }

    private static Core Opened(bool auto, params WindowId[] windows) => Opened(auto, "", windows);

    private static Core Opened(bool auto, string config, params WindowId[] windows)
    {
        var text = (auto ? "" : "group:auto_group = false\n") + config;
        var core = new Core(ConfigParser.Parse(text));
        string[] titles = ["", "one", "two", "three"];
        foreach (var window in windows)
            core.Handle(Appear(window, titles[window.Value]));

        return core;
    }

    private static WindowAppeared Appear(WindowId window, string title) =>
        new(window, Area) { Traits = WindowTraits.Normal with { Title = title } };

    private static Client ClientOf(Outcome outcome, WindowId window) =>
        outcome.State.Clients.Single(client => client.Window == window);

    private static Decoration Decoration(Outcome outcome, WindowId window) =>
        outcome.Decorations.Single(decoration => decoration.Client == new ClientId(window.Value));

    private static IReadOnlyList<string> Lines(CommandReply reply) =>
        CommandProtocol.FormatStream(reply.Effect.Events, reply.Effect.State)
            .Where(line => !line.StartsWith("activewindow", StringComparison.Ordinal))
            .ToArray();
}
