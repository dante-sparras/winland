namespace Winland.Domain;

/// <summary>
/// What every tiling Layout keeps around its own tiling: the Clients it holds, its floating Clients, the size each
/// Client opened with, its pseudotiles and its size limits. A Layout places its tiled Clients itself.
/// </summary>
internal abstract class TiledLayout
{
    private protected readonly List<Client> _clients = [];
    private protected readonly FloatingClients _floating = new();
    private protected readonly Dictionary<ClientId, Rectangle> _natural = [];
    private protected readonly PseudoTiles _pseudo = new();
    private protected readonly Dictionary<ClientId, SizeLimits> _limits = [];

    public IReadOnlyList<Client> Clients => _clients;

    public void Retitle(WindowId window, string title)
    {
        for (var i = 0; i < _clients.Count; i++)
        {
            if (_clients[i].Window == window)
                _clients[i] = _clients[i] with { Title = title };
        }
    }

    public void Reclass(WindowId window, string windowClass)
    {
        for (var i = 0; i < _clients.Count; i++)
        {
            if (_clients[i].Window == window)
                _clients[i] = _clients[i] with { Class = windowClass };
        }
    }

    public void Readdress(WorkspaceId workspace)
    {
        for (var i = 0; i < _clients.Count; i++)
            _clients[i] = _clients[i] with { Workspace = workspace };
    }

    public void NudgeFloating(int dx, int dy) => _floating.Nudge(dx, dy);

    /// <summary>The tiled placements, which each Layout works out.</summary>
    public abstract IReadOnlyList<Placement> Placed();

    public abstract IReadOnlyList<Placement> Remove(WindowId window);

    /// <summary>The tiled placements and the shown floating Clients.</summary>
    public IReadOnlyList<Placement> Visible()
    {
        var placements = new List<Placement>(Placed());
        _floating.AddShown(placements, _clients);
        return placements;
    }

    /// <summary>Removes a Client so another Workspace can adopt it, with what it needs to take its place there.</summary>
    public bool TryTake(WindowId window, out Taken taken)
    {
        taken = default;
        var id = new ClientId(window.Value);
        var index = IndexOf(id);
        if (index < 0)
            return false;

        var client = _clients[index];
        var bounds = _floating.TryGet(id, out var floating)
            ? floating
            : _natural.TryGetValue(id, out var natural) ? natural : default;
        _limits.TryGetValue(id, out var limits);
        taken = SlotHandover.Describe(client, bounds, limits, TakenWidth(id));
        Remove(window);
        return true;
    }

    /// <summary>The width a Client takes with it to another Workspace, for a Layout that has one.</summary>
    private protected virtual double? TakenWidth(ClientId id) => null;

    private protected int IndexOf(ClientId id) => _clients.FindIndex(client => client.Id == id);

    private protected bool Minimized(ClientId id)
    {
        var index = IndexOf(id);
        return index >= 0 && _clients[index].Minimized;
    }

    private protected void SetMinimized(ClientId id, bool minimized)
    {
        var index = IndexOf(id);
        if (index >= 0)
            _clients[index] = _clients[index] with { Minimized = minimized };
    }

    private protected void SetFloating(ClientId id, bool floating)
    {
        var index = IndexOf(id);
        if (index >= 0)
            _clients[index] = _clients[index] with { Floating = floating };
    }
}
