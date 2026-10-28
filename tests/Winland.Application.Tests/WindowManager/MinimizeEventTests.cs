namespace Winland.Application.Tests;

/// <summary>
/// The <c>minimized</c> events. On Windows the taskbar minimizes and restores a window itself, and Winland hears it.
/// </summary>
public class MinimizeEventTests
{
    [Fact]
    public void MinimizeAndRestoreAreMinimizedEvents()
    {
        var desk = new TestDesktop();
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
