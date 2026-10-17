namespace Winland;

/// <summary>
/// A Workspace the core owns, with the Layout that places its Clients. <see cref="Workspace"/> is the
/// snapshot of it that <see cref="State"/> reports.
/// </summary>
/// <remarks>
/// A Workspace keeps its own id. The Monitor it belongs to can change when a display is unplugged.
/// </remarks>
internal sealed partial class LiveWorkspace(WorkspaceId id, MonitorId monitor, Layout layout)
{
    public WorkspaceId Id { get; private set; } = id;

    /// <summary>Takes a new id, with every Client and waiting Group member, as <c>changeworkspaceid</c> asks.</summary>
    public void Readdress(WorkspaceId id)
    {
        Id = id;
        Layout.Readdress(id);
        foreach (var (window, waiting) in _waiting.ToArray())
            _waiting[window] = waiting with { Client = waiting.Client with { Workspace = id } };
    }

    public MonitorId Monitor { get; set; } = monitor;

    public Layout Layout { get; private set; } = layout;

    public void Replace(Layout layout) => Layout = layout;

    public WindowId? Focus { get; set; }

    public bool Persistent { get; set; }

    public string? DisplayName { get; set; }

    /// <summary><c>renameworkspace</c> set <see cref="DisplayName"/>, so a rule's <c>defaultName</c> no longer does.</summary>
    public bool Renamed { get; set; }

    public LayoutOptions Options { get; set; } = LayoutOptions.Unconfigured;

    public Client? ClientOf(WindowId window)
    {
        foreach (var client in Layout.Clients)
        {
            if (client.Window == window)
                return client;
        }

        return WaitingOf(window);
    }

    /// <summary>The internal mode of the Client covering this Workspace, which an <c>f[]</c> workspace selector reads.</summary>
    public FullscreenMode CoveringMode()
    {
        foreach (var client in Layout.Clients)
        {
            if (client.Fullscreen != FullscreenMode.None)
                return client.Fullscreen;
        }

        return FullscreenMode.None;
    }

    /// <summary>
    /// The Clients a <c>w[]</c> workspace selector counts, as the pinned release does. A Group counts by its
    /// first member's pin and its shown member's tile and visibility. A waiting member is not a Client
    /// of the Layout, so a Group is one Client without <see cref="WindowCount.Groups"/>. Under a covering
    /// Client only that Client and pinned ones are visible.
    /// </summary>
    public int CountWindows(WindowCount wanted, IReadOnlySet<WindowId> pinned)
    {
        var showing = wanted.Visible ? Showing() : null;
        WindowId? covering = null;
        foreach (var client in Layout.Clients)
        {
            if (client.Fullscreen != FullscreenMode.None)
            {
                covering = client.Window;
                break;
            }
        }

        var count = 0;
        if (wanted.Groups)
        {
            foreach (var group in _groups)
            {
                if (ClientOf(group.Shown) is Client shown && Counts(shown, group.Members[0]))
                    count++;
            }

            return count;
        }

        foreach (var client in Layout.Clients)
        {
            if (Counts(client, client.Window))
                count++;
        }

        return count;

        bool Counts(Client client, WindowId pin)
        {
            if (wanted.Tiled is bool tiled && client.Floating == tiled)
                return false;

            if (wanted.Pinned && !pinned.Contains(pin))
                return false;

            if (showing is null)
                return true;

            return !client.Minimized
                && showing.Contains(client.Window)
                && (covering is null || covering == client.Window || pinned.Contains(client.Window));
        }
    }

    // A Layout may omit tiled Clients from Visible when it is only showing one of them.
    public HashSet<WindowId> Showing()
    {
        var ids = new Dictionary<ClientId, WindowId>(Layout.Clients.Count);
        foreach (var client in Layout.Clients)
            ids[client.Id] = client.Window;

        var showing = new HashSet<WindowId>();
        foreach (var placement in Layout.Visible())
        {
            if (ids.TryGetValue(placement.Client, out var window))
                showing.Add(window);
        }

        return showing;
    }

    public WindowId[] VisibleWindows()
    {
        var showing = Showing();
        var visible = new List<WindowId>();
        foreach (var client in Layout.Clients)
        {
            if (!client.Minimized && showing.Contains(client.Window))
                visible.Add(client.Window);
        }

        return visible.ToArray();
    }

    public HashSet<WindowId> Covered()
    {
        var showing = Showing();
        var covered = new HashSet<WindowId>();
        foreach (var client in Layout.Clients)
        {
            if (client.Minimized || client.Floating || showing.Contains(client.Window))
                continue;

            covered.Add(client.Window);
        }

        foreach (var window in _waiting.Keys)
            covered.Add(window);

        return covered;
    }

    /// <summary>Adds the windows that <see cref="Covered"/> uncovered or covered since <paramref name="covered"/>.</summary>
    public void Collect(HashSet<WindowId> covered, List<WindowId> show, List<WindowId> hide)
    {
        var after = Covered();
        foreach (var window in after)
        {
            if (!covered.Contains(window))
                hide.Add(window);
        }

        foreach (var window in covered)
        {
            if (!after.Contains(window) && ClientOf(window) is { Minimized: false })
                show.Add(window);
        }
    }

    public WindowId? LastVisible()
    {
        for (var i = Layout.Clients.Count - 1; i >= 0; i--)
        {
            if (!Layout.Clients[i].Minimized)
                return Layout.Clients[i].Window;
        }

        return null;
    }

    public WindowId? Remembered()
    {
        if (Focus is WindowId focus && ClientOf(focus) is { Minimized: false })
            return focus;

        return LastVisible();
    }

    /// <summary>
    /// Takes a Client out of the Layout to move it to another Workspace, with its fullscreen modes,
    /// which <see cref="Receive"/> puts back as the pinned release does.
    /// </summary>
    public bool Take(WindowId window, out Taken taken)
    {
        var client = ClientOf(window);
        if (!Layout.TryTake(window, out taken))
            return false;

        if (client is not null)
            taken = taken with { Fullscreen = client.Fullscreen, FullscreenClient = client.FullscreenClient };

        return true;
    }

    /// <summary>Adds a window another Layout gave up, keeping what it knew about the window.</summary>
    public void Receive(WindowId window, Taken taken, Rectangle area, Rectangle monitor = default)
    {
        if (taken.ScrollingWidth is double width)
            Layout.NoteScrollingWidth(window, width);

        Layout.Add(
            window,
            area,
            null,
            taken.Floating,
            taken.Bounds,
            Id,
            taken.Class,
            taken.Title,
            monitor: monitor,
            processId: taken.ProcessId,
            initialClass: taken.InitialClass,
            initialTitle: taken.InitialTitle,
            minSize: taken.MinSize,
            maxSize: taken.MaxSize,
            tiledLimits: taken.TiledLimits);
        if (taken.Fullscreen != FullscreenMode.None || taken.FullscreenClient != FullscreenMode.None)
            Layout.SetFullscreen(window, taken.Fullscreen, taken.FullscreenClient);
        if (taken.Minimized)
            Layout.Minimize(window);
    }
}
