namespace Winland.Core.Shell;

// The Layout of each Workspace and each Group, carried on the windows (ADR 0050).
public sealed partial class ShellController
{
    // How many placements a Group waits for a member that is alive but not yet among the tiles: one the replaced desktop left hidden is shown
    // as this desktop starts, and the Host lists it a moment later.
    private const int RegroupPatience = 20;

    // The Groups the windows carried when this desktop started, by Workspace, that no session holds yet. A Group is made once every
    // member it names is in the session, after the tiles were read from the rectangles.
    private readonly Dictionary<int, List<RestoredGroup>> _restoring = [];

    // What each window was last given by WriteTiling, and the session and the version each Workspace was last written at.
    private readonly Dictionary<long, int> _tilingSent = [];
    private readonly Dictionary<int, (TilingSession Session, int Version)> _tilingWritten = [];
    private readonly HashSet<long> _tilingSeen = [];

    private sealed class RestoredGroup(List<long> members, long shown)
    {
        public List<long> Members { get; } = members;

        public long Shown { get; } = shown;

        public int Waited { get; set; }
    }

    /// <summary>
    /// Takes the Layout and the Groups the desktop before this one left on the windows (ADR 0050). Each Workspace's Layout is set before a tile
    /// is built or placed, so the first rectangles asked of the gateway are the ones its Layout gives. The Layout is the one most of the
    /// Workspace's tiles carry, and a Group is kept for <see cref="RegroupRestored"/>: the tiles are read from the rectangles first.
    /// </summary>
    /// <param name="homes">The windows of each Workspace, as the marks found them.</param>
    private void ReadTiling(List<long>?[] homes)
    {
        var tiling = _windows.TilingMarks();
        _restoring.Clear();
        for (var home = 1; home <= WorkspaceBook.Maximum; home++)
        {
            if (homes[home] is not { Count: > 0 } windows)
            {
                continue;
            }

            var scrolling = 0;
            Dictionary<int, List<(int Position, long Id, bool Shown)>>? groups = null;
            foreach (var id in windows)
            {
                if (!tiling.TryGetValue(id, out var mark))
                {
                    continue;
                }

                var reading = TilingMark.Read(mark);
                scrolling += reading.Scrolling ? 1 : 0;
                if (reading.Group > 0)
                {
                    groups ??= [];
                    if (!groups.TryGetValue(reading.Group, out var members))
                    {
                        groups[reading.Group] = members = [];
                    }

                    members.Add((reading.Position, id, reading.Shown));
                }
            }

            // A window that moved Workspaces carries the Layout of the one it left until the next write. The Workspace's own tiles outvote it.
            var layout = scrolling * 2 > windows.Count ? LayoutKind.Scrolling : LayoutKind.Dwindle;
            _travel.At(home).SetLayout(layout);
            if (layout == LayoutKind.Scrolling)
            {
                Log.Information("Tiling", "workspace " + home + " is scrolling, as its windows say");
            }

            if (groups is null)
            {
                continue;
            }

            foreach (var group in groups.OrderBy(pair => pair.Key))
            {
                // A Group that names one window is a plain tile, and the window is not lost.
                if (group.Value.Count < 2)
                {
                    continue;
                }

                var ordered = group.Value.OrderBy(member => member.Position).ThenBy(member => member.Id).ToList();
                var shown = ordered.FirstOrDefault(member => member.Shown) is { Id: not 0 } named ? named.Id : ordered[0].Id;
                if (!_restoring.TryGetValue(home, out var restoring))
                {
                    _restoring[home] = restoring = [];
                }

                restoring.Add(new RestoredGroup([.. ordered.Select(member => member.Id)], shown));
            }
        }
    }

    /// <summary>
    /// Makes each Group the windows carried of <paramref name="home"/>, once the session holds the members that are still alive.
    /// A member that is gone, or that another Workspace holds now, is left out, and a Group of fewer than two is no Group.
    /// </summary>
    /// <param name="home">The Workspace number.</param>
    /// <param name="session">That Workspace's session, with its tiles already built.</param>
    /// <param name="wait">True while a member that is alive and on no Workspace yet may still arrive: the Host has not listed it.</param>
    private void RegroupRestored(int home, TilingSession session, bool wait)
    {
        if (!_restoring.TryGetValue(home, out var groups))
        {
            return;
        }

        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var group = groups[i];
            var members = new List<long>(group.Members.Count);
            var coming = false;
            foreach (var id in group.Members)
            {
                if (!_windows.Alive(id))
                {
                    continue;
                }

                if (session.Windows.Contains(id))
                {
                    members.Add(id);
                }
                else if (_travel.HomeOf(id) == 0)
                {
                    coming = true;
                }
            }

            if (wait && coming && group.Waited++ < RegroupPatience)
            {
                continue;
            }

            groups.RemoveAt(i);
            if (session.Regroup(members, group.Shown))
            {
                Log.Information("Tiling", "workspace " + home + " kept a group of " + members.Count + " windows");
            }
        }

        if (groups.Count == 0)
        {
            _restoring.Remove(home);
        }
    }

    /// <summary>
    /// Writes the Layout and the Group of every window of every Workspace onto the window, for each Workspace whose session changed since
    /// it was last written. A window is asked only when what it carries is not what was last written, so a quiet desktop asks nothing.
    /// </summary>
    private void WriteTiling()
    {
        _tilingSeen.Clear();
        foreach (var pair in _travel.Numbered)
        {
            var session = pair.Value;
            if (session.Windows.Count == 0)
            {
                _tilingWritten.Remove(pair.Key);
                continue;
            }

            foreach (var id in session.Windows)
            {
                _tilingSeen.Add(id);
            }

            if (_tilingWritten.TryGetValue(pair.Key, out var written) && ReferenceEquals(written.Session, session) && written.Version == session.Version)
            {
                continue;
            }

            _tilingWritten[pair.Key] = (session, session.Version);
            var scrolling = session.Layout == LayoutKind.Scrolling;
            var groups = session.Groups();
            foreach (var id in session.Windows)
            {
                var mark = TilingMark.Write(scrolling);
                for (var g = 0; g < groups.Count && g < TilingMark.Largest; g++)
                {
                    var members = groups[g].Members;
                    var at = -1;
                    for (var m = 0; m < members.Count && at < 0; m++)
                    {
                        at = members[m] == id ? m : -1;
                    }

                    if (at >= 0)
                    {
                        mark = TilingMark.Write(scrolling, g + 1, at, groups[g].Shown == id);
                        break;
                    }
                }

                if (!_tilingSent.TryGetValue(id, out var sent) || sent != mark)
                {
                    _tilingSent[id] = mark;
                    _windows.MarkTiling(id, mark);
                }
            }
        }

        // A window that left every session keeps what it carries, and is written again where it lands.
        if (_tilingSent.Count > _tilingSeen.Count)
        {
            foreach (var id in _tilingSent.Keys.Where(id => !_tilingSeen.Contains(id)).ToList())
            {
                _tilingSent.Remove(id);
            }
        }
    }
}
