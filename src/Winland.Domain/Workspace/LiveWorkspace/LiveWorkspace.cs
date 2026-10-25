namespace Winland.Domain;

/// <summary>
/// A Workspace the core owns, with the Layout that places its Clients. <see cref="Workspace"/> is the
/// snapshot of it that <see cref="State"/> reports.
/// </summary>
/// <remarks>
/// A numbered Workspace is that number on one Monitor. A Guest keeps the identity of its own Monitor
/// and the number it had there, and shows a different number until that Monitor comes back.
/// </remarks>
internal sealed partial class LiveWorkspace(WorkspaceId id, MonitorId monitor, Layout layout)
{
    public WorkspaceId Id { get; private set; } = id;

    /// <summary>Gives the Client, shown or waiting in a Group, its window's new title.</summary>
    public void Retitle(WindowId window, string title)
    {
        Layout.Retitle(window, title);
        if (_waiting.TryGetValue(window, out var waiting))
            _waiting[window] = waiting with { Client = waiting.Client with { Title = title } };
    }

    /// <summary>Gives the Client, shown or waiting in a Group, its window's new class.</summary>
    public void Reclass(WindowId window, string windowClass)
    {
        Layout.Reclass(window, windowClass);
        if (_waiting.TryGetValue(window, out var waiting))
            _waiting[window] = waiting with { Client = waiting.Client with { Class = windowClass } };
    }

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

    /// <summary>The Client this Workspace gives focus back to when it is shown again. Set through FocusState.</summary>
    public WindowId? Focus { get; private set; }

    internal void RememberFocus(WindowId? window) => Focus = window;

    public bool Persistent { get; set; }

    /// <summary>The identity of the Monitor a Guest belongs to. Null when the Workspace is on its own Monitor.</summary>
    public string? HomeIdentity { get; private set; }

    /// <summary>The number a Guest had on its own Monitor.</summary>
    public long HomeNumber { get; private set; }

    public bool IsGuest => HomeIdentity is not null;

    // A Workspace becomes a Guest, and stops being one, only through WorkspaceSet.
    internal void MarkGuest(string homeIdentity, long homeNumber)
    {
        HomeIdentity = homeIdentity;
        HomeNumber = homeNumber;
    }

    internal void ClearGuest()
    {
        HomeIdentity = null;
        HomeNumber = 0;
    }

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
            if (IsCovering(client))
                return client.Fullscreen;
        }

        return FullscreenMode.None;
    }

    /// <summary>
    /// Whether a Client's fullscreen or maximized mode covers this Workspace. One its Layout holds fullscreen
    /// covers only while it fills the view, as Winland's scrolling Layout reports it.
    /// </summary>
    public bool IsCovering(Client client) =>
        client.Fullscreen != FullscreenMode.None && (!client.LayoutFullscreen || Layout.FullscreenCovers(client.Window));

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
    /// which <see cref="Receive"/> puts back.
    /// </summary>
    public bool Take(WindowId window, out Taken taken)
    {
        var client = ClientOf(window);
        if (!Layout.TryTake(window, out taken))
            return false;

        if (client is not null)
            taken = taken with { Fullscreen = client.Fullscreen, FullscreenClient = client.FullscreenClient, LayoutFullscreen = client.LayoutFullscreen };

        return true;
    }

    /// <summary>Adds a window another Layout gave up, keeping what it knew about the window.</summary>
    /// <remarks><paramref name="pointer"/> is where the window goes in, as a Layout places a window opening at the pointer.</remarks>
    public void Receive(WindowId window, Taken taken, Rectangle area, Rectangle monitor = default, Point? pointer = null)
    {
        if (taken.ScrollingWidth is double width)
            Layout.NoteScrollingWidth(window, width);

        Layout.Add(
            window,
            area,
            pointer,
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
            Layout.SetFullscreen(window, taken.Fullscreen, taken.FullscreenClient, taken.LayoutFullscreen);
        if (taken.Minimized)
            Layout.Minimize(window);
    }
}
