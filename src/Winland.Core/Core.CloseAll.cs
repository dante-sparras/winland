namespace Winland;

public sealed partial class Core
{
    // The same request closewindow makes, so the application can still ask to save, for every Client.
    // A Client no_close_for still protects is left open. Workspace 1 of the focused Monitor is what shows
    // afterwards, which closes a Special workspace open on that Monitor.
    private Outcome CloseAll()
    {
        const string name = "closeall";
        var closes = new List<WindowId>();
        foreach (var client in EveryClient())
        {
            if (Closeable(client.Window))
                closes.Add(client.Window);
        }

        var monitor = FocusedId();
        if (SpecialOn(monitor) is null)
        {
            return SwitchTo(WorkspaceId.FromNumber(1), focus: null, consumed: false, dispatcher: name, pull: true)
                with { Closes = closes };
        }

        // Closing it first shows the Monitor's Workspace. Switching with it still open would leave it on top.
        var concealed = CloseSpecial(monitor, name);
        var shown = SwitchTo(WorkspaceId.FromNumber(1), focus: null, consumed: false, dispatcher: name, pull: true);
        return Merge(concealed, shown) with { Closes = closes, Dispatcher = name, Focus = _active };
    }
}
