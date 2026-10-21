namespace Winland;

/// <summary>
/// A black layer over the whole of <paramref name="Monitor"/>, in screen pixels, kept just below
/// <paramref name="Window"/>, so everything under that window is dimmed by <paramref name="Alpha"/>.
/// </summary>
public sealed record DimAround(WindowId Window, Rectangle Monitor, double Alpha);

public sealed partial class Core
{
    // The dim_around property draws a black layer over the window's Monitor just below it, at decoration:dim_around times
    // the window's opacity, as Winland renders it. A window that is not shown dims nothing.
    private List<DimAround> DimsAround(State state, IReadOnlyList<Decoration> decorations)
    {
        var dims = new List<DimAround>();
        if (_decoration.DimAround <= 0)
            return dims;

        var alphas = new Dictionary<ClientId, byte>(decorations.Count);
        foreach (var decoration in decorations)
            alphas[decoration.Client] = decoration.Alpha;

        foreach (var desk in _workspaces)
        {
            if (!IsShown(desk.Id) || MonitorById(desk.Monitor) is not LiveMonitor screen)
                continue;

            HashSet<WindowId>? showing = null;
            foreach (var client in state.Clients)
            {
                if (client.Workspace != desk.Id || client.Minimized || OwnProp(client.Window, "dim_around") is not true)
                    continue;

                showing ??= desk.Showing();
                if (!showing.Contains(client.Window))
                    continue;

                var monitor = screen.Effective;
                var opacity = alphas.TryGetValue(client.Id, out var alpha) ? alpha / 255.0 : 1;
                dims.Add(new DimAround(client.Window, new Rectangle(monitor.X, monitor.Y, monitor.Width, monitor.Height), _decoration.DimAround * opacity));
            }
        }

        return dims;
    }
}
