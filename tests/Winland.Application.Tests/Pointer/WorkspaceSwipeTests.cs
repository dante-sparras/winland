using System.Text.Json;

namespace Winland.Application.Tests;

/// <summary>
/// A nine-finger horizontal workspace swipe, step by step, and a swipe ending as cancelled. The fixture holds three
/// empty persistent Workspaces on the focused Monitor and shows the middle one, and holds the Workspace shown before
/// it. A test checks what the replies show: which Workspaces exist, which one each Monitor shows, and on which
/// Monitor each is.
/// </summary>
public class WorkspaceSwipeTests
{
    // The fixture's Workspaces are rendered before the swipe starts, which has no core form, so these parts run as
    // the ones without it.
    [Fact]
    public void WorkspaceSwipePreexistingForce()
    {
        var swipe = Fixture.Run();
        swipe.Begin();
        swipe.Update(-90);
        swipe.Preview(swipe.Left);
        swipe.Baseline(swipe.Right, swipe.R);
        swipe.Update(180);
        swipe.Preview(swipe.Right);
        swipe.Baseline(swipe.Left, swipe.L);
        swipe.Cancel();
        swipe.Cleaned();
        swipe.End(false);
        swipe.Cleaned();

        foreach (var distance in new[] { 90, 180 })
        {
            swipe = Fixture.Run();
            swipe.Begin();
            swipe.Update(distance);
            swipe.Preview(swipe.Right);
            swipe.End(false);
            swipe.Neutral(swipe.Right);
            swipe.Baseline(swipe.Left, swipe.L);
        }
    }

    private const string OriginOutput = "HYPRTEST-SWIPE-A";

    private const string PeerOutput = "HYPRTEST-SWIPE-B";

    /// <summary>What the replies show of a Workspace: whether it exists, whether its Monitor shows it, and which Monitor.</summary>
    private readonly record struct Shot(bool Exists, bool Active, string Monitor);

    /// <summary>The reference's swipe fixture and the checks it makes on it.</summary>
    private sealed class Fixture
    {
        private const string Options = """
            gestures {
                workspace_swipe_distance = 300
                workspace_swipe_invert = false
            }
            gesture = 9, horizontal, workspace

            """;

        private Fixture(TestDesktop desk, string left, string origin, string right)
        {
            TestDesktop = desk;
            Left = left;
            Origin = origin;
            Right = right;
        }

        internal TestDesktop TestDesktop { get; }

        internal string Left { get; }

        internal string Origin { get; set; }

        internal string Right { get; }

        internal Shot L { get; private set; }

        internal Shot O { get; set; }

        internal Shot R { get; private set; }

        /// <summary>The reference's run: the swipe options and gesture, then the fixture.</summary>
        internal static Fixture Run()
        {
            var swipe = new Fixture(new TestDesktop(Options + Hold("1", "9901", "9902", "9903")), "9901", "9902", "9903");
            swipe.Prepare();
            return swipe;
        }

        /// <summary>
        /// The reference's run on two new outputs side by side, with the fixture on the first and Workspace 9910
        /// shown on the second. The first output shows Workspace 2 before the fixture, and the plugin holds it.
        /// </summary>
        internal static Fixture RunOnOutputs()
        {
            var desk = new TestDesktop(Options + $"""
                monitor = {OriginOutput}, 1920x1080@60, 20000x0, 1
                monitor = {PeerOutput}, 1920x1080@60, 21920x0, 1
                workspace = 2, monitor:{OriginOutput}, default:true, persistent:true
                workspace = 9901, monitor:{OriginOutput}, persistent:true
                workspace = 9902, monitor:{OriginOutput}, persistent:true
                workspace = 9903, monitor:{OriginOutput}, persistent:true
                workspace = 9910, monitor:{PeerOutput}, default:true, persistent:true

                """);
            desk.AddMonitor(OriginOutput, new Rectangle(20000, 0, 1920, 1080));
            desk.AddMonitor(PeerOutput, new Rectangle(21920, 0, 1920, 1080));
            desk.Ok("focusmonitor", OriginOutput);

            var swipe = new Fixture(desk, "9901", "9902", "9903");
            swipe.Prepare();
            return swipe;
        }

        private static string Hold(params string[] workspaces) =>
            string.Concat(workspaces.Select(workspace => $"workspace = {workspace}, persistent:true\n"));

        internal void Begin() => TestDesktop.Handle(new SwipeBegan(9));

        internal void Update(double delta) => TestDesktop.Handle(new SwipeUpdated(9, delta, 0));

        internal void End(bool cancelled) => TestDesktop.Handle(new SwipeEnded(cancelled));

        /// <summary>The test plugin's cancel. The core's form of it is the swipe ending as cancelled.</summary>
        internal void Cancel() => TestDesktop.Handle(new SwipeEnded(Cancelled: true));

        internal Shot Snapshot(string name)
        {
            var workspace = TestDesktop.Workspaces().FirstOrDefault(each => each.GetProperty("name").GetString() == name);
            if (workspace.ValueKind == JsonValueKind.Undefined)
                return new Shot(false, false, "");

            var monitor = workspace.GetProperty("monitor").GetString() ?? "";
            using var monitors = JsonDocument.Parse(TestDesktop.Send("j/monitors"));
            var active = monitors.RootElement.EnumerateArray().Any(each =>
                each.GetProperty("name").GetString() == monitor
                && each.GetProperty("activeWorkspace").GetProperty("name").GetString() == name);
            return new Shot(true, active, monitor);
        }

        /// <summary>The Workspace is the one shown, on the origin's Monitor, with no swipe left over.</summary>
        internal void Neutral(string name) => Assert.Equal(new Shot(true, true, O.Monitor), Snapshot(name));

        internal void Baseline(string name, Shot expected) => Assert.Equal(expected, Snapshot(name));

        /// <summary>The swipe is heading for <paramref name="name"/> and has not switched: the origin is still shown.</summary>
        internal void Preview(string name)
        {
            Assert.False(Snapshot(name).Active);
            Assert.True(Snapshot(Origin).Active);
        }

        internal void Cleaned()
        {
            Baseline(Left, L);
            Baseline(Right, R);
            Neutral(Origin);
        }

        private void Prepare()
        {
            TestDesktop.Ok("workspace", Origin);
            L = Snapshot(Left);
            O = Snapshot(Origin);
            R = Snapshot(Right);
            Assert.True(L.Exists);
            Assert.True(R.Exists);
            Assert.False(L.Active);
            Assert.False(R.Active);
            Neutral(Origin);
        }
    }
}
