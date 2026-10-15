namespace Winland;

internal readonly record struct MinimizedPresence(WindowId Window, bool WorkspaceShown, bool Minimized);

internal readonly record struct MinimizedPlan(
    IReadOnlyList<WindowId> Hide,
    IReadOnlyList<WindowId> Show,
    IReadOnlyList<WindowId> Concealed);

internal static class MinimizedConcealment
{
    internal static MinimizedPlan Plan(IReadOnlyList<MinimizedPresence> clients, IReadOnlyCollection<WindowId> concealed)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(concealed);

        var held = new HashSet<WindowId>(concealed);
        var hide = new List<WindowId>();
        var show = new List<WindowId>();
        var next = new List<WindowId>();
        foreach (var client in clients)
        {
            if (!client.Minimized)
                continue;

            if (!client.WorkspaceShown)
            {
                next.Add(client.Window);
                if (!held.Contains(client.Window))
                    hide.Add(client.Window);
                continue;
            }

            if (held.Contains(client.Window))
                show.Add(client.Window);
        }

        return new MinimizedPlan(hide, show, next);
    }
}
