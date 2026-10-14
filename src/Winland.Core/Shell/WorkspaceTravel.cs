using System.Globalization;

namespace Winland.Core.Shell;

/// <summary>
/// Keeps the Workspace in front on each monitor, which of them has the keyboard, the windows each Workspace holds,
/// and the monitor each belongs to (ADR 0003, ADR 0079). It shows and hides nothing: <see cref="ShellController"/> does, around each change made here.
/// </summary>
public sealed class WorkspaceTravel
{
    private readonly IWindowGateway _windows;
    private readonly ITileSettings _settings;
    private readonly IWorkspaceHook _hooks;
    private readonly WorkspaceBook _book = new();
    private readonly Dictionary<int, TilingSession> _sessions = [];
    // The monitor each workspace belongs to, as the rectangle it had when last read. An index would name another monitor
    // once one is added or removed. A workspace whose monitor went away keeps its rectangle, and is back there when the monitor is.
    // It is kept nowhere on disk: a desktop that starts reads it from where the workspace's windows are.
    private readonly Dictionary<int, TileRect> _home = [];

    // The Workspace in front on each monitor that has been seen. One whose monitor went away keeps its entry,
    // so the same Workspace is in front there when the monitor is back.
    private readonly List<Stand> _stands = [];
    // The Free windows that stand alone, each with its Workspace. The shell keeps them. They take no tile,
    // and a Workspace holds them as it holds its tiles: it exists while one is on it.
    private IReadOnlyDictionary<long, int>? _free;
    private readonly TileRect[] _single = new TileRect[1];
    private readonly List<int> _numbers = [];
    private readonly List<int> _was = [];

    private sealed class Stand
    {
        // The monitor's work area, as last read.
        public TileRect Area;

        // The Workspace in front there, or zero before one is picked.
        public int Front;

        // Where the monitor is in the list just read, or -1 while it is away.
        public int Index = -1;
    }

    /// <summary>Wires the tiler and the monitor each workspace occupies.</summary>
    /// <param name="windows">Windows, work areas, and the bar monitor.</param>
    /// <param name="settings">Tiling settings. Spacing is copied onto each new session.</param>
    /// <param name="hooks">Hears <c>workspace-changed</c>.</param>
    public WorkspaceTravel(IWindowGateway windows, ITileSettings settings, IWorkspaceHook hooks)
    {
        _windows = windows;
        _settings = settings;
        _hooks = hooks;
        for (var number = 1; number <= WorkspaceBook.Minimum; number++)
        {
            _sessions[number] = NewSession();
        }
    }

    internal WorkspaceBook Book => _book;

    internal bool LayoutDirty { get; set; }

    internal TilingSession Current => Session(_book.Current);

    /// <summary>
    /// The work area for <paramref name="workspace"/>.
    /// An empty monitor list uses the bar monitor's work area.
    /// The first read of a workspace uses the monitor that contains that work area.
    /// Later reads use the same monitor, wherever it then is in the monitor list.
    /// </summary>
    /// <param name="workspace">Workspace number.</param>
    /// <returns>The rectangle that workspace's windows occupy.</returns>
    public TileRect Area(int workspace) => Area(_windows.Monitors, _windows.FirstBarArea, workspace);

    /// <summary>The monitor index <see cref="Area(int)"/> uses for <paramref name="workspace"/>.</summary>
    /// <param name="workspace">Workspace number.</param>
    /// <returns>An index into the monitor list, or zero when that list is empty.</returns>
    public int Monitor(int workspace) => Monitor(_windows.Monitors, _windows.FirstBarArea, workspace);

    /// <summary>
    /// Moves the Workspace with the keyboard onto the monitor in <paramref name="direction"/>, in front there, and the keyboard goes with it.
    /// The Workspace that was in front there is no longer. The monitor it left shows another Workspace of its own that holds a window,
    /// or a new one with the lowest free number.
    /// </summary>
    /// <param name="direction">The arrow.</param>
    /// <returns>False when no monitor lies that way, or when there is only one.</returns>
    public bool Move(Direction direction)
    {
        Settle();
        var monitors = Seat(out var fallback);
        if (monitors.Count < 2)
        {
            return false;
        }

        var workspace = _book.Current;
        var here = Home(monitors, fallback, workspace);
        var next = MonitorPick.Neighbor(monitors, here, direction);
        if (next == here)
        {
            return false;
        }

        Log.Information("Workspace", "workspace " + workspace + " moves from monitor " + here + " to monitor " + next);
        _home[workspace] = monitors[next];
        StandAt(next).Front = workspace;
        StandAt(here).Front = 0;
        Settle();
        LayoutDirty = true;
        return true;
    }

