namespace Winland;

public readonly record struct ClientId(long Value);

public sealed record Client(
    ClientId Id,
    WindowId Window,
    bool Floating = false,
    bool Minimized = false,
    WorkspaceId Workspace = default,
    string Class = "",
    string Title = "",
    long ProcessId = 0,
    string InitialClass = "",
    string InitialTitle = "",
    Rectangle Bounds = default,
    FullscreenMode Fullscreen = FullscreenMode.None,
    FullscreenMode FullscreenClient = FullscreenMode.None,
    bool Pinned = false,
    WindowId? Swallowing = null,
    bool InhibitingIdle = false);

public readonly record struct Placement(ClientId Client, Rectangle Bounds);

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
}

public sealed record Outcome(
    IReadOnlyList<Placement> Placements,
    State State,
    bool Consumed = false,
    string? Dispatcher = null,
    WindowId? Close = null,
    WindowId? Focus = null)
{
    public IReadOnlyList<WindowId> Show { get; init; } = [];

    public IReadOnlyList<WindowId> Hide { get; init; } = [];

    // Minimized Clients that were hidden with their Workspace and must be shown
    // again without leaving the minimized state.
    public IReadOnlyList<WindowId> ShowMinimized { get; init; } = [];

    public bool AllowsElevation { get; init; }

    public Launch? Launch { get; init; }

    public IReadOnlyList<StreamEvent> Events { get; init; } = [];

    public IReadOnlyList<Decoration> Decorations { get; init; } = [];

    /// <summary>How the windows in this outcome animate to their placements, shows, hides and closes.</summary>
    public IReadOnlyList<Motion> Motions { get; init; } = [];

    public IReadOnlyList<WindowId> ResetDecorations { get; init; } = [];

    /// <summary>Every Group tab bar to draw now. A bar that is not listed is removed.</summary>
    public IReadOnlyList<GroupBar> GroupBars { get; init; } = [];

    public IReadOnlyList<DisplayOrientationRequest> Orientations { get; init; } = [];

    public WarpTo? Warp { get; init; }

    public InputSettingsRequest? InputSettings { get; init; }

    public CursorBehavior? Cursor { get; init; }

    /// <summary>The border cursor while the pointer is over a resize grab. Null when it is not.</summary>
    public string? ResizeCursor { get; init; }

    public string? Error { get; init; }

    /// <summary>Terminate the Client's process. The adapter performs it; tests pass a stand-in.</summary>
    public KillRequest? Kill { get; init; }

    /// <summary>Stop the daemon after the command reply is written.</summary>
    public bool Exit { get; init; }

    public IReadOnlyList<DisplayPowerRequest> DisplayPower { get; init; } = [];

    /// <summary>Keys to send on to a window. The adapter sends them; tests read them.</summary>
    public IReadOnlyList<KeyRelay> Relays { get; init; } = [];

    /// <summary>Clients to raise above or lower below the other windows, in order.</summary>
    public IReadOnlyList<ZOrderRequest> ZOrder { get; init; } = [];

    /// <summary>Text a plugin dispatcher reports on success. Empty when the dispatcher has nothing to say.</summary>
    public string? Notice { get; init; }

    /// <summary>Where this outcome falls among the ones its Core produced. Null for an outcome made outside a Core.</summary>
    public OutcomeOrder? Order { get; init; }
}

/// <summary>
/// The Core that produced an outcome and its place in that Core's sequence, taken under the Core's lock. Outcomes
/// are applied on several threads after the lock is released, so the adapter uses this to tell an older one.
/// </summary>
public readonly record struct OutcomeOrder(long Core, long Sequence);

/// <summary>A request to end the process that owns a Client. The adapter reads the owner of <see cref="Window"/> again when it acts.</summary>
public readonly record struct KillRequest(long ProcessId)
{
    public WindowId? Window { get; init; }
}

/// <summary>A request to raise a Client to the top of the z-order, or lower it to the bottom.</summary>
public readonly record struct ZOrderRequest(WindowId Window, bool Top);

/// <summary>A request to turn one display, or every display when <see cref="Device"/> is empty, on or off.</summary>
public readonly record struct DisplayPowerRequest(string Device, bool On);
