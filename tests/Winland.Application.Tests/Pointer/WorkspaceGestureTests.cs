namespace Winland.Application.Tests.Conformance;

/// <summary>
/// Workspace swipe cases. One swipe is 300 pixels along one axis in a single update.
/// </summary>
[Trait("Category", "Conformance")]
public class GesturesConformance
{
    private const string ConfigGestures = """
        gesture = 3, horizontal, workspace
        """;

    private static string Config(string extra = "") =>
        KeybindsConformance.ConfigBinds + "\n" + ConfigGestures + "\n" + extra;

    [Fact]
    public void Gestures()
    {
        var desk = new Desk(Config());
        var touchpad = new Touchpad(desk);
        desk.Spawn("a");
        Assert.Single(desk.Clients());


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

        // workspace previous after a workspace gesture.
        desk.Keyword("gestures:workspace_swipe_invert", "0");
        desk.Ok("workspace", "3");

        // Workspace 5 is entered from 3, and remembers it.
        desk.Ok("workspace", "5");
        desk.Spawn("a");

        // A swipe from 1 to 5 makes 5 remember 1 instead.
        desk.Ok("workspace", "1");
        touchpad.Swipe("right", 3);
        Assert.Equal(5, desk.ActiveWorkspace().GetProperty("id").GetInt32());

        desk.Ok("workspace", "previous");
        Assert.Equal(1, desk.ActiveWorkspace().GetProperty("id").GetInt32());

        desk.Ok("workspace", "previous");
        Assert.Equal(5, desk.ActiveWorkspace().GetProperty("id").GetInt32());

        desk.Ok("workspace", "1");
    }

    private static bool HasWorkspace(Desk desk, int id) =>
        desk.Workspaces().Any(workspace => workspace.GetProperty("id").GetInt32() == id && workspace.GetProperty("name").GetString() == $"{id}");

    /// <summary>One swipe of 300 pixels along an axis, in a single update.</summary>
    private sealed class Touchpad(Desk desk)
    {
        internal void Swipe(string direction, int fingers)
        {
            var (x, y) = direction switch
            {
                "down" => (0, 300),
                "up" => (0, -300),
                "left" => (-300, 0),
                _ => (300, 0),
            };

            _ = desk.Handle(new SwipeBegan(fingers));
            _ = desk.Handle(new SwipeUpdated(fingers, x, y));
            _ = desk.Handle(new SwipeEnded());
        }
    }
}
