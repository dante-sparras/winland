using FluentAssertions;
using Winland.Core.Binds;
using Winland.Core.Chords;
using Winland.Core.Tiling;

namespace Winland.Tests;

[TestClass]
public sealed class BindTests
{
    private static readonly Chord Close = new(ChordModifiers.Super, ChordKey.Q);
    private static readonly Chord Escape = new(ChordModifiers.None, ChordKey.Escape);

    [TestMethod]
    public void Every_window_action_and_nothing_round_trip()
    {
        foreach (var action in Enum.GetValues<WindowAction>())
        {
            var command = new WinlandCommand.Window(action);
            var id = WinlandCommands.IdOf(command);

            WinlandCommands.TryParse(id, out var parsed).Should().BeTrue(id);
            parsed.Should().Be(command);
            WinlandCommands.TryParse(id.ToUpperInvariant(), out var upper).Should().BeTrue(id);
            upper.Should().Be(command);
        }

        var nothing = new WinlandCommand.Nothing();
        WinlandCommands.TryParse(WinlandCommands.IdOf(nothing), out var parsedNothing).Should().BeTrue();
        parsedNothing.Should().Be(nothing);
    }

    [TestMethod]
    public void Shaped_ids_round_trip()
    {
        var commands = new WinlandCommand[]
        {
            new WinlandCommand.JumpWorkspace(3),
            new WinlandCommand.MoveWindow(3, Follow: true),
            new WinlandCommand.MoveWindow(3, Follow: false),
            new WinlandCommand.CycleWorkspace(WorkspaceCycle.Next),
            new WinlandCommand.CycleWorkspace(WorkspaceCycle.Previous),
            new WinlandCommand.CycleWorkspace(WorkspaceCycle.Former),
            new WinlandCommand.Focus(Direction.Right),
            new WinlandCommand.Swap(Direction.Up),
            new WinlandCommand.Resize(-25, 0),
            new WinlandCommand.Resize(300, 0),
            new WinlandCommand.Resize(0, -100),
            new WinlandCommand.Resize(0, 100),
            new WinlandCommand.GroupStep(1),
            new WinlandCommand.GroupStep(-1),
            new WinlandCommand.GroupIndex(4),
            new WinlandCommand.JoinGroup(Direction.Left),
            new WinlandCommand.MoveMonitor(Direction.Down),
        };

        foreach (var command in commands)
        {
            var id = WinlandCommands.IdOf(command);

            WinlandCommands.TryParse(id, out var parsed).Should().BeTrue(id);
            parsed.Should().Be(command);
        }
    }

    [TestMethod]
    public void A_resize_that_is_not_one_edge_is_written_as_resize_left()
    {
        WinlandCommands.IdOf(new WinlandCommand.Resize(5, 5)).Should().Be("resize.left");
        WinlandCommands.IdOf(new WinlandCommand.GroupStep(2)).Should().Be("group.next");
        WinlandCommands.IdOf(new WinlandCommand.GroupStep(0)).Should().Be("group.next");
    }

    [TestMethod]
    public void A_shell_or_plugin_id_is_not_a_winland_command()
    {
        string[] foreign =
        [
            "menu",
            "capture",
            "lock",
            "terminal",
            "plugin:winoma.dictation:toggle",
            "app.music",
            "panel:winoma.audio",
            "right-panel:1",
            "focus.1",
            "focus.right,up",
            "workspace.cycle",
            "workspace.jump.0",
            "snip",
        ];

        foreach (var id in foreign)
        {
            WinlandCommands.TryParse(id, out _).Should().BeFalse(id);
        }
    }

    [TestMethod]
    public void A_global_name_round_trips()
    {
        var plugin = GlobalName.Plugin("winoma.dictation", "toggle");
        GlobalName.TryPlugin(plugin, out var owner, out var command).Should().BeTrue();
        owner.Should().Be("winoma.dictation");
        command.Should().Be("toggle");
        GlobalName.TryWinoma(plugin, out _).Should().BeFalse();

        var shell = GlobalName.Winoma("panel:winoma.audio");
        GlobalName.TryWinoma(shell, out var id).Should().BeTrue();
        id.Should().Be("panel:winoma.audio");
        GlobalName.TryPlugin(shell, out _, out _).Should().BeFalse();

        GlobalName.TryPlugin("winshell:winoma.dictation:", out _, out _).Should().BeFalse();
        GlobalName.TryWinoma("winoma:", out _).Should().BeFalse();
        GlobalName.TryWinoma("winoma:menu ", out _).Should().BeFalse();
    }

