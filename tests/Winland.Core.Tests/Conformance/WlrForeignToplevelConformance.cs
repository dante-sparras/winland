using Winland.Core;

namespace Winland.Core.Tests.Conformance;

/// <summary>
/// Winland's foreign toplevel case. A taskbar asks to minimize a window and to restore it, and a handler
/// records each minimize event. On Windows the taskbar does that itself, and Winland hears the window minimized and
/// restored. The reference records each minimize. Here the stream's <c>minimized</c> events do.
/// </summary>
[Trait("Category", "Conformance")]
public class WlrForeignToplevelConformance
{
    [Fact]
    public void WlrForeignToplevelMinimizeEvents()
    {
        var desk = new Desk();
        var events = new List<string>();
        var kitty = desk.Spawn("hyprtester-wlr-minimize");
        var pid = desk.Client("hyprtester-wlr-minimize").GetProperty("pid").GetInt64();

        // The reference checks that opening the window records no event. Spawn does not return the open's events.
        Record(events, desk.Handle(new WindowMinimized(kitty)));
        Assert.Equal($"{pid}:true", string.Join(',', events));

        Record(events, desk.Handle(new WindowRestored(kitty)));
        Assert.Equal($"{pid}:true,{pid}:false", string.Join(',', events));
    }

    // Writes each minimize event as the reference handler does: the window's pid, a colon and the state.
    private static void Record(List<string> events, Outcome outcome)
    {
        foreach (var minimized in outcome.Events.OfType<ClientMinimized>())
        {
            var pid = outcome.State.Clients.Concat(outcome.State.Hidden).First(client => client.Window == minimized.Window).ProcessId;
            events.Add($"{pid}:{(minimized.Minimized ? "true" : "false")}");
        }
    }
}
