namespace Winland;

/// <summary>
/// Shared steps for <see cref="Layout.Substitute"/>: a Layout hands one Client's slot to a Window it
/// does not hold, and the slot keeps its place, size and floating state.
/// </summary>
internal static class SlotHandover
{
    /// <summary>What the Layout knew about the Client that leaves the slot.</summary>
    public static Taken Describe(Client client, Rectangle bounds, Size? min, Size? max, double? scrollingWidth = null) =>
        new(
            client.Floating,
            client.Minimized,
            bounds,
            client.Class,
            client.Title,
            client.ProcessId,
            client.InitialClass,
            client.InitialTitle,
            min,
            max,
            scrollingWidth);

    /// <summary>The slot's Client record, now naming <paramref name="next"/>.</summary>
    public static Client Rename(Client client, WindowId next, Taken incoming) =>
        client with
        {
            Id = new ClientId(next.Value),
            Window = next,
            Class = incoming.Class,
            Title = incoming.Title,
            ProcessId = incoming.ProcessId,
            InitialClass = incoming.InitialClass.Length == 0 ? incoming.Class : incoming.InitialClass,
            InitialTitle = incoming.InitialTitle.Length == 0 ? incoming.Title : incoming.InitialTitle,
        };

    public static void Move<TValue>(Dictionary<ClientId, TValue> map, ClientId from, ClientId to)
    {
        if (map.Remove(from, out var value))
            map[to] = value;
    }

    public static void Move(HashSet<ClientId> set, ClientId from, ClientId to)
    {
        if (set.Remove(from))
            set.Add(to);
    }

    public static void Move(List<ClientId> list, ClientId from, ClientId to)
    {
        var index = list.IndexOf(from);
        if (index >= 0)
            list[index] = to;
    }
}
