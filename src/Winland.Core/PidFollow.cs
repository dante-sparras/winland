namespace Winland.Core;

internal sealed class PidFollowSet
{
    internal const long TimeoutMilliseconds = 60_000;

    private readonly List<Watch> _watches = [];

    internal void Track(int pid, IReadOnlyList<WindowRule> rules, long now)
    {
        if (pid <= 0 || rules.Count == 0)
            return;

        Expire(now);
        _watches.Add(new Watch(pid, rules, now + TimeoutMilliseconds));
    }

    internal void Note(int pid, int parentPid)
    {
        if (pid <= 0 || parentPid <= 0 || pid == parentPid)
            return;

        foreach (var watch in _watches)
        {
            if (watch.Pids.Contains(parentPid))
                watch.Pids.Add(pid);
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

    internal IReadOnlyList<WindowRule> Take(int pid, long now)
    {
        Expire(now);
        if (pid <= 0)
            return [];

        for (var i = 0; i < _watches.Count; i++)
        {
            if (!_watches[i].Pids.Contains(pid))
                continue;

            var rules = _watches[i].Rules;
            _watches.RemoveAt(i);
            return rules;
        }

        return [];
    }

    private void Expire(long now) =>
        _watches.RemoveAll(watch => now > watch.ExpiresAt);

    private sealed class Watch(int pid, IReadOnlyList<WindowRule> rules, long expiresAt)
    {
        public HashSet<int> Pids { get; } = [pid];

        public IReadOnlyList<WindowRule> Rules { get; } = rules;

        public long ExpiresAt { get; } = expiresAt;
    }
}
