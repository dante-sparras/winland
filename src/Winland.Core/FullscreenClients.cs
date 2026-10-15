namespace Winland;

internal static class FullscreenClients
{
    internal static bool Assign(List<Client> clients, WindowId window, FullscreenMode mode, FullscreenMode clientMode)
    {
        var index = clients.FindIndex(client => client.Window == window);
        if (index < 0)
            return false;

        if (mode != FullscreenMode.None)
        {
            for (var i = 0; i < clients.Count; i++)
            {
                if (i == index || clients[i].Fullscreen == FullscreenMode.None)
                    continue;

                clients[i] = clients[i] with
                {
                    Fullscreen = FullscreenMode.None,
                    FullscreenClient = FullscreenMode.None,
                };
            }
        }

        clients[index] = clients[index] with
        {
            Fullscreen = mode,
            FullscreenClient = clientMode,
        };
        return true;
    }

    internal static bool Covering(IReadOnlyList<Client> clients)
    {
        foreach (var client in clients)
        {
            if (client.Fullscreen != FullscreenMode.None)
                return true;
        }

        return false;
    }
}
