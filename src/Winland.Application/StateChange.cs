namespace Winland.Application;

/// <summary>
/// What changed between the <see cref="State"/> before a step and the one after it: each Client before and after, by
/// window, which were hidden, and which Clients closed and opened.
/// </summary>
internal sealed record StateChange
{
    private StateChange(State before, State after)
    {
        Before = before;
        After = after;
        Was = ByWindow(before.Clients);
        Is = ByWindow(after.Clients);
        HiddenBefore = new HashSet<WindowId>(before.Hidden.Select(client => client.Window));
        HiddenAfter = new HashSet<WindowId>(after.Hidden.Select(client => client.Window));
        Closed = [.. before.Clients.Select(client => client.Window).Where(window => !Is.ContainsKey(window))];
        Opened = [.. after.Clients.Select(client => client.Window).Where(window => !Was.ContainsKey(window))];
    }

    public State Before { get; }

    public State After { get; }

    /// <summary>Each Client before the step, by window.</summary>
    public IReadOnlyDictionary<WindowId, Client> Was { get; }

    /// <summary>Each Client after the step, by window.</summary>
    public IReadOnlyDictionary<WindowId, Client> Is { get; }

    public IReadOnlySet<WindowId> HiddenBefore { get; }

    public IReadOnlySet<WindowId> HiddenAfter { get; }

    /// <summary>The Clients that went away, in the order the State before listed them.</summary>
    public IReadOnlyList<WindowId> Closed { get; }

    /// <summary>The Clients that are new, in the order the State after lists them.</summary>
    public IReadOnlyList<WindowId> Opened { get; }

    public static StateChange Between(State before, State after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return new StateChange(before, after);
    }

    /// <summary>True when <paramref name="window"/> was a Client shown before the step.</summary>
    public bool WasShown(WindowId window) => Was.ContainsKey(window) && !HiddenBefore.Contains(window);

    private static Dictionary<WindowId, Client> ByWindow(IReadOnlyList<Client> clients)
    {
        var map = new Dictionary<WindowId, Client>(clients.Count);
        foreach (var client in clients)
            map[client.Window] = client;

        return map;
    }
}
