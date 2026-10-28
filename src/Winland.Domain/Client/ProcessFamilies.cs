namespace Winland.Domain;

/// <summary>
/// Values held for programs Winland starts, such as the rules of an <c>exec</c> rule block or the Workspace it was
/// started on. A value is held by the started process and every process it starts, until it expires or the first
/// window of that family takes it.
/// </summary>
internal sealed class ProcessFamilies<T>
{
    private readonly List<Family> _families = [];

    internal void Track(int pid, T value, long expiresAt)
    {
        if (pid > 0)
            _families.Add(new Family(pid, value, expiresAt));
    }

    /// <summary>Changes every value held, as a Workspace whose id changed does.</summary>
    internal void Update(Func<T, T> change)
    {
        foreach (var family in _families)
            family.Value = change(family.Value);
    }

    internal void Note(int pid, int parentPid)
    {
        if (pid <= 0 || parentPid <= 0 || pid == parentPid)
            return;

        foreach (var family in _families)
        {
            if (family.Pids.Contains(parentPid))
                family.Pids.Add(pid);
        }
    }

    internal void NoteLineage(int pid, IReadOnlyList<int>? ancestorsNearestFirst)
    {
        if (pid <= 0 || ancestorsNearestFirst is null || ancestorsNearestFirst.Count == 0)
            return;

        var chain = new int[ancestorsNearestFirst.Count + 1];
        chain[0] = pid;
        for (var i = 0; i < ancestorsNearestFirst.Count; i++)
            chain[i + 1] = ancestorsNearestFirst[i];

        for (var i = chain.Length - 1; i > 0; i--)
            Note(chain[i - 1], chain[i]);
    }

    /// <summary>Takes the value held for this process's family, the oldest first.</summary>
    internal bool TryTake(int pid, long now, out T value)
    {
        _families.RemoveAll(family => now > family.ExpiresAt);
        for (var i = 0; pid > 0 && i < _families.Count; i++)
        {
            if (!_families[i].Pids.Contains(pid))
                continue;

            value = _families[i].Value;
            _families.RemoveAt(i);
            return true;
        }

        value = default!;
        return false;
    }

    private sealed class Family(int pid, T value, long expiresAt)
    {
        public HashSet<int> Pids { get; } = [pid];

        public T Value { get; set; } = value;

        public long ExpiresAt { get; } = expiresAt;
    }
}
