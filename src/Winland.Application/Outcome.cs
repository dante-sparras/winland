namespace Winland.Application;

public sealed record MonitorSnapshot(MonitorId Id, Monitor Monitor, WorkspaceId ActiveWorkspace, bool Focused)
{
    public WorkspaceId? SpecialWorkspace { get; init; }
}

public sealed record State(IReadOnlyList<Client> Clients)
{
    public IReadOnlyList<Workspace> Workspaces { get; init; } = [];

    public WorkspaceId ActiveWorkspace { get; init; } = WorkspaceId.Initial;

    /// <summary>The special workspace shown on the focused Monitor, when one is open.</summary>
    public WorkspaceId? SpecialWorkspace { get; init; }

    public MonitorId ActiveMonitor { get; init; } = MonitorId.Primary;

    public IReadOnlyList<Client> Hidden { get; init; } = [];

    public IReadOnlyList<Monitor> Monitors { get; init; } = [];

    public IReadOnlyList<MonitorSnapshot> Screens { get; init; } = [];

    /// <summary>The members of each Group, in Group order.</summary>
    public IReadOnlyList<IReadOnlyList<WindowId>> Groups { get; init; } = [];

    /// <summary>The Clients from the bottom of the stacking order to the top, as far as Winland raised or lowered them.</summary>
    public IReadOnlyList<WindowId> Stacking { get; init; } = [];
}

