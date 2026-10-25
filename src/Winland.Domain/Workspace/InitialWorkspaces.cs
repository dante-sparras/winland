namespace Winland.Domain;

/// <summary>
/// The Workspace each program Winland starts was started on, so its window opens there. A token is held by the
/// started process and every process it starts. The first window that opens uses the token up.
/// </summary>
internal sealed class InitialWorkspaces
{
    private readonly List<Token> _tokens = [];

    internal void Track(int pid, WorkspaceId workspace, long expiresAt)
    {
        if (pid <= 0)
            return;

        _tokens.Add(new Token(pid, workspace, expiresAt));
    }

    /// <summary>A token for a Workspace whose id changed now names the new id.</summary>
    internal void Readdress(WorkspaceId from, WorkspaceId to)
    {
        foreach (var token in _tokens)
        {
            if (token.Workspace == from)
                token.Workspace = to;
        }
    }

    internal void Note(int pid, int parentPid)
    {
        if (pid <= 0 || parentPid <= 0 || pid == parentPid)
            return;

        foreach (var token in _tokens)
        {
            if (token.Pids.Contains(parentPid))
                token.Pids.Add(pid);
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

    /// <summary>The Workspace a window of this process opens on, or null.</summary>
    internal WorkspaceId? Take(int pid, long now)
    {
        _tokens.RemoveAll(token => now > token.ExpiresAt);
        if (pid <= 0)
            return null;

        for (var i = 0; i < _tokens.Count; i++)
        {
            var token = _tokens[i];
            if (!token.Pids.Contains(pid))
                continue;

            _tokens.RemoveAt(i);
            return token.Workspace;
        }

        return null;
    }

    private sealed class Token(int pid, WorkspaceId workspace, long expiresAt)
    {
        public HashSet<int> Pids { get; } = [pid];

        public WorkspaceId Workspace { get; set; } = workspace;

        public long ExpiresAt { get; } = expiresAt;
    }
}
