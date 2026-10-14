namespace Winland.Core.Shell;

// The desktop as `winoma desktop` prints it.
public sealed partial class ShellController
{
    /// <summary>The desktop as the CLI prints it. The shell adds the open panel, plugin facts, and capture.</summary>
    /// <returns>The facts a chord can change, without tile rectangles.</returns>
    public TilerFacts Facts()
    {
        var world = _windows.World();
        var book = Workspaces;
        var workspaces = new List<WorkspaceFact>(WorkspaceBook.Maximum);
        var windows = new List<WindowFact>();
        var index = new Dictionary<long, int>();

        // Read once for every workspace. Each read walks the monitors.
        var monitors = _windows.Monitors;
        var fallback = _windows.FirstBarArea;
        for (var number = 1; number <= WorkspaceBook.Maximum; number++)
        {
            // The first four, and any other that holds a window or is in front. The numbers may leave holes.
            if (!book.Listed(number))
            {
                continue;
            }

            var session = _travel.At(number);
            var groups = session.Groups();
            workspaces.Add(new WorkspaceFact(
                number,
                book.Occupancy(number),
                session.Layout == LayoutKind.Scrolling ? "scrolling" : "dwindle",
                _travel.Monitor(monitors, fallback, number),
                session.GroupingArmed)
            {
                Front = book.InFront(number),
                Groups = groups.Count == 0 ? null : [.. groups.Select(group => new GroupFact(group.Members, group.Shown))],
            });
            foreach (var id in session.Windows.OrderBy(id => id))
            {
                AddWindow(windows, index, WindowFactFor(id, "workspace", number, session, free: false));
            }
        }

        foreach (var id in _scratchpad.Windows)
        {
            AddWindow(windows, index, WindowFactFor(id, "scratchpad", null, null, free: false));
        }

        foreach (var id in _sticky.Keys.OrderBy(id => id))
        {
            AddWindow(windows, index, WindowFactFor(id, "sticky", null, null, free: false));
        }

        foreach (var id in world.PictureInPicture.OrderBy(id => id))
        {
            AddWindow(windows, index, WindowFactFor(id, "picture-in-picture", null, null, free: false));
        }

        foreach (var id in world.Free)
        {
            var title = _windows.Caption(id);
            if (index.TryGetValue(id, out var at))
            {
                if (title.Length == 0)
                {
                    title = windows[at].Title;
                }

                windows[at] = windows[at] with { Free = true, Title = title };
                continue;
            }

            var session = CurrentSession.Windows.Contains(id) ? CurrentSession : null;

            // A dialog is on its owner's Workspace, which can be the one in front on another monitor.
            AddWindow(windows, index, WindowFactFor(id, "workspace", WorkspaceOf(id) is > 0 and var home ? home : book.Current, session, free: true, title: title));
        }

        // A Free window of a Workspace that is not in front is hidden with it, and the Host lists it nowhere.
        foreach (var pair in _freeHome.OrderBy(pair => pair.Key))
        {
            AddWindow(windows, index, WindowFactFor(pair.Key, "workspace", pair.Value, null, free: true));
        }

        var occupancy = new int[WorkspaceBook.Maximum + 1];
        foreach (var window in windows)
        {
            // A tile, or a Free window that stands alone. A dialog is its owner's, and is not counted beside it.
            if (window.Place == "workspace" && (!window.Free || _freeHome.ContainsKey(window.Hwnd)) && window.Workspace is int home && home >= 1 && home < occupancy.Length)
            {
                occupancy[home]++;
            }
        }

        for (var i = 0; i < workspaces.Count; i++)
        {
            workspaces[i] = workspaces[i] with { Occupancy = occupancy[workspaces[i].Number] };
        }

        return new TilerFacts(
            workspaces,
            windows,
            ActiveWindow(),
            _settings.SpacingEnabled,
            _settings.IdleLockEnabled,
            _scratchpad.Open,
            Switching,
            BarFacts(),
            _travel.Monitor(monitors, fallback, book.Current),
            _settings.SquareTileEnabled,
            _settings.FontFace);
    }

    /// <summary>
    /// Tiled windows on every workspace, as the bar's workspace icons list them: the titled tiles on the glass, then the windows
    /// each Workspace holds that the census does not list. Those are the windows Winoma hid, and a window fullscreen in its own app,
    /// which has no caption to be listed by (ADR 0076). A window is listed while it is alive and has a title.
    /// </summary>
    /// <returns>The census's tiles when it lists every window, with no list made.</returns>
    public IReadOnlyList<TrackedWindow> WorkspaceWindows()
    {
        var open = _windows.World().Tiles;
        List<TrackedWindow>? windows = null;
        for (var i = 0; i < open.Count; i++)
        {
            if (open[i].Title.Length == 0)
            {
                windows ??= [.. open.Take(i)];
            }
            else
            {
                windows?.Add(open[i]);
            }
        }

        foreach (var session in _travel.Sessions)
        {
            foreach (var id in session.Windows)
            {
                // A few dozen windows at most, so a scan costs less than a set built on every recount.
                if (Lists(open, id) || IsOverlay(id) || !_windows.Alive(id))
                {
                    continue;
                }

                var title = _windows.Caption(id);
                if (title.Length == 0)
                {
                    continue;
                }

                windows ??= [.. open];
                windows.Add(new TrackedWindow(id, title));
            }
        }

        return windows ?? open;
    }

    private static bool Lists(IReadOnlyList<TrackedWindow> windows, long id)
    {
        for (var i = 0; i < windows.Count; i++)
        {
            if (windows[i].Id == id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The workspace <paramref name="id"/> is on: the one whose tiles hold it, the one a Free window that stands alone opened on,
    /// or the one its owner is on, for a dialog a window owns. Only Winoma knows. The bar asks here for each window it draws an icon for,
    /// and the Menu before it gives a window the keyboard.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <returns>The workspace number, or 0 when the window is on none: a Sticky window, a scratchpad window, or one Winoma has not met.</returns>
    public int WorkspaceOf(long id)
    {
        // A dialog can own a dialog. The Host is asked only about a window that is neither a tile nor a Free window that stands alone.
        for (var depth = 0; depth < 8 && id != 0; depth++)
        {
            if (_freeHome.TryGetValue(id, out var home))
            {
                return home;
            }

            home = _travel.HomeOf(id);
            if (home != 0 || IsOverlay(id))
            {
                return home;
            }

            id = _windows.Owner(id);
        }

        return 0;
    }

    private WindowFact WindowFactFor(long id, string place, int? workspace, TilingSession? session, bool free, string? title = null) =>
        new(
            id,
            title ?? _windows.Caption(id),
            place,
            workspace,
            session?.Floated(id) ?? place == "sticky",
            session is not null && session.Fullscreen == id,
            session?.Grouped(id) ?? false,
            free,
            session?.Wide(id) ?? false,
            session?.Pseudo(id) ?? false);

    private static void AddWindow(List<WindowFact> windows, Dictionary<long, int> index, WindowFact fact)
    {
        if (!index.TryAdd(fact.Hwnd, windows.Count))
        {
            return;
        }

        windows.Add(fact);
    }
}