    [TestMethod]
    public void An_asked_conditional_covers_the_always_on_bind_until_it_is_asked_off()
    {
        var table = new FixedTable();
        table.Set(Escape, new Dispatch.Local(new WinlandCommand.Window(WindowAction.Close)), hold: true);
        table.Set(Close, new Dispatch.Local(new WinlandCommand.Window(WindowAction.Close)), hold: false);
        var book = new BindBook(table);
        book.Add(new Bind(Escape, new Dispatch.Global(GlobalName.Winoma("escape")), Hold: false, BindConditions.Escape));
        var later = new Dispatch.Global(GlobalName.Winoma("later"));
        book.Add(new Bind(Escape, later, Hold: false, "later"));

        book.Resolve(Escape).Should().Be(new Dispatch.Local(new WinlandCommand.Window(WindowAction.Close)));
        book.IsHold(Escape).Should().BeTrue();

        book.Ask(BindConditions.Escape, true);
        book.Resolve(Escape).Should().Be(new Dispatch.Global(GlobalName.Winoma("escape")));
        book.IsHold(Escape).Should().BeFalse();
        book.ResolveHold(Escape).Should().BeNull();
        book.Resolve(Close).Should().Be(new Dispatch.Local(new WinlandCommand.Window(WindowAction.Close)));

        book.Ask("later", true);
        book.Resolve(Escape).Should().Be(later);

        book.Ask("later", false);
        book.Resolve(Escape).Should().Be(new Dispatch.Global(GlobalName.Winoma("escape")));

        book.Ask(BindConditions.Escape, false);
        book.Resolve(Escape).Should().Be(new Dispatch.Local(new WinlandCommand.Window(WindowAction.Close)));
        book.IsHold(Escape).Should().BeTrue();
    }

    [TestMethod]
    public void A_local_tap_runs_on_the_down_edge_and_a_global_hold_reports_both()
    {
        var table = new FixedTable();
        table.Set(Close, new Dispatch.Local(new WinlandCommand.Window(WindowAction.Close)), hold: false);
        table.Set(Escape, new Dispatch.Global(GlobalName.Plugin("winoma.dictation", "toggle")), hold: true);
        var local = new RecordingDispatch();
        var sink = new RecordingSink();

        BindDispatch.Deliver(table, sink, local, Close, down: true).Should().BeTrue();
        BindDispatch.Deliver(table, sink, local, Close, down: false).Should().BeFalse();
        local.Ran.Should().Equal(new WinlandCommand.Window(WindowAction.Close));

        BindDispatch.Deliver(table, sink, local, Escape, down: true).Should().BeTrue();
        BindDispatch.Deliver(table, sink, local, Escape, down: false).Should().BeTrue();
        sink.Edges.Should().Equal(
            (GlobalName.Plugin("winoma.dictation", "toggle"), true, true),
            (GlobalName.Plugin("winoma.dictation", "toggle"), false, true));

        BindDispatch.Deliver(table, sink, local, new Chord(ChordModifiers.Super, ChordKey.A), down: true).Should().BeFalse();
    }

    private sealed class FixedTable : IBindTable
    {
        private readonly Dictionary<Chord, (Dispatch Dispatch, bool Hold)> _rows = [];

        public void Set(Chord chord, Dispatch dispatch, bool hold) => _rows[chord] = (dispatch, hold);

        public Dispatch? Resolve(Chord chord) => _rows.TryGetValue(chord, out var row) ? row.Dispatch : null;

        public bool IsHold(Chord chord) => _rows.TryGetValue(chord, out var row) && row.Hold;

        public Dispatch? ResolveHold(Chord chord) => IsHold(chord) ? Resolve(chord) : null;
    }

    private sealed class RecordingDispatch : IWinlandDispatch
    {
        public List<WinlandCommand> Ran { get; } = [];

        public void Run(WinlandCommand command) => Ran.Add(command);
    }

    private sealed class RecordingSink : IGlobalBindSink
    {
        public List<(string Name, bool Down, bool Hold)> Edges { get; } = [];

        public void OnGlobal(string name, bool down, bool hold) => Edges.Add((name, down, hold));
    }
}
