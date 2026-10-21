using System.Text.Json;
using Winland;

namespace Winland.Tests.Conformance;

/// <summary>
/// Winland's swallow case. The reference starts kitty processes from other kitty processes; here each is a
/// window whose process descends from the kitty that started it.
/// </summary>
[Trait("Category", "Conformance")]
public class SwallowConformance
{
    private const string None = "0x0";

    [Fact]
    public void Swallow()
    {
        // The case turns swallowing on before its first window opens.
        var desk = new Desk("misc:enable_swallow = true\nmisc:swallow_regex = ^(kitty_swallowee)$\n");
        var kitties = new Kitties(desk);

        desk.Ok("workspace", "name:swallow");
        Assert.Empty(desk.Clients());

        // The window that will be swallowed.
        kitties.RemoteControl("swallowee");
        Assert.Single(desk.Clients());
        Assert.Equal(0, SwallowingCount(desk));
        var swallowee = ActiveAddress(desk);

        // A child process that swallows it.
        kitties.Swallower("swallowee", "swallower");
        Assert.Equal(1, CountSwallowing(desk, None));
        Assert.Equal(1, CountSwallowing(desk, swallowee));
        Assert.True(HasWorkspaceWithWindows(desk, 1));

        desk.Ok("toggleswallow");
        Assert.Equal(1, CountSwallowing(desk, None));
        Assert.Equal(1, CountSwallowing(desk, swallowee));
        Assert.True(HasWorkspaceWithWindows(desk, 2));

        // A new window of the swallowing kitty does not swallow the first one again.
        kitties.OsWindow("swallower");
        Assert.Equal(2, CountSwallowing(desk, None));
        Assert.Equal(1, CountSwallowing(desk, swallowee));
        Assert.True(HasWorkspaceWithWindows(desk, 3));

        desk.Ok("focuswindow", "class:kitty_swallowee");
        Assert.Equal("kitty_swallowee", desk.ActiveClass());
        desk.Ok("toggleswallow");
        Assert.Equal("kitty_swallower", desk.ActiveClass());
        Assert.Equal(2, CountSwallowing(desk, None));
        Assert.Equal(1, CountSwallowing(desk, swallowee));
        Assert.True(HasWorkspaceWithWindows(desk, 2));

        kitties.KillAll();
        Assert.Empty(desk.Clients());

        // Chained swallowing.
        Assert.Equal("ok", desk.Send("keyword misc:swallow_regex ^(kitty_swallowee|kitty_chain_middle|kitty_chain_removed_middle)$"));

        kitties.RemoteControl("chain_first");
        var chainFirst = ActiveAddress(desk);
        kitties.Swallower("chain_first", "chain_middle");
        var chainMiddle = ActiveAddress(desk);
        kitties.Swallower("chain_middle", "chain_last");
        Assert.Equal(1, CountSwallowing(desk, None));
        Assert.Equal(1, CountSwallowing(desk, chainFirst));
        Assert.Equal(1, CountSwallowing(desk, chainMiddle));
        Assert.Equal("kitty_chain_last", desk.ActiveClass());
        Assert.True(HasWorkspaceWithWindows(desk, 1));

        kitties.Kill("kitty_chain_last");
        Assert.Equal(2, desk.Clients().Length);
        Assert.Equal(1, CountSwallowing(desk, None));
        Assert.Equal(1, CountSwallowing(desk, chainFirst));
        Assert.Equal("kitty_chain_middle", desk.ActiveClass());
        Assert.True(HasWorkspaceWithWindows(desk, 1));

        kitties.Kill("kitty_chain_middle");
        Assert.Single(desk.Clients());
        Assert.Equal(1, CountSwallowing(desk, None));
        Assert.Equal("kitty_swallowee", desk.ActiveClass());
        Assert.True(HasWorkspaceWithWindows(desk, 1));

        kitties.KillAll();
        Assert.Empty(desk.Clients());

        kitties.RemoteControl("chain_removed_first");
        kitties.Swallower("chain_removed_first", "chain_removed_middle");
        kitties.Swallower("chain_removed_middle", "chain_removed_last");
        kitties.Kill("kitty_chain_removed_middle");
        Assert.Equal(2, desk.Clients().Length);
        Assert.Equal(2, CountSwallowing(desk, None));
        Assert.Equal("kitty_chain_removed_last", desk.ActiveClass());
        Assert.True(HasWorkspaceWithWindows(desk, 2));

        kitties.KillAll();
        Assert.Empty(desk.Clients());
        Assert.Equal("ok", desk.Send("keyword misc:swallow_regex ^(kitty_swallowee)$"));

        // Swallowing in a Group.
        Assert.Equal("ok", desk.Send("keyword group:auto_group true"));

        kitties.RemoteControl("grouped_swallowee");
        var groupedSwallowee = ActiveAddress(desk);
        desk.Ok("togglegroup");
        kitties.Plain("swallow_group_peer");
        desk.Ok("focuswindow", "class:kitty_swallowee");
        Assert.Equal(0, UngroupedCount(desk));

        kitties.Swallower("grouped_swallowee", "grouped_swallower");
        Assert.Equal(1, UngroupedCount(desk));
        Assert.Equal(1, CountSwallowing(desk, groupedSwallowee));

        // Only the internal mode follows the Group slot. Each Client keeps its own client mode.
        desk.Ok("fullscreenstate", "2 1 set");
        Assert.Equal(2, Desk.Fullscreen(desk.ActiveWindow()));
        Assert.Equal(1, Desk.FullscreenClient(desk.ActiveWindow()));

        desk.Ok("toggleswallow");
        ExpectActive(desk, "kitty_swallowee", 2, 0);
        Assert.Equal(1, CountSwallowing(desk, groupedSwallowee));
        Assert.Equal(1, desk.Clients().Count(client => Desk.FullscreenClient(client) == 1));

        desk.Ok("toggleswallow");
        ExpectActive(desk, "kitty_grouped_swallower", 2, 1);

        desk.Ok("fullscreenstate", "1 1 set");
        desk.Ok("fullscreenstate", "2 2 set");
        desk.Ok("toggleswallow");
        desk.Ok("toggleswallow");

        // The reference then checks a maximize echo inside its test plugin, which has no core form.
        desk.Ok("fullscreenstate", "2 2 set");
        desk.Ok("fullscreenstate", "0 0 set");
        ExpectActive(desk, "kitty_grouped_swallower", 0, 1);

        desk.Ok("fullscreenstate", "0 0 set");
        Assert.Equal(0, Desk.FullscreenClient(desk.ActiveWindow()));

        desk.Ok("fullscreenstate", "2 1 set");

        // Closing the swallowing Client brings the swallowed one back with the internal mode.
        kitties.Kill("kitty_grouped_swallower");
        Assert.Equal(2, desk.Clients().Length);
        Assert.Equal(0, UngroupedCount(desk));
        Assert.Equal(2, CountSwallowing(desk, None));
        ExpectActive(desk, "kitty_swallowee", 2, 0);
        Assert.Equal("default", desk.ActiveWindow().GetProperty("fullscreenHandler").GetString());

        kitties.KillAll();
        Assert.Empty(desk.Clients());

        // Closing the shown swallowed Client gives its Group slot back to the one that swallowed it.
        kitties.RemoteControl("target_first_swallowee");
        desk.Ok("togglegroup");
        kitties.Plain("target_first_group_peer");
        desk.Ok("focuswindow", "class:kitty_swallowee");
        kitties.Swallower("target_first_swallowee", "target_first_swallower");
        desk.Ok("toggleswallow");
        kitties.Kill("kitty_swallowee");
        Assert.Equal(2, desk.Clients().Length);
        Assert.Equal(0, UngroupedCount(desk));
        Assert.Equal(2, CountSwallowing(desk, None));

        kitties.KillAll();
        Assert.Empty(desk.Clients());

        // A Layout's fullscreen follows the Group slot and survives the swallowing Client closing.
        Assert.Equal("ok", desk.Send("keyword general:layout scrolling"));
        kitties.RemoteControl("layout_swallowee");
        desk.Ok("togglegroup");
        kitties.Plain("layout_swallow_group_peer");
        desk.Ok("focuswindow", "class:kitty_swallowee");
        kitties.Swallower("layout_swallowee", "layout_swallower");
        desk.Ok("fullscreenstate", "2 1 set");

        desk.Ok("toggleswallow");
        ExpectActive(desk, "kitty_swallowee", 2, 0);
        Assert.Equal("scrolling", desk.ActiveWindow().GetProperty("fullscreenHandler").GetString());

        desk.Ok("toggleswallow");
        kitties.Kill("kitty_layout_swallower");
        Assert.Equal(2, desk.Clients().Length);
        ExpectActive(desk, "kitty_swallowee", 2, 0);
        Assert.Equal("scrolling", desk.ActiveWindow().GetProperty("fullscreenHandler").GetString());

        kitties.KillAll();
        Assert.Empty(desk.Clients());
        Assert.Equal("ok", desk.Send("keyword general:layout dwindle"));

        // A locked Group stays whole and is not swallowed.
        kitties.RemoteControl("locked_swallowee");
        desk.Ok("togglegroup");
        desk.Ok("lockactivegroup", "lock");
        kitties.Swallower("locked_swallowee", "locked_swallower");
        Assert.Equal(0, SwallowingCount(desk));
        Assert.Equal(1, UngroupedCount(desk));

        Assert.Equal("ok", desk.Send("keyword group:auto_group false"));
        kitties.KillAll();
    }

