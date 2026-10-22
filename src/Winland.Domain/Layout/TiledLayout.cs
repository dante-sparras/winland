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

        taken = Describe(index);
        Remove(window);
        return true;
    }

    /// <summary>
    /// Hands the slot of <paramref name="current"/> to <paramref name="next"/>, a Window this Layout does not hold. The
    /// slot keeps its place, size and floating state, and each Layout renames the Client in its own structure.
    /// </summary>
    public bool Substitute(WindowId current, WindowId next, Taken incoming, out Taken outgoing)
    {
        outgoing = default;
        var from = new ClientId(current.Value);
        var to = new ClientId(next.Value);
        var index = IndexOf(from);
        if (index < 0 || from == to || IndexOf(to) >= 0)
            return false;

        outgoing = Describe(index);
        _clients[index] = SlotHandover.Rename(_clients[index], next, incoming);
        Rename(from, to);
        _floating.Hand(from, to);
        _pseudo.Hand(from, to);
        _natural.Remove(from);
        _natural[to] = incoming.Bounds;
        _limits.Remove(from);
        _limits[to] = new SizeLimits(incoming.MinSize, incoming.MaxSize) { Tiled = incoming.TiledLimits };
        return true;
    }

    /// <summary>Gives the tile of <paramref name="from"/> to <paramref name="to"/> in what the Layout keeps for its own tiling.</summary>
    private protected abstract void Rename(ClientId from, ClientId to);

    private Taken Describe(int index)
    {
        var client = _clients[index];
        var bounds = _floating.TryGet(client.Id, out var floating)
            ? floating
            : _natural.TryGetValue(client.Id, out var natural) ? natural : default;
        _limits.TryGetValue(client.Id, out var limits);
        return SlotHandover.Describe(client, bounds, limits, TakenWidth(client.Id));
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