    /// <summary>
    /// Makes the Workspaces in front follow the monitors: every monitor has one in front, and it is one that belongs there.
    /// A monitor with none takes another Workspace of its own that holds a window, or a new one with the lowest free number.
    /// A monitor that went away hands its Workspaces to the monitor the bar is on, and they remember where they came from:
    /// when it is back they return, with the same one in front. The keyboard goes to the Workspace in front where its own now is.
    /// </summary>
    /// <returns>True when a Workspace in front changed, or the keyboard went to another Workspace.</returns>
    public bool Settle()
    {
        var monitors = Seat(out var fallback);
        var changed = false;
        var current = _book.Current;
        _was.Clear();
        _was.AddRange(_numbers);

        // The Workspace with the keyboard is in front where it is, unless its monitor went away under it.
        var displaced = _home.TryGetValue(current, out var kept) && Find(monitors, kept) < 0;
        if (displaced)
        {
            // The monitor it falls to is new and shows nothing yet, as when the only monitor is swapped for another.
            // The Workspace with the keyboard is that monitor's own from here on, and nothing changes for the person.
            var index = Match(monitors, fallback);
            if (StandAt(index).Front == 0)
            {
                _home[current] = monitors[index];
                displaced = false;
            }
        }

        if (!displaced)
        {
            var stand = StandAt(Home(monitors, fallback, current));
            if (stand.Front != current)
            {
                stand.Front = current;
                changed = true;
            }
        }

        // First every monitor lets go of a Workspace that is no longer its own, so the next loop can hand it to the monitor it is on.
        foreach (var stand in _stands)
        {
            if (stand.Index >= 0 && stand.Front != 0 && Home(monitors, fallback, stand.Front) != stand.Index)
            {
                stand.Front = 0;
            }
        }

        foreach (var stand in _stands)
        {
            if (stand.Index >= 0 && stand.Front == 0)
            {
                stand.Front = Pick(monitors, fallback, stand.Index);
                changed = true;
            }
        }

        if (displaced)
        {
            var target = StandAt(Home(monitors, fallback, current)).Front;
            if (target != current)
            {
                Log.Information("Workspace", "the monitor of workspace " + current + " went away; the keyboard goes to workspace " + target);
                Keyboard(target);
                changed = true;
            }
        }

        NoteFronts();

        // A monitor that went away took its Workspace off the glass, though no other monitor's changed.
        return changed || !_was.SequenceEqual(_numbers);
    }

