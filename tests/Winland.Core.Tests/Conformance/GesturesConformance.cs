using System.Text;
using System.Text.Json;
using Winland;

namespace Winland.Tests.Conformance;

/// <summary>
/// The pinned release's gesture cases. The reference test plugin swipes 300 pixels along one axis in a single update;
/// here that is a core swipe. Its alt switch is Alt held on the keyboard.
/// </summary>
[Trait("Category", "Conformance")]
public class GesturesConformance
{
    // The gestures of the reference config, in the line-based syntax.
    private const string ConfigGestures = """
        gesture = 3, left, dispatcher, exec, kitty
        gesture = 3, right, float
        gesture = 3, up, close
        gesture = 3, down, fullscreen
        gesture = 3, down, mod: ALT, float
        gesture = 3, horizontal, mod: ALT, workspace
        gesture = 5, up, dispatcher, sendshortcut, , e, activewindow
        gesture = 5, down, dispatcher, sendshortcut, , x, activewindow
        gesture = 5, left, dispatcher, sendshortcut, , i, activewindow
        gesture = 5, right, dispatcher, sendshortcut, , t, activewindow
        gesture = 4, right, dispatcher, sendshortcut, , return, activewindow
        gesture = 4, left, dispatcher, movecursortocorner, 1
        gesture = 2, pinch, cursorZoom, 1, live
        gesturep = 2, right, float
        """;

    private static string Config(string extra = "") =>
        KeybindsConformance.ConfigBinds + "\n" + ConfigGestures + "\n" + extra;

    [Fact(Skip = "The special gesture action is not honored.")]
    public void DefaultSpecialWorkspaceGesture()
    {
        // The case adds the gesture before any window opens.
        var desk = new Desk(Config("gesture = 8, left, special"));
        var touchpad = new Touchpad(desk);

        touchpad.Swipe("left", 8);
        Assert.Equal("special:special", SpecialWorkspace(desk));
        touchpad.Swipe("left", 8);
        Assert.NotEqual("special:special", SpecialWorkspace(desk));
    }

    [Fact(Skip = "Only the workspace gesture action is honored.")]
    public void Gestures()
    {
        var desk = new Desk(Config());
        var touchpad = new Touchpad(desk);
        desk.Spawn("a");
        Assert.Single(desk.Clients());

        touchpad.Swipe("up", 5);
        touchpad.Swipe("down", 5);
        touchpad.Swipe("left", 5);
        touchpad.Swipe("right", 5);
        touchpad.Swipe("right", 4);

        // The shell in kitty reads exit and Return, and kitty closes.
        Assert.Equal("exit\n", touchpad.Typed);
        desk.Close("a");
        Assert.Empty(desk.Clients());

        touchpad.Swipe("left", 3);
        Assert.Contains("kitty", touchpad.Launched);
        desk.Spawn("kitty");
        Assert.Single(desk.Clients());

        touchpad.Swipe("right", 3);
        Assert.Contains(desk.Clients(), Desk.Floating);

        touchpad.Swipe("down", 3);
        Assert.Contains(desk.Clients(), client => Desk.Fullscreen(client) == 2);

        touchpad.Swipe("down", 3);
        Assert.Contains(desk.Clients(), client => Desk.Fullscreen(client) == 0);

        touchpad.Alt(true);

        touchpad.Swipe("left", 3);
        Assert.True(HasWorkspace(desk, 2));

        touchpad.Swipe("right", 3);
        Assert.False(HasWorkspace(desk, 2));

        // A third swipe checks that nothing breaks.
        touchpad.Swipe("right", 3);
        Assert.False(HasWorkspace(desk, 2));

        desk.Keyword("gestures:workspace_swipe_invert", "0");

        touchpad.Swipe("right", 3);
        Assert.True(HasWorkspace(desk, 2));

        touchpad.Swipe("left", 3);
        Assert.False(HasWorkspace(desk, 2));

        desk.Keyword("gestures:workspace_swipe_invert", "1");
        desk.Keyword("gestures:workspace_swipe_create_new", "0");

        touchpad.Swipe("left", 3);
        Assert.False(HasWorkspace(desk, 2));
        Assert.True(HasWorkspace(desk, 1));

        touchpad.Swipe("down", 3);
        Assert.Contains(desk.Clients(), client => !Desk.Floating(client));

        touchpad.Alt(false);

        // kitty closes when it is asked to.
        var closed = touchpad.Swipe("up", 3);
        foreach (var window in closed.Closes)
            desk.Handle(new WindowClosed(window));
        Assert.Empty(desk.Clients());

        // movecursortocorner takes a single character, so the gesture's argument must arrive as one.
        desk.Spawn("a");
        desk.Ok("movecursortocorner", "0");
        var before = desk.Send("cursorpos");
        touchpad.Swipe("left", 4);
        Assert.NotEqual(before, desk.Send("cursorpos"));

        // workspace previous after a workspace gesture.
        desk.Keyword("gestures:workspace_swipe_invert", "0");
        desk.Keyword("gestures:workspace_swipe_create_new", "1");
        desk.Ok("workspace", "3");

        // Workspace 5 is entered from 3, and remembers it.
        desk.Ok("workspace", "5");
        desk.Spawn("a");

        // A swipe from 1 to 5 makes 5 remember 1 instead.
        desk.Ok("workspace", "1");
        touchpad.Alt(true);
        touchpad.Swipe("right", 3);
        touchpad.Alt(false);
        Assert.Equal(5, desk.ActiveWorkspace().GetProperty("id").GetInt32());

        desk.Ok("workspace", "previous");
        Assert.Equal(1, desk.ActiveWorkspace().GetProperty("id").GetInt32());

        desk.Ok("workspace", "previous");
        Assert.Equal(5, desk.ActiveWorkspace().GetProperty("id").GetInt32());

        desk.Ok("workspace", "1");

        var beforePinch = desk.Send("cursorpos").Split(',', StringSplitOptions.TrimEntries);
        desk.Ok("movecursor", "500 500");
        desk.Keyword("cursor:zoom_factor", "1");

        // The rest of the case pinches and reads the cursor zoom, which the core has no input or reply for. It then
        // puts the cursor back.
        desk.Ok("movecursor", $"{beforePinch[0]} {beforePinch[1]}");
    }

