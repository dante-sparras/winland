using System.Text.Json;
using Winland;

namespace Winland.Tests.Conformance;

/// <summary>
/// Winland's special workspace swipe cases. Each test is named after its case, and sends the same swipes
/// in the same order. The reference drives a special workspace gesture object through its test plugin; here that is
/// a three-finger horizontal <c>special</c> gesture, and each step is a core swipe event. The plugin's fixture makes
/// the special workspace on the focused Monitor and shows it when the case asks; here the fixture shows it with
/// <c>togglespecialworkspace</c>, and starts from a desk that shows none, as the plugin's reset leaves it. The
/// reference also reads the special workspace's alpha, offset and render hold, the Monitor's fade, dim and blur,
/// and the gesture's session, through its test plugin, and can hold the special workspace's rendering before the
/// swipe. None of those has a core form, so a test checks what the replies show: whether the special workspace
/// exists and whether its Monitor shows it.
/// </summary>
[Trait("Category", "Conformance")]
public class SpecialWorkspaceSwipeConformance
{
    private const string Name = "hyprtester-special-swipe";

    private const string Replacement = "hyprtester-special-replacement";

    private const string OriginOutput = "HYPRTEST-SPECIAL-A";

    private const string PeerOutput = "HYPRTEST-SPECIAL-B";

    [Fact]
    public void SpecialWorkspaceSwipeThresholds()
    {
        foreach (var animations in new[] { false, true })
        {
            foreach (var opened in new[] { false, true })
            {
                foreach (var cancelled in new[] { false, true })
                {
                    foreach (var distance in new[] { 44.99, 45, 150 })
                    {
                        var swipe = Fixture.Run(animations, opened);
                        swipe.Begin();

                        // The updates add up: the first one is not a whole swipe.
                        swipe.Update(distance / 2);
                        swipe.Update(distance / 2);
                        swipe.Preview();
                        swipe.End(cancelled);
                        var open = distance >= 45 ? !opened : opened;
                        swipe.Finished(open);

                        // Late events change nothing.
                        swipe.Update(150);
                        swipe.End(!cancelled);
                        swipe.Finished(open);
                    }
                }
            }
        }
    }

    [Fact]
    public void SpecialWorkspaceSwipeReplacementInvalidation()
    {
        foreach (var animations in new[] { false, true })
        {
            foreach (var opened in new[] { false, true })
            {
                var swipe = Fixture.Run(animations, opened);
                swipe.Begin();
                swipe.Update(75);
                swipe.Preview();

                // Another special workspace replaces the one being swiped, and late events change nothing.
                swipe.Desk.Ok("togglespecialworkspace", Replacement);
                Assert.True(swipe.Shows(Replacement));
                swipe.Update(150);
                swipe.End(false);
                swipe.End(true);
                Assert.True(swipe.Shows(Replacement));
                Assert.False(swipe.Shows(Name));
            }
        }
    }

    [Fact]
    public void SpecialWorkspaceSwipeMoveInvalidation()
    {
        foreach (var animations in new[] { false, true })
        {
            foreach (var opened in new[] { false, true })
            {
                var swipe = Fixture.RunOnOutputs(animations, opened);
                swipe.Begin();
                swipe.Update(75);
                swipe.Preview();

                // The second output shows a special workspace of its own, and then the one being swiped.
                swipe.Desk.Ok("focusmonitor", PeerOutput);
                swipe.Desk.Ok("togglespecialworkspace", Replacement);
                swipe.Desk.Ok("togglespecialworkspace", Name);
                Assert.True(swipe.Shows(Name));
                Assert.Equal(PeerOutput, swipe.MonitorShowing(Name));
                swipe.Update(150);
                swipe.End(false);
                swipe.End(true);
                Assert.True(swipe.Shows(Name));
            }
        }
    }