    /// <summary>The Workspace in front on each monitor, in the order of the monitors.</summary>
    /// <param name="into">Cleared, then given one number per monitor.</param>
    internal void Fronts(List<int> into)
    {
        into.Clear();
        for (var index = 0; index < _stands.Count; index++)
        {
            var found = false;
            foreach (var stand in _stands)
            {
                if (stand.Index == index && stand.Front != 0)
                {
                    into.Add(stand.Front);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                break;
            }
        }

        if (into.Count == 0)
        {
            // Before the monitors were first read.
            into.Add(_book.Current);
        }
    }

    /// <summary>The Workspace in front on the monitor <paramref name="steps"/> after the one with the keyboard, in the monitors' order, wrapping.</summary>
    /// <param name="steps">1 for the next monitor, -1 for the previous.</param>
    /// <returns>The Workspace with the keyboard when there is one monitor.</returns>
    internal int FrontBeside(int steps)
    {
        Settle();
        var monitors = Seat(out var fallback);
        if (monitors.Count < 2)
        {
            return _book.Current;
        }

        var here = Home(monitors, fallback, _book.Current);
        var index = (((here + steps) % monitors.Count) + monitors.Count) % monitors.Count;
        return StandAt(index).Front;
    }

    /// <summary>The Workspace in front on the monitor in <paramref name="direction"/> from the one with the keyboard.</summary>
    /// <param name="direction">The arrow.</param>
    /// <returns>Zero when no monitor lies that way.</returns>
    internal int FrontToward(Direction direction)
    {
        Settle();
        var monitors = Seat(out var fallback);
        if (monitors.Count < 2)
        {
            return 0;
        }

        var here = Home(monitors, fallback, _book.Current);
        var next = MonitorPick.Neighbor(monitors, here, direction);
        return next == here ? 0 : StandAt(next).Front;
    }

    /// <summary>The Workspace in front on the monitor that holds the point.</summary>
    /// <param name="x">Horizontal position in physical pixels.</param>
    /// <param name="y">Vertical position in physical pixels.</param>
    /// <returns>Zero when no monitor's work area holds it.</returns>
    internal int FrontAt(int x, int y)
    {
        Settle();
        var monitors = Seat(out _);
        for (var i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            if (x >= monitor.X && x < monitor.X + monitor.Width && y >= monitor.Y && y < monitor.Y + monitor.Height)
            {
                return StandAt(i).Front;
            }
        }

        return 0;
    }

    /// <summary>
    /// Says <paramref name="workspace"/> belongs to the monitor that covers the most of <paramref name="where"/>, as a desktop that starts
    /// reads it from where the Workspace's windows are. A rectangle on no monitor leaves the Workspace where it is.
    /// </summary>
    /// <param name="workspace">Workspace number.</param>
    /// <param name="where">A rectangle one of its windows covers.</param>
    internal void Belong(int workspace, TileRect where)
    {
        var monitors = Seat(out _);
        var index = Find(monitors, where);
        if (index >= 0)
        {
            _home[workspace] = monitors[index];
        }
    }

    /// <summary>The index of the monitor that covers the most of <paramref name="where"/>.</summary>
    /// <param name="where">A rectangle in physical pixels.</param>
    /// <returns>-1 when no monitor covers any of it.</returns>
    internal int MonitorOf(TileRect where) => Find(Seat(out _), where);

    /// <summary>How many monitors there are. One when the Host lists none.</summary>
    internal int MonitorCount => Math.Max(1, _windows.Monitors.Count);

    /// <summary>
    /// Keeps the monitor <paramref name="number"/> is on. One that does not exist yet is made on the monitor with the keyboard.
    /// </summary>
    private void Anchor(int number)
    {
        if (!_home.ContainsKey(number) || !_book.Exists(number))
        {
            var monitors = Seat(out var fallback);
            _home[number] = monitors[Home(monitors, fallback, _book.Current)];
        }
    }

    /// <summary>Reads the monitors and finds each one's entry again. A monitor seen for the first time gets one with no Workspace yet.</summary>
    /// <returns>The monitors, or the bar monitor's work area alone when the Host lists none.</returns>
    private IReadOnlyList<TileRect> Seat(out TileRect fallback)
    {
        var monitors = _windows.Monitors;
        fallback = _windows.FirstBarArea;
        if (monitors.Count == 0)
        {
            _single[0] = fallback;
            monitors = _single;
        }

        foreach (var stand in _stands)
        {
            stand.Index = -1;
        }

        for (var i = 0; i < monitors.Count; i++)
        {
            Stand? found = null;
            long most = 0;
            foreach (var stand in _stands)
            {
                if (stand.Index < 0 && Find(monitors, stand.Area) == i && Overlap(stand.Area, monitors[i]) is var covered && covered > most)
                {
                    most = covered;
                    found = stand;
                }
            }

            if (found is null)
            {
                found = new Stand();
                _stands.Add(found);
            }

            found.Index = i;
            found.Area = monitors[i];
        }

        return monitors;
    }

    private Stand StandAt(int index)
    {
        foreach (var stand in _stands)
        {
            if (stand.Index == index)
            {
                return stand;
            }
        }

        // Asked before the monitors were read.
        var made = new Stand { Index = index };
        _stands.Add(made);
        return made;
    }

    /// <summary>
    /// The Workspace a monitor with none in front shows: the lowest of its own that holds a window, or a new one with the lowest free number.
    /// </summary>
    private int Pick(IReadOnlyList<TileRect> monitors, TileRect fallback, int index)
    {
        for (var number = 1; number <= WorkspaceBook.Maximum; number++)
        {
            if (_book.Occupancy(number) > 0 && number != _book.Current && !Stands(number) && Home(monitors, fallback, number) == index)
            {
                return number;
            }
        }

        return Free(monitors, index);
    }

    /// <summary>A new Workspace on the monitor at <paramref name="index"/>, with the lowest number that holds no window and is in front nowhere.</summary>
    private int Free(IReadOnlyList<TileRect> monitors, int index)
    {
        for (var number = 1; number <= WorkspaceBook.Maximum; number++)
        {
            if (_book.Occupancy(number) == 0 && number != _book.Current && !Stands(number))
            {
                _home[number] = monitors[index];
                EnsureSession(number);
                return number;
            }
        }

        // Every number is taken. The monitor shares the Workspace with the keyboard until one is free.
        return _book.Current;
    }

    /// <summary>True when <paramref name="number"/> is in front on a monitor that is attached.</summary>
    private bool Stands(int number)
    {
        foreach (var stand in _stands)
        {
            if (stand.Index >= 0 && stand.Front == number)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tells the book which Workspaces are in front, and forgets the monitor of each Workspace that stopped existing.</summary>
    private void NoteFronts()
    {
        _numbers.Clear();
        foreach (var stand in _stands)
        {
            if (stand.Index >= 0 && stand.Front != 0)
            {
                _numbers.Add(stand.Front);
            }
        }

        _book.SetFront(_numbers);
        if (_home.Count > _numbers.Count)
        {
            for (var number = 1; number <= WorkspaceBook.Maximum; number++)
            {
                // Made again, it is made on the monitor with the keyboard.
                if (!_book.Exists(number))
                {
                    _home.Remove(number);
                }
            }
        }
    }

    /// <summary>Gives <paramref name="number"/> the keyboard in the book, and fires the Workspace Hook when that is a change.</summary>
    private void Keyboard(int number)
    {
        var previous = _book.Current;
        _book.Jump(number);
        EnsureSession(number);
        NoteFronts();
        if (_book.Current != previous)
        {
            _hooks.Fire("workspace-changed", _book.Current.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The tiling session for <paramref name="number"/>, made when this is the first time the Workspace is asked for.</summary>
    /// <param name="number">Workspace number, 1-based.</param>
    /// <returns>The session that holds that workspace's windows.</returns>
    internal TilingSession At(int number) => Session(number);

    internal IEnumerable<TilingSession> Sessions => _sessions.Values;

    /// <summary>Each Workspace that has been asked for, with its session.</summary>
    internal Dictionary<int, TilingSession> Numbered => _sessions;

    private int _groupBar;

    /// <summary>Sets the group strip height on every workspace, and on each one made later.</summary>
    internal void SetGroupBar(int pixels)
    {
        _groupBar = Math.Max(0, pixels);
        foreach (var session in _sessions.Values)
        {
            session.SetGroupBar(_groupBar);
        }
    }

    /// <summary>
    /// Puts <paramref name="fronts"/> in front as a desktop starts, each on the monitor it belongs to, and gives <paramref name="keyboard"/> the keyboard:
    /// they are where the windows on the glass already are (<see cref="WorkspaceFront"/>). A monitor that shows none takes the lowest free number.
    /// Nothing changed for the person, so no Hook fires.
    /// </summary>
    /// <param name="keyboard">The Workspace with the keyboard, 1-based. It is in front on its monitor.</param>
    /// <param name="fronts">The Workspace in front on each monitor that shows one. Null when only <paramref name="keyboard"/> is.</param>
    internal void Start(int keyboard, IReadOnlyList<int>? fronts = null)
    {
        var monitors = Seat(out var fallback);
        _book.Jump(keyboard);
        EnsureSession(keyboard);
        if (fronts is not null)
        {
            foreach (var number in fronts)
            {
                if (number != keyboard)
                {
                    StandAt(Home(monitors, fallback, number)).Front = number;
                    EnsureSession(number);
                }
            }
        }

        StandAt(Home(monitors, fallback, keyboard)).Front = keyboard;

        // A monitor that shows no window takes the lowest free number, and not a Workspace of its own that was hidden:
        // an empty Workspace in front is not kept across a restart, and nothing is shown that was not on the glass.
        foreach (var stand in _stands)
        {
            if (stand.Index >= 0 && stand.Front == 0)
            {
                stand.Front = Free(monitors, stand.Index);
            }
        }

        Settle();
        if (monitors.Count < 2)
        {
            Log.Information("Workspace", "workspace " + keyboard + " is in front");
            return;
        }

        var shown = new List<int>();
        Fronts(shown);
        Log.Information("Workspace", "workspaces " + string.Join(", ", shown) + " are in front, one for each monitor; workspace " + keyboard + " has the keyboard");
    }

    /// <summary>
    /// Gives <paramref name="number"/> the keyboard, in front on its own monitor, and fires the Workspace Hook when that is a change.
    /// A Workspace on another monitor stays there, and the keyboard goes to it. A number that did not exist is made on the monitor
    /// with the keyboard, and no other is: a Workspace exists while it holds a window or is in front.
    /// </summary>
    /// <param name="number">Workspace number, 1-based.</param>
    /// <param name="here">
    /// True when a Chord asked for the Workspace by number. One that is hidden then comes to the monitor with the keyboard, and is
    /// that monitor's own from then on. One that is in front on another monitor stays there, and the keyboard goes to it.
    /// </param>
    internal void Go(int number, bool here = false)
    {
        PruneClosed();
        Settle();
        Log.Information("Workspace", "from " + _book.Current + " to " + number);
        Anchor(number);
        var monitors = Seat(out var fallback);
        if (here && !Stands(number))
        {
            var keyboard = Home(monitors, fallback, _book.Current);
            if (Home(monitors, fallback, number) != keyboard)
            {
                Log.Information("Workspace", "workspace " + number + " was hidden on another monitor; it comes to monitor " + keyboard);
                _home[number] = monitors[keyboard];
            }
        }

        StandAt(Home(monitors, fallback, number)).Front = number;
        LayoutDirty = true;
        Keyboard(number);
    }

    /// <summary>
    /// Takes <paramref name="id"/> out of the Workspace that holds it and gives it a tile on <paramref name="number"/>,
    /// and writes that Workspace onto the window (ADR 0078). The window is neither shown nor hidden here.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <param name="number">The Workspace it goes to, 1-based.</param>
    internal void Put(long id, int number)
    {
        // A Workspace that did not exist is made on the monitor with the keyboard.
        Anchor(number);
        foreach (var pair in _sessions)
        {
            if (pair.Key != number && pair.Value.Windows.Contains(id))
            {
                pair.Value.Remove(id, Area(pair.Key));
                Sync(pair.Key);
            }
        }

        var destination = Session(number);
        if (!destination.Windows.Contains(id))
        {
            destination.Add(id, Area(number));
        }

        Sync(number);
        _windows.MarkWorkspace(id, number);
    }

    /// <summary>Takes <paramref name="id"/> out of the Workspace that holds it. It is then on none, as a window Winoma has not met.</summary>
    /// <param name="id">Window identity.</param>
    internal void Drop(long id)
    {
        foreach (var pair in _sessions)
        {
            if (pair.Value.Windows.Contains(id))
            {
                pair.Value.Remove(id, Area(pair.Key));
                Sync(pair.Key);
            }
        }
    }

    /// <summary>The Workspace whose tiles hold <paramref name="id"/>.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>The Workspace number, or 0 when no Workspace tiles the window.</returns>
    internal int HomeOf(long id)
    {
        foreach (var pair in _sessions)
        {
            if (pair.Value.Windows.Contains(id))
            {
                return pair.Key;
            }
        }

        return 0;
    }

    /// <summary>True when a Workspace that is not in front holds <paramref name="id"/>.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>False for a window on a Workspace in front, or on none.</returns>
    internal bool OnHiddenWorkspace(long id)
    {
        foreach (var pair in _sessions)
        {
            if (!_book.InFront(pair.Key) && pair.Value.Windows.Contains(id))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Forgets the windows that closed on every Workspace but the one in front, whose own arrange does that, and stores the counts.
    /// An empty Workspace that is not in front stops existing with its last window.
    /// </summary>
    /// <returns>True when a window was forgotten.</returns>
    internal bool PruneClosed()
    {
        var pruned = false;
        foreach (var pair in _sessions)
        {
            if (pair.Key == _book.Current)
            {
                Sync(pair.Key);
                continue;
            }

            // Asked on every arrange. A Workspace with no window has none to ask about.
            if (pair.Value.Windows.Count == 0)
            {
                continue;
            }

            long[]? closed = null;
            var count = 0;
            foreach (var id in pair.Value.Windows)
            {
                if (!_windows.Alive(id))
                {
                    (closed ??= new long[pair.Value.Windows.Count])[count++] = id;
                }
            }

            for (var i = 0; i < count; i++)
            {
                pair.Value.Remove(closed![i], Area(pair.Key));
                pruned = true;
            }

            Sync(pair.Key);
        }

        return pruned;
    }

    /// <summary>
    /// Names the Free windows that stand alone, each with the Workspace it is on. The shell keeps the map and changes it,
    /// and says so with <see cref="Recount"/>.
    /// </summary>
    /// <param name="free">Each such window with its Workspace number.</param>
    internal void HoldFree(IReadOnlyDictionary<long, int> free) => _free = free;

    /// <summary>Stores how many windows each Workspace holds again, after a Free window came, went, or changed its Workspace.</summary>
    internal void Recount()
    {
        for (var number = 1; number <= WorkspaceBook.Maximum; number++)
        {
            if (_sessions.ContainsKey(number) || FreeOn(number) > 0)
            {
                Sync(number);
            }
        }
    }

    private int FreeOn(int number)
    {
        var count = 0;
        if (_free is { Count: > 0 } free)
        {
            foreach (var pair in free)
            {
                count += pair.Value == number ? 1 : 0;
            }
        }

        return count;
    }

    /// <summary>
    /// Stores how many windows <paramref name="number"/> holds, which is what makes it exist while it is not in front:
    /// its tiles, and the Free windows that stand alone on it.
    /// </summary>
    internal void Sync(int number)
    {
        var count = Session(number).Windows.Count + FreeOn(number);
        if (count > 0 && !_home.ContainsKey(number))
        {
            // It holds a window, so it is on a monitor of its own from here on.
            Anchor(number);
        }

        _book.SetOccupancy(number, count);
    }

    private TilingSession Session(int number)
    {
        EnsureSession(number);
        return _sessions[number];
    }

    private void EnsureSession(int number)
    {
        if (!_sessions.ContainsKey(number))
        {
            _sessions[number] = NewSession();
        }
    }

    private TilingSession NewSession()
    {
        var session = new TilingSession();
        session.SetGroupBar(_groupBar);
        session.SetSpacing(_settings.SpacingPixels);
        if (_settings.SpacingEnabled)
        {
            session.ToggleSpacing();
        }

        if (_settings.SquareTileEnabled)
        {
            session.ToggleSquareTile();
        }

        return session;
    }

    /// <summary><see cref="Area(int)"/> for a caller that has already read the monitors and the bar monitor's work area.</summary>
    internal TileRect Area(IReadOnlyList<TileRect> monitors, TileRect fallback, int workspace)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            return fallback;
        }

        return monitors[Home(monitors, fallback, workspace)];
    }

    internal int Monitor(IReadOnlyList<TileRect> monitors, TileRect fallback, int workspace)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        return monitors.Count == 0 ? 0 : Home(monitors, fallback, workspace);
    }

    /// <summary>
    /// The index of the monitor <paramref name="workspace"/> is on, in a list that is not empty.
    /// The list changes order when a monitor is added or removed, so the index is found again on each read.
    /// A workspace with no monitor left where its own stood is on the bar monitor, and keeps the rectangle of its own for when that is back.
    /// The Workspace with the keyboard starts on the bar monitor. Any other that has not been made yet would be made
    /// on the monitor with the keyboard, so it reads as that one.
    /// </summary>
    private int Home(IReadOnlyList<TileRect> monitors, TileRect fallback, int workspace)
    {
        if (_home.TryGetValue(workspace, out var kept))
        {
            var index = Find(monitors, kept);
            if (index < 0)
            {
                return Match(monitors, fallback);
            }

            // The bar's monitor loses and regains the bar's strip, and a monitor can change size where it stands.
            _home[workspace] = monitors[index];
            return index;
        }

        if (workspace != _book.Current)
        {
            return Home(monitors, fallback, _book.Current);
        }

        var first = Match(monitors, fallback);
        _home[workspace] = monitors[first];
        return first;
    }

    /// <summary>The index of the monitor that covers the most of <paramref name="kept"/>, or -1 when none covers any of it.</summary>
    private static int Find(IReadOnlyList<TileRect> monitors, TileRect kept)
    {
        var found = -1;
        long most = 0;
        for (var i = 0; i < monitors.Count; i++)
        {
            var covered = Overlap(kept, monitors[i]);
            if (covered > most)
            {
                most = covered;
                found = i;
            }
        }

        return found;
    }

    private static long Overlap(TileRect kept, TileRect monitor)
    {
        long width = Math.Min(kept.X + kept.Width, monitor.X + monitor.Width) - Math.Max(kept.X, monitor.X);
        long height = Math.Min(kept.Y + kept.Height, monitor.Y + monitor.Height) - Math.Max(kept.Y, monitor.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }

    private static int Match(IReadOnlyList<TileRect> monitors, TileRect fallback)
    {
        for (var i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            if (fallback.X >= monitor.X && fallback.X < monitor.X + monitor.Width
                && fallback.Y >= monitor.Y && fallback.Y < monitor.Y + monitor.Height)
            {
                return i;
            }
        }

        return 0;
    }
}