    private static void ExpectActive(Desk desk, string windowClass, int fullscreen, int fullscreenClient)
    {
        var active = desk.ActiveWindow();
        Assert.Equal(windowClass, Desk.ClassOf(active));
        Assert.Equal(fullscreen, Desk.Fullscreen(active));
        Assert.Equal(fullscreenClient, Desk.FullscreenClient(active));
    }

    private static string ActiveAddress(Desk desk) => desk.ActiveWindow().GetProperty("address").GetString() ?? "";

    private static string Swallowing(JsonElement client) => client.GetProperty("swallowing").GetString() ?? "";

    private static int CountSwallowing(Desk desk, string address) =>
        desk.Clients().Count(client => Swallowing(client) == address);

    private static int SwallowingCount(Desk desk) => desk.Clients().Count(client => Swallowing(client) != None);

    // The reference counts "grouped: 0", a Client in no Group.
    private static int UngroupedCount(Desk desk) =>
        desk.Clients().Count(client => client.GetProperty("grouped").GetArrayLength() == 0);

    private static bool HasWorkspaceWithWindows(Desk desk, int windows) =>
        desk.Workspaces().Any(workspace => workspace.GetProperty("windows").GetInt32() == windows);

    /// <summary>
    /// The kitty processes the reference starts. A kitty started from another one, or a new window of one, is a
    /// window whose process descends from that kitty's process. Killing a kitty closes every window of its process.
    /// </summary>
    private sealed class Kitties(Desk desk)
    {
        // The test harness, which starts the first kitty of each chain.
        private const int Harness = 50;