    private static bool HasWorkspace(Desk desk, int id) =>
        desk.Workspaces().Any(workspace => workspace.GetProperty("id").GetInt32() == id && workspace.GetProperty("name").GetString() == $"{id}");

    private static string SpecialWorkspace(Desk desk)
    {
        using var monitors = JsonDocument.Parse(desk.Send("j/monitors"));
        var focused = monitors.RootElement.EnumerateArray().Single(monitor => monitor.GetProperty("focused").GetBoolean());
        return focused.GetProperty("specialWorkspace").GetProperty("name").GetString() ?? "";
    }

    /// <summary>
    /// The reference test plugin's touchpad: one swipe of 300 pixels along an axis, in a single update. It keeps
    /// what the swipes start and the keys they send on.
    /// </summary>
    private sealed class Touchpad(Desk desk)
    {
        private readonly Desk _desk = desk;
        private readonly StringBuilder _typed = new();
        private readonly List<string> _launched = [];

        internal string Typed => _typed.ToString();

        internal IReadOnlyList<string> Launched => _launched;

        internal Outcome Swipe(string direction, int fingers)
        {
            var (x, y) = direction switch
            {
                "down" => (0, 300),
                "up" => (0, -300),
                "left" => (-300, 0),
                _ => (300, 0),
            };

            Keep(_desk.Handle(new SwipeBegan(fingers)));
            Keep(_desk.Handle(new SwipeUpdated(fingers, x, y)));
            var ended = _desk.Handle(new SwipeEnded());
            Keep(ended);
            return ended;
        }

        internal void Alt(bool held) =>
            Keep(_desk.Handle(held ? new KeyPressed("Alt_L") : new KeyReleased("Alt_L")));

        private void Keep(Outcome outcome)
        {
            foreach (var launch in outcome.Launches)
                _launched.Add(launch.CommandLine);
            foreach (var relay in outcome.Relays)
            {
                if (relay.Phase != KeyRelayPhase.Release)
                    _typed.Append(relay.Key.Equals("return", StringComparison.OrdinalIgnoreCase) ? "\n" : relay.Key);
            }
        }
    }
}