    [Fact]
    public void SpecialWorkspaceSwipeOutputRemoval()
    {
        foreach (var animations in new[] { false, true })
        {
            foreach (var opened in new[] { false, true })
            {
                var swipe = Fixture.RunOnOutputs(animations, opened);
                swipe.Begin();
                swipe.Update(75);
                swipe.Preview();

                // The reference then checks, through its plugin, that the swipe lets go as the output goes.
                swipe.Desk.RemoveMonitor(OriginOutput);
            }
        }
    }

    [Fact]
    public void SpecialWorkspaceSwipeEmptyWorkspaceLifetime()
    {
        foreach (var animations in new[] { false, true })
        {
            foreach (var opened in new[] { false, true })
            {
                var distance = opened ? 150 : 20;
                var swipe = Fixture.Run(animations, opened);
                swipe.Begin();
                swipe.Update(distance);
                swipe.Preview();

                // The swipe keeps its empty special workspace while it lasts.
                Assert.True(swipe.Exists(Name));
                swipe.End(false);

                // Closed and empty, the special workspace goes, and late events do not bring it back.
                Assert.False(swipe.Exists(Name));
                swipe.Update(150);
                swipe.End(true);
                Assert.False(swipe.Exists(Name));
            }
        }
    }

    /// <summary>The reference's special swipe fixture and the checks it makes on it.</summary>
    private sealed class Fixture(Desk desk)
    {
        internal Desk Desk { get; } = desk;

        internal static Fixture Run(bool animations, bool opened)
        {
            var swipe = new Fixture(new Desk(Options(animations)));
            swipe.Prepare(opened);
            return swipe;
        }

        /// <summary>The reference's run on two new outputs side by side, with the first focused.</summary>
        internal static Fixture RunOnOutputs(bool animations, bool opened)
        {
            var desk = new Desk(Options(animations) + $"""
                monitor = {OriginOutput}, 1920x1080@60, 24000x0, 1
                monitor = {PeerOutput}, 1920x1080@60, 25920x0, 1

                """);
            desk.AddMonitor(OriginOutput, new Rectangle(24000, 0, 1920, 1080));
            desk.AddMonitor(PeerOutput, new Rectangle(25920, 0, 1920, 1080));
            desk.Ok("focusmonitor", OriginOutput);
            var swipe = new Fixture(desk);
            swipe.Prepare(opened);
            return swipe;
        }

        private static string Options(bool animations) => $"""
            animations:enabled = {(animations ? "true" : "false")}
            decoration:dim_special = 0.4
            animation = specialWorkspace, 1, 1, default, slidefade
            animation = specialWorkspaceIn, 1, 1, default, slidefade
            animation = specialWorkspaceOut, 1, 1, default, slidefade
            gesture = 3, horizontal, special, {Name}

            """;

        internal void Begin() => Desk.Handle(new SwipeBegan(3));

        internal void Update(double delta) => Desk.Handle(new SwipeUpdated(3, delta, 0));

        internal void End(bool cancelled) => Desk.Handle(new SwipeEnded(cancelled));

        internal bool Exists(string name) =>
            Desk.Workspaces().Any(workspace => workspace.GetProperty("name").GetString() == $"special:{name}");

        /// <summary>The Monitor that shows the special workspace, or empty when none does.</summary>
        internal string MonitorShowing(string name)
        {
            using var monitors = JsonDocument.Parse(Desk.Send("j/monitors"));
            foreach (var monitor in monitors.RootElement.EnumerateArray())
            {
                if (monitor.GetProperty("specialWorkspace").GetProperty("name").GetString() == $"special:{name}")
                    return monitor.GetProperty("name").GetString() ?? "";
            }

            return "";
        }

        internal bool Shows(string name) => MonitorShowing(name).Length > 0;

        /// <summary>The swipe has the special workspace shown while the fingers move.</summary>
        internal void Preview()
        {
            Assert.True(Exists(Name));
            Assert.True(Shows(Name));
        }

        internal void Finished(bool opened) => Assert.Equal(opened, Shows(Name));

        private void Prepare(bool opened)
        {
            if (opened)
                Desk.Ok("togglespecialworkspace", Name);
        }
    }
}