        private readonly Dictionary<string, int> _processes = new(StringComparer.Ordinal);
        private readonly Dictionary<int, List<int>> _ancestors = [];
        private readonly Dictionary<int, string> _classes = [];
        private readonly List<(WindowId Window, int Process, string Class)> _windows = [];
        private int _nextProcess = 100;
        private long _nextWindow = 1_000;

        /// <summary>A kitty listening on its own socket. The reference gives every such kitty the same class.</summary>
        internal void RemoteControl(string name) => Start(name, "kitty_swallowee", [Harness]);

        /// <summary>A kitty the kitty listening as <paramref name="parent"/> starts.</summary>
        internal void Swallower(string parent, string name)
        {
            var parentProcess = _processes[parent];
            Start(name, $"kitty_{name}", [parentProcess, .. _ancestors[parentProcess]]);
        }

        /// <summary>A second window of the kitty listening as <paramref name="parent"/>.</summary>
        internal void OsWindow(string parent)
        {
            var process = _processes[parent];
            Open(process, _classes[process]);
        }

        internal void Plain(string windowClass) => Start(windowClass, windowClass, [Harness]);

        /// <summary>
        /// Kills the window's process, as the reference's <c>kill</c> dispatcher does: the core asks for the kill,
        /// and every window of that process closes.
        /// </summary>
        internal void Kill(string windowClass)
        {
            var reply = desk.Core.Execute($"dispatch killwindow class:{windowClass}");
            Assert.Equal("ok", reply.Text);
            Assert.True(reply.Effect.Kill.HasValue);
            var process = reply.Effect.Kill.Value.ProcessId;
            foreach (var window in _windows.Where(window => window.Process == process).ToList())
            {
                _windows.Remove(window);
                desk.Handle(new WindowClosed(window.Window));
            }
        }

        internal void KillAll()
        {
            foreach (var window in _windows.ToList())
                desk.Handle(new WindowClosed(window.Window));
            _windows.Clear();
        }

        private void Start(string name, string windowClass, List<int> ancestors)
        {
            var process = _nextProcess++;
            _processes[name] = process;
            _ancestors[process] = ancestors;
            _classes[process] = windowClass;
            Open(process, windowClass);
        }

        private void Open(int process, string windowClass)
        {
            var window = new WindowId(_nextWindow++);
            _windows.Add((window, process, windowClass));
            var area = desk.FocusedArea();
            desk.Handle(new WindowAppeared(window, area)
            {
                Pointer = desk.Pointer,
                Traits = WindowTraits.Normal with { Class = windowClass, Title = windowClass },
                Bounds = new Rectangle(area.X + (area.Width / 2) - 320, area.Y + (area.Height / 2) - 240, 640, 480),
                ProcessId = process,
                AncestorProcessIds = _ancestors[process],
            });
        }
    }
}