public sealed record Outcome(
    IReadOnlyList<Placement> Placements,
    State State,
    bool Consumed = false,
    string? Dispatcher = null,
    WindowId? Focus = null)
{
    /// <summary>Clients to close, in order. A bind or batch that runs several commands can close several.</summary>
    public IReadOnlyList<WindowId> Closes { get; init; } = [];

    /// <summary>The first Client to close, or null. Setting it replaces <see cref="Closes"/> with that one Client.</summary>
    public WindowId? Close
    {
        get => Closes.Count > 0 ? Closes[0] : null;
        init => Closes = value is WindowId window ? [window] : [];
    }

    public IReadOnlyList<WindowId> Show { get; init; } = [];

    public IReadOnlyList<WindowId> Hide { get; init; } = [];

    // Minimized Clients that were hidden with their Workspace and must be shown
    // again without leaving the minimized state.
    public IReadOnlyList<WindowId> ShowMinimized { get; init; } = [];

    /// <summary>Programs to start, in order: every exec the outcome ran and every on-created-empty command.</summary>
    public IReadOnlyList<Launch> Launches { get; init; } = [];

    /// <summary>The first program to start, or null. Setting it replaces <see cref="Launches"/> with that one program.</summary>
    public Launch? Launch
    {
        get => Launches.Count > 0 ? Launches[0] : null;
        init => Launches = value is null ? [] : [value];
    }

    public IReadOnlyList<StreamEvent> Events { get; init; } = [];

    public IReadOnlyList<Decoration> Decorations { get; init; } = [];

    /// <summary>How the windows in this outcome animate to their placements, shows, hides and closes.</summary>
    public IReadOnlyList<Motion> Motions { get; init; } = [];

    /// <summary>
    /// One step of a drag that is still held. Another step follows soon, and the drag's end places every
    /// window exactly, so the adapter moves the windows the quickest way it can.
    /// </summary>
    public bool Live { get; init; }

    public IReadOnlyList<WindowId> ResetDecorations { get; init; } = [];

    /// <summary>Every Group tab bar to draw now. A bar that is not listed is removed.</summary>
    public IReadOnlyList<GroupBar> GroupBars { get; init; } = [];

    /// <summary>The config notification to show, or null when this load had no config errors. The same notice stays until a later load replaces it.</summary>
    public ConfigNotice? ConfigNotice { get; init; }

    /// <summary>The notification-area icon, or null when this outcome does not say.</summary>
    public TrayIcon? Tray { get; init; }


    public WarpTo? Warp { get; init; }

    public CursorBehavior? Cursor { get; init; }

    /// <summary>The border cursor while the pointer is over a resize grab. Null when it is not.</summary>
    public string? ResizeCursor { get; init; }

    public string? Error { get; init; }

    /// <summary>Terminate these Clients' processes, in order. The adapter performs it; tests pass a stand-in.</summary>
    public IReadOnlyList<KillRequest> Kills { get; init; } = [];

    /// <summary>The first kill request, or null. Setting it replaces <see cref="Kills"/> with that one request.</summary>
    public KillRequest? Kill
    {
        get => Kills.Count > 0 ? Kills[0] : null;
        init => Kills = value is KillRequest kill ? [kill] : [];
    }

    /// <summary>Stop the daemon after the command reply is written.</summary>
    public bool Exit { get; init; }

    public IReadOnlyList<DisplayPowerRequest> DisplayPower { get; init; } = [];

    /// <summary>Keys to send on to a window. The adapter sends them; tests read them.</summary>
    public IReadOnlyList<KeyRelay> Relays { get; init; } = [];

    /// <summary>Clients to raise above or lower below the other windows, in order.</summary>
    public IReadOnlyList<ZOrderRequest> ZOrder { get; init; } = [];

    /// <summary>Where this outcome falls among the ones its WindowManager produced. Null for an outcome made outside a WindowManager.</summary>
    public OutcomeOrder? Order { get; init; }

    /// <summary>
    /// This outcome followed by <paramref name="next"/>, as one: what both ask the adapter to do, in order, and the state
    /// after the second. Requests are joined. What describes the state after the step is the second's. A value the second
    /// leaves unset keeps the first's, and the first error wins, so a later step cannot hide an earlier failure.
    /// A property added to this record needs a rule here; a test counts them.
    /// </summary>
    public Outcome Then(Outcome next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return next with
        {
            Placements = Union(Placements, next.Placements, next.State),
            Focus = next.Focus ?? Focus,
            Closes = [.. Closes, .. next.Closes],
            Show = [.. Show, .. next.Show],
            Hide = [.. Hide, .. next.Hide],
            ShowMinimized = [.. ShowMinimized, .. next.ShowMinimized],
            Launches = [.. Launches, .. next.Launches],
            Events = [.. Events, .. next.Events],
            Tray = next.Tray ?? Tray,
            Warp = next.Warp ?? Warp,
            Cursor = next.Cursor ?? Cursor,
            Error = Error ?? next.Error,
            Kills = [.. Kills, .. next.Kills],
            Exit = Exit || next.Exit,
            DisplayPower = [.. DisplayPower, .. next.DisplayPower],
            Relays = [.. Relays, .. next.Relays],
            ZOrder = [.. ZOrder, .. next.ZOrder],
        };
    }

    // Every placement of the later outcome, and each earlier one it does not replace, for a Client that is still
    // open and not hidden. A command that moves one floating window places only that window, so the earlier
    // command's window would otherwise keep its old rectangle.
    internal static IReadOnlyList<Placement> Union(IReadOnlyList<Placement> first, IReadOnlyList<Placement> second, State state)
    {
        if (first.Count == 0)
            return second;

        var placed = new HashSet<ClientId>(second.Count);
        foreach (var placement in second)
            placed.Add(placement.Client);

        var open = new HashSet<ClientId>(state.Clients.Count);
        foreach (var client in state.Clients)
            open.Add(client.Id);
        foreach (var client in state.Hidden)
            open.Remove(client.Id);

        var merged = new List<Placement>(first.Count + second.Count);
        foreach (var placement in first)
        {
            if (!placed.Contains(placement.Client) && open.Contains(placement.Client))
                merged.Add(placement);
        }

        merged.AddRange(second);
        return merged;
    }
}

/// <summary>
/// The WindowManager that produced an outcome and its place in that WindowManager's sequence, taken under the WindowManager's lock. Outcomes
/// are applied on several threads after the lock is released, so the adapter uses this to tell an older one.
/// </summary>
public readonly record struct OutcomeOrder(long WindowManager, long Sequence);

/// <summary>A request to end the process that owns a Client. The adapter reads the owner of <see cref="Window"/> again when it acts.</summary>
public readonly record struct KillRequest(long ProcessId)
{
    public WindowId? Window { get; init; }
}

/// <summary>A request to raise a Client to the top of the z-order, or lower it to the bottom.</summary>
public readonly record struct ZOrderRequest(WindowId Window, bool Top);

/// <summary>A request to turn one display, or every display when <see cref="Device"/> is empty, on or off.</summary>
public readonly record struct DisplayPowerRequest(string Device, bool On);
