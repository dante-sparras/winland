namespace Winland.Domain;

/// <summary>
/// The Shown workspace of each Monitor, the Special workspace it shows over it, and the visits
/// <c>workspace previous</c> reads. A live Monitor, one Windows reports now, starts with no Shown workspace until it is
/// given one. What a Monitor showed last is kept after it leaves, so <see cref="ActiveOn"/> still answers for it.
/// </summary>
internal sealed class ShownWorkspaces
{
    private readonly HashSet<MonitorId> _live = [];
    private readonly Dictionary<MonitorId, WorkspaceId> _shown = [];
    private readonly Dictionary<MonitorId, WorkspaceId> _lastShown = [];
    private readonly Dictionary<MonitorId, WorkspaceId> _special = [];
    private readonly Dictionary<MonitorId, WorkspaceId> _lastSpecial = [];

    // Newest first, each with the Monitor it was on. A visit to a Workspace that was removed since stays, as a gone
    // one, until the visits are collected.
    private readonly List<Visited> _visits = [];

    /// <summary>The Shown workspace of a live Monitor, or null when it has none yet.</summary>
    public WorkspaceId? Shown(MonitorId monitor) => _shown.TryGetValue(monitor, out var shown) ? shown : null;

    /// <summary>The Workspace a Monitor showed last, live or not, or null when it never showed one.</summary>
    public WorkspaceId? LastShown(MonitorId monitor) => _lastShown.TryGetValue(monitor, out var last) ? last : null;

    /// <summary>
    /// The Workspace a Monitor shows: its Shown workspace, else the one it showed last, else Workspace 1. Answers for a
    /// Monitor that is not live too.
    /// </summary>
    public WorkspaceId ActiveOn(MonitorId monitor) =>
        Shown(monitor) ?? (_lastShown.TryGetValue(monitor, out var last) ? last : WorkspaceId.Initial);

    /// <summary>The Special workspace a Monitor shows, or null. For a Monitor that is not live, the one it showed last.</summary>
    public WorkspaceId? SpecialOn(MonitorId monitor)
    {
        var map = _live.Contains(monitor) ? _special : _lastSpecial;
        return map.TryGetValue(monitor, out var special) ? special : null;
    }

    /// <summary>Makes <paramref name="workspace"/> the Shown workspace of <paramref name="monitor"/>.</summary>
    public void Show(MonitorId monitor, WorkspaceId workspace)
    {
        if (_live.Contains(monitor))
            _shown[monitor] = workspace;
        _lastShown[monitor] = workspace;
    }

    /// <summary>Shows <paramref name="workspace"/> over <paramref name="monitor"/>, or closes its Special workspace when null.</summary>
    public void ShowSpecial(MonitorId monitor, WorkspaceId? workspace)
    {
        if (workspace is WorkspaceId special)
        {
            if (_live.Contains(monitor))
                _special[monitor] = special;
            _lastSpecial[monitor] = special;
            return;
        }

        _special.Remove(monitor);
        _lastSpecial.Remove(monitor);
    }

    /// <summary>
    /// The Monitors Windows reports now. One that was not live before starts with no Shown workspace and no Special
    /// workspace. One that left keeps only what it showed last.
    /// </summary>
    public void SetLive(IEnumerable<MonitorId> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        var next = new HashSet<MonitorId>(monitors);
        foreach (var monitor in _live)
        {
            if (!next.Contains(monitor))
                Unassign(monitor);
        }

        foreach (var monitor in next)
        {
            if (!_live.Contains(monitor))
                Unassign(monitor);
        }

        _live.Clear();
        _live.UnionWith(next);
    }

    /// <summary>A Workspace took a new id: every Monitor that shows it, showed it last, or visited it follows.</summary>
    public void Rename(WorkspaceId from, WorkspaceId to)
    {
        Replace(_shown, from, to);
        Replace(_lastShown, from, to);
        Replace(_special, from, to);
        Replace(_lastSpecial, from, to);
        for (var i = 0; i < _visits.Count; i++)
        {
            if (!_visits[i].Gone && _visits[i].Workspace == from)
                _visits[i] = _visits[i] with { Workspace = to };
        }
    }

    /// <summary>A Workspace was removed: no Monitor shows it, and its visits are gone.</summary>
    public void Remove(WorkspaceId workspace)
    {
        Drop(_shown, workspace);
        Drop(_lastShown, workspace);
        for (var i = 0; i < _visits.Count; i++)
        {
            if (_visits[i].Workspace == workspace)
                _visits[i] = _visits[i] with { Gone = true };
        }
    }

    /// <summary>
    /// Records a visit to <paramref name="workspace"/> on <paramref name="monitor"/>. Without
    /// <paramref name="allowCycles"/>, a visit to the newest visited Workspace again is not recorded.
    /// </summary>
    public void Visit(WorkspaceId workspace, MonitorId monitor, bool allowCycles)
    {
        if (!allowCycles && _visits.Count > 0 && _visits[0] is { Gone: false } newest && newest.Workspace == workspace)
            return;

        _visits.RemoveAll(visit => !visit.Gone && visit.Workspace == workspace);
        _visits.Insert(0, new Visited(workspace, monitor));
    }

    /// <summary>
    /// The Workspace visited before the one <paramref name="monitor"/> shows, or null. With <paramref name="perMonitor"/>
    /// only a visit on that Monitor counts.
    /// </summary>
    public WorkspaceId? Previous(MonitorId monitor, bool perMonitor)
    {
        Collect();
        var active = ActiveOn(monitor);
        var index = _visits.FindIndex(visit => !visit.Gone && visit.Workspace == active);
        if (!perMonitor)
            return index >= 0 && index + 1 < _visits.Count ? _visits[index + 1].Workspace : null;

        for (var i = index < 0 ? 0 : index + 1; i < _visits.Count; i++)
        {
            if (_visits[i].Monitor == monitor)
                return _visits[i].Workspace;
        }

        return null;
    }

    private void Unassign(MonitorId monitor)
    {
        _shown.Remove(monitor);
        _special.Remove(monitor);
    }

    // Drops the visits to removed Workspaces: the newest visit on each Monitor is kept until that Monitor has been
    // seen past the first one in the list, so it can still be gone to.
    private void Collect()
    {
        var seen = new List<MonitorId>();
        _visits.RemoveAll(visit =>
        {
            for (var i = 1; i < seen.Count; i++)
            {
                if (seen[i] == visit.Monitor)
                    return visit.Gone;
            }

            seen.Add(visit.Monitor);
            return false;
        });
    }

    private static void Replace(Dictionary<MonitorId, WorkspaceId> map, WorkspaceId from, WorkspaceId to)
    {
        foreach (var (monitor, workspace) in map.ToArray())
        {
            if (workspace == from)
                map[monitor] = to;
        }
    }

    private static void Drop(Dictionary<MonitorId, WorkspaceId> map, WorkspaceId workspace)
    {
        foreach (var (monitor, shown) in map.ToArray())
        {
            if (shown == workspace)
                map.Remove(monitor);
        }
    }

    private readonly record struct Visited(WorkspaceId Workspace, MonitorId Monitor, bool Gone = false);
}
