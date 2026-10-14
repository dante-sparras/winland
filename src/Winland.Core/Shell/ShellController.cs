namespace Winland.Core.Shell;

/// <summary>
/// Applies chords to the Workspaces and the tiling session of the one with the keyboard. The Workspaces are Winoma's own (ADR 0003):
/// this shows the windows of the one in front on each monitor (ADR 0079) and hides the windows of every other.
/// </summary>
public sealed partial class ShellController : IWinlandDispatch
{
    private readonly IWindowGateway _windows;
    private readonly IShellActions _actions;
    private readonly ITileSettings _settings;
    private readonly IChordTable _chords;
    private readonly IBindTable _binds;
    private readonly ShellGlobalSink _commandSink;
    private IGlobalBindSink _sink;
    private readonly WorkspaceTravel _travel;
    private readonly Scratchpad _scratchpad = new();
    private readonly Dictionary<long, TileRect> _sticky = [];
    private readonly HashSet<long> _recorded = [];
    private long? _raised;
    private long? _followed;
    private readonly Dictionary<long, long> _closing = [];

    // Windows fullscreen in their own app that were put over the monitor for it. Each is answered once,
    // so one the person tiled again with the fullscreen chord stays on its tile (ADR 0076).
    private readonly HashSet<long> _inApp = [];

    // The window the tiled fullscreen chord asked to go fullscreen, and when the asking is given up on.
    // Its fullscreen is answered by leaving it on its tile.
    private (long Id, long Until)? _tiledAsk;

    // How long an app has to answer the tiled fullscreen chord, in milliseconds.
    private const long TiledAskMs = 3000;
    private readonly FrameSample? _pace;
    private readonly Func<long> _clock;
    private readonly Func<bool> _superHeld;
    private readonly Func<bool> _surfaceHeld;
    private readonly IDefaultBrowser? _defaultBrowser;

    /// <summary>Where the files chord opens Explorer. The shell wires this. The window manager does not open folders.</summary>
    internal static Func<FrontWindow, string> FolderFor { get; set; } = static _ => "";

    /// <summary>The private-window flag for a browser program. The shell wires this.</summary>
    internal static Func<string, string> PrivateFlag { get; set; } = static _ => "";
    private bool _focusAfterSurface;
    private readonly KeyboardHandoff _handoff = new();
    private int _arrangeDepth;

    // True when the arrange in progress has to run once more: a window it could not hide is on the glass without a tile.
    private bool _again;

    // True when the arrange in progress forgot a hidden window that closed.
    private bool _forgot;

    // The windows of the Workspace in front that a switch showed and the Host has not listed since. Until it lists one,
    // nothing says the show worked, so the window keeps its tile, stays on the record, and is asked to show again.
    private readonly HashSet<long> _arriving = [];

    // The windows one switch shows, and then the ones it hides. A switch does not nest.
    private readonly List<long> _batch = [];

    // The Workspace of each Free window that no window owns. It takes no tile, and it is hidden and shown with that Workspace.
    // A Free window another window owns is not kept here: Windows cloaks an owned window with its owner.
    private readonly Dictionary<long, int> _freeHome = [];
    private readonly List<long> _freeGone = [];

    // What the last placement was computed from. An arrange that finds all of it again, with every tile still on its tile,
    // places nothing: most window events are a tooltip, a menu, or a tray flyout, and change no tile.
    private TilingSession? _placedSession;
    private IReadOnlyList<Placement>? _placedTiles;
    private TileRect _placedArea;
    private TileRect _placedScreen;
    private List<long> _placedMark = [];
    private List<long> _mark = [];

    // Kept between arranges so one allocates nothing for them. Only an arrange reads them, and an arrange does not nest.
    private readonly HashSet<long> _liveIds = [];
    private readonly List<long> _gone = [];
    private readonly List<long> _before = [];
    private readonly List<long> _hidden = [];

    // The floated and Sticky windows the gateway was last told a Carry may take, and the list the next telling is gathered in.
    private List<long> _floatedTold = [];
    private List<long> _floated = [];

    /// <summary>Wires the book, the tiler, and the window gateway.</summary>
    /// <param name="windows">The Host's windows: which are on the glass, and the ways to show, hide, place, and focus one.</param>
    /// <param name="actions">Commands that leave the tiler.</param>
    /// <param name="settings">Tiling settings. The shell's settings implement this.</param>
    /// <param name="hooks">User scripts. Null starts nothing. The window manager fires <c>workspace-changed</c> only.</param>
    /// <param name="pace">Five-second frame sample. Null records nothing.</param>
    /// <param name="clock">Millisecond clock for a window asked to close and for <paramref name="pace"/>. Null reads <see cref="Environment.TickCount64"/>.</param>
    /// <param name="superHeld">True while the chord ledger records a Super finger. Null is never held.</param>
    /// <param name="chords">Chord table. Null uses the built-in map.</param>
    /// <param name="surfaceHeld">True while an Winoma panel has the keyboard. A workspace switch then leaves the keyboard on it. Null is never held.</param>
    /// <param name="defaultBrowser">Names the browser Windows opens a link with, for a private window when settings name none. Null names none.</param>
    public ShellController(
        IWindowGateway windows,
        IShellActions actions,
        ITileSettings settings,
        IWorkspaceHook? hooks = null,
        FrameSample? pace = null,
        Func<long>? clock = null,
        Func<bool>? superHeld = null,
        IChordTable? chords = null,
        Func<bool>? surfaceHeld = null,
        IDefaultBrowser? defaultBrowser = null)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(settings);
        _windows = windows;
        _actions = actions;
        _settings = settings;
        _chords = chords ?? new ChordMap();
        _binds = new ShellBinds(_chords);
        _commandSink = new ShellGlobalSink(Execute);
        _sink = _commandSink;
        _travel = new WorkspaceTravel(windows, settings, hooks ?? NoHooks.Instance);
        _travel.HoldFree(_freeHome);
        _pace = pace;
        _clock = clock ?? DefaultClock;
        _superHeld = superHeld ?? (() => false);
        _surfaceHeld = surfaceHeld ?? (() => false);
        _defaultBrowser = defaultBrowser;
    }

    /// <summary>
    /// How long a window that was asked to close is left out of the tiles, in milliseconds.
    /// It is still listed until its program lets go of it. One that is open after this refused to close, and takes a tile again.
    /// </summary>
    public const long ClosingGraceMs = 2000;

    /// <summary>Raised after the windows of the workspace in front were placed. Winland draws the group strips then.</summary>
    public event Action? Placed;

    /// <summary>Sets the height of the strip a group keeps free along the top of its tile, in pixels, on every workspace. Zero keeps none.</summary>
    /// <param name="pixels">The strip's height in pixels.</param>
    public void SetGroupBar(int pixels) => _travel.SetGroupBar(pixels);

    /// <summary>
    /// The strip of each group on the workspace in front, and of the last tile when the windows that do not fit share it.
    /// None while two or more scratchpad windows cover the tiles. Under one, a centered panel, the strips stay:
    /// the desktop draws a strip under every other window, so the panel is over the part of a strip it crosses.
    /// </summary>
    /// <returns>One strip per group, and one for a shared last tile.</returns>
    public IReadOnlyList<GroupStrip> GroupStrips()
    {
        if (Workspaces.SeveralInFront)
        {
            return StripsOfEveryFront();
        }

        // Asked after every placement. A workspace without a group has none, and the monitors are not read for it.
        if (_scratchpad.CoversTiles || !CurrentSession.HasStrips)
        {
            return [];
        }

        HomeRects(out var area, out var screen);
        return CurrentSession.Strips(area, screen);
    }

    /// <summary>The strips of each Workspace in front, but for the one two or more scratchpad windows cover.</summary>
    private IReadOnlyList<GroupStrip> StripsOfEveryFront()
    {
        List<GroupStrip>? strips = null;
        foreach (var front in Fronts())
        {
            if (front.Session.HasStrips && !(_scratchpad.CoversTiles && front.Number == _scratchpadOver))
            {
                (strips ??= []).AddRange(front.Session.Strips(front.Area, front.Screen));
            }
        }

        return strips is null ? [] : strips;
    }

    /// <summary>True when <paramref name="id"/> is in the scratchpad, shown or hidden.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>False for a window on a workspace, a sticky window, or one Winoma does not know.</returns>
    public bool InScratchpad(long id) => _scratchpad.Contains(id);

    /// <summary>True while the scratchpad is on screen.</summary>
    public bool ScratchpadOpen => _scratchpad.Open;

    /// <summary>Shows <paramref name="id"/> in its group and gives it the keyboard, as a click on its tab in the group's strip asks.</summary>
    /// <param name="id">A window on a workspace in front. Any other is left alone.</param>
    public void ShowGroupMember(long id)
    {
        if (!CurrentSession.Windows.Contains(id))
        {
            // On the Workspace in front on another monitor: the keyboard goes there with the click.
            var home = _travel.HomeOf(id);
            if (home == 0 || !InFront(home))
            {
                return;
            }

            _followed = id;
            Scene++;
            Travel(home);
        }

        CurrentSession.Focus(id);
        FocusCurrent();
        Arrange();
    }

    /// <summary>The workspace book.</summary>
    public WorkspaceBook Workspaces => _travel.Book;

    /// <summary>The session for the current workspace: the one with the keyboard.</summary>
    public TilingSession CurrentSession => _travel.Current;

    private bool InFront(int number) => Workspaces.InFront(number);

    /// <summary>A Workspace in front, with the work area and the whole rectangle of the monitor it is on.</summary>
    private readonly record struct Front(int Number, TilingSession Session, TileRect Area, TileRect Screen);

    // Kept between calls so an arrange allocates nothing for them. A caller is done with the list before the next one asks.
    private readonly List<Front> _fronts = [];
    private readonly List<int> _frontNumbers = [];

    /// <summary>
    /// The Workspace in front on each monitor, from one read of the monitors. The one with the keyboard is last:
    /// a placement names every tile on the glass, and the last one made is the one the Host keeps.
    /// </summary>
    private List<Front> Fronts()
    {
        var fronts = _fronts;
        fronts.Clear();
        _travel.Fronts(_frontNumbers);
        var monitors = _windows.Monitors;
        var fallback = _windows.FirstBarArea;
        var screens = _windows.Screens;
        var current = Workspaces.Current;
        foreach (var number in _frontNumbers)
        {
            if (number != current)
            {
                Rects(monitors, fallback, screens, number, out var area, out var screen);
                fronts.Add(new Front(number, _travel.At(number), area, screen));
            }
        }

        Rects(monitors, fallback, screens, current, out var here, out var whole);
        fronts.Add(new Front(current, CurrentSession, here, whole));
        return fronts;
    }

    private void Rects(IReadOnlyList<TileRect> monitors, TileRect fallback, IReadOnlyList<TileRect> screens, int number, out TileRect area, out TileRect screen)
    {
        area = _travel.Area(monitors, fallback, number);
        if (screens.Count == 0)
        {
            screen = area;
            return;
        }

        var index = _travel.Monitor(monitors.Count > 0 ? monitors : screens, fallback, number);
        screen = index >= 0 && index < screens.Count ? screens[index] : screens[0];
    }

    /// <summary>Changes when the tiled set or the current workspace changes. The bar paints on a new value.</summary>
    public int Scene { get; private set; }

    /// <summary>True while a workspace change is moving windows. Show and hide events in that span are the change itself.</summary>
    public bool Switching { get; private set; }

    /// <summary>
    /// True when the fullscreen window of a Workspace in front is on <paramref name="screen"/>. One on another monitor leaves that monitor's Bar alone.
    /// </summary>
    /// <param name="screen">One monitor, whole: the one a Bar is on.</param>
    /// <returns>True when that monitor's Workspace in front has a fullscreen window.</returns>
    public bool FullscreenOn(TileRect screen)
    {
        if (!Workspaces.SeveralInFront)
        {
            return CurrentSession.Fullscreen is not null && SameScreen(HomeScreen(), screen);
        }

        // Asked for each Bar on every turn of the bar loop. The monitors are read only while some window is fullscreen.
        var any = false;
        foreach (var pair in _travel.Numbered)
        {
            any |= pair.Value.Fullscreen is not null && InFront(pair.Key);
        }

        if (!any)
        {
            return false;
        }

        foreach (var front in Fronts())
        {
            if (front.Session.Fullscreen is not null && SameScreen(front.Screen, screen))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the Bar on <paramref name="screen"/> should leave so a window can fill that monitor.</summary>
    /// <param name="screen">One monitor, whole: the one the Bar is on.</param>
    /// <returns>True when a fullscreen window or a window that fills the monitor is on it.</returns>
    public bool BarYields(TileRect screen) =>
        !BarLayer.Decide(FullscreenOn(screen), _windows.WindowFills(screen), topmostCrossesBar: false).BarInFront;

    /// <summary>The Bars, one for each monitor in the Host's order, as <c>winoma desktop</c> lists them (ADR 0080).</summary>
    private List<BarFact> BarFacts()
    {
        var screens = _windows.Screens;
        var bars = new List<BarFact>(Math.Max(1, screens.Count));
        if (screens.Count == 0)
        {
            // A Host that lists no monitors has one, and its Bar.
            bars.Add(new BarFact(0, _settings.BarVisible, BarYields(HomeScreen())));
            return bars;
        }

        for (var i = 0; i < screens.Count; i++)
        {
            bars.Add(new BarFact(i, _settings.BarVisible, BarYields(screens[i])));
        }

        return bars;
    }

    /// <summary>The chord table this shell is using.</summary>
    public IChordTable Chords => _chords;

    /// <summary>
    /// Follows a window Winoma keeps hidden that showed itself (ADR 0038): it took the keyboard, as a window picked in Alt+Tab,
    /// a Notification's window, or a browser handed a link does, or its program put it back on the glass.
    /// A window of another Workspace is followed there and has the keyboard. A scratchpad window shows the scratchpad,
    /// and a Group member behind the one shown is shown in its Group. Winoma decides before the window is drawn: it is still hidden when it is found.
    /// A keyboard Windows handed on by itself is not a window showing itself: when the window that had the keyboard is gone, Windows gives
    /// it to the next window in its own order, hidden or not. That keyboard goes back to the Workspace in front.
    /// </summary>
    /// <remarks>
    /// With a Workspace in front on each monitor, a window on another monitor that took the keyboard is on the glass already:
    /// a click, the pointer, or its own program gave it the keyboard. The keyboard is then on that monitor, and its Workspace is the one with the keyboard.
    /// </remarks>
    /// <returns>True when another Workspace came in front or took the keyboard, and the bar should paint.</returns>
    public bool FollowShown()
    {
        if (Switching || _arrangeDepth > 0 || !_marksAdopted)
        {
            return false;
        }

        ForgetKeptKeyboard();
        if (Workspaces.SeveralInFront && FollowKeyboard())
        {
            FlushLayout();
            return true;
        }

        if (_windows.Foreground() is long front && KeepsHidden(front))
        {
            // Winoma hid it with the keyboard on it, and the keyboard waits there for Super to go up (SettleKeyboard). It did not show itself.
            if (front == _keptKeyboard || KeepFromLaunched(front))
            {
                return false;
            }

            if (Fell(front))
            {
                // Once for each window the keyboard falls to. While Super is held it stays there until Super goes up (ADR 0020).
                if (_fellTo != front)
                {
                    _fellTo = front;
                    Log.Information("Workspace", "the keyboard fell to hidden window " + front + "; workspace " + Workspaces.Current + " takes it back");

                    // The window that went away may have been this Workspace's focused tile. The arrange drops it first.
                    Arrange();
                    FocusWorkspace();
                }

                return false;
            }

            _fellTo = null;
            return Follow(front);
        }

        _fellTo = null;
        var world = _windows.World();
        var tiles = world.Tiles;
        for (var i = 0; i < tiles.Count; i++)
        {
            if (_travel.OnHiddenWorkspace(tiles[i].Id) && _windows.Shown(tiles[i].Id))
            {
                return Follow(tiles[i].Id);
            }
        }

        if (_freeHome.Count > 0)
        {
            var alone = world.FreeAlone;
            for (var i = 0; i < alone.Count; i++)
            {
                if (_freeHome.TryGetValue(alone[i], out var home) && !InFront(home) && _windows.Shown(alone[i]))
                {
                    return Follow(alone[i]);
                }
            }
        }

        return false;
    }

    private readonly LaunchAsks _launches = new();

    // The window a start sent to a Workspace the keyboard had left, until the keyboard has been taken off it once or the time is up.
    private long? _launched;
    private long _launchedUntil;

    /// <summary>
    /// How long a window that opened on the Workspace it was started from is kept from taking the keyboard there, in milliseconds.
    /// A new window takes the keyboard as it is drawn. After this, the keyboard on it is a person's pick, and is followed.
    /// </summary>
    public const long LaunchedKeyboardMs = 2000;

    /// <summary>
    /// A program is being started. The window it opens joins the Workspace that has the keyboard now, wherever the keyboard is
    /// when the window opens (ADR 0038). The Host calls this as it starts a program for the Menu.
    /// </summary>
    public void NoteLaunch() => _launches.Note(Workspaces.Current, _clock());

    /// <summary>
    /// A program made a window and has not shown it yet. With several monitors it is moved onto the monitor of the Workspace it will join,
    /// so it is first drawn there: the Workspace it was started from when that one is in front, and otherwise the one with the keyboard
    /// (ADR 0038). The arrange that follows the show gives it its tile. A window a hidden Workspace will take is left where it is.
    /// </summary>
    /// <param name="id">The window that was made.</param>
    /// <returns>True when the window was moved.</returns>
    public bool Seat(long id)
    {
        if (Switching || _arrangeDepth > 0 || !_marksAdopted || !Workspaces.SeveralInFront || _windows.Unshown(id) is not TileRect made)
        {
            return false;
        }

        var number = Workspaces.Current;
        if (_launches.Peek(_clock()) is var asked && asked != 0 && asked != number)
        {
            if (!InFront(asked))
            {
                return false;
            }

            number = asked;
        }

        var screens = _windows.Screens;
        Rects(_windows.Monitors, _windows.FirstBarArea, screens, number, out var area, out var screen);

        // The tile the arrange will give it, so the window is drawn on it from its first frame.
        if (_travel.At(number).Preview(id, area, screen) is TileRect tile)
        {
            Log.Information("Workspace", "window " + id + " is put on its tile on workspace " + number + " before it is shown");
            _windows.Seat(id, tile, tile: true);
            return true;
        }

        if (OpeningSeat.On(made, screens, screen) is not TileRect seat)
        {
            return false;
        }

        Log.Information("Workspace", "window " + id + " was made on another monitor; it is moved to the monitor of workspace " + number + " before it is shown");
        _windows.Seat(id, seat, tile: false);
        return true;
    }

    /// <summary>
    /// Takes the keyboard off a window a start sent to another Workspace, once: the person went elsewhere while its program started.
    /// </summary>
    /// <returns>True when <paramref name="front"/> was that window, and the keyboard is not to follow it.</returns>
    private bool KeepFromLaunched(long front)
    {
        if (_launched != front)
        {
            return false;
        }

        _launched = null;
        if (_clock() > _launchedUntil)
        {
            return false;
        }

        Log.Information("Workspace", "window " + front + " opened on the workspace it was started from; workspace " + Workspaces.Current + " keeps the keyboard");
        var shown = !KeepsHidden(front);
        FocusWorkspace(shown ? front : null);
        if (shown && _windows.Foreground() == front)
        {
            // Nothing could take it, as while Super is held. It waits there, and is not followed.
            _keyboardLeft = front;
        }

        return true;
    }

    // The window that kept the keyboard when a chord took the keyboard to another monitor: no window there took it, or Super held it back.
    // It is on the glass, on the Workspace in front on its own monitor, and it did not take the keyboard. Null once the keyboard has left it.
    private long? _keyboardLeft;

    /// <summary>
    /// Makes the Workspace of the window that has the keyboard the one with the keyboard, when that window is on the glass
    /// on another monitor. Nothing is shown or hidden: both Workspaces are in front. The Workspace Hook fires, since the Workspace
    /// with the keyboard changed.
    /// </summary>
    /// <returns>True when the keyboard is on another Workspace now.</returns>
    private bool FollowKeyboard()
    {
        var foreground = _windows.Foreground();
        if (_keyboardLeft is long left)
        {
            if (foreground == left)
            {
                return false;
            }

            _keyboardLeft = null;
        }

        if (foreground is not long front || FrontWithoutKeyboard(front) is not (var home and not 0))
        {
            return false;
        }

        if (KeepFromLaunched(front))
        {
            return false;
        }

        // The window that had the keyboard closed, and Windows handed the keyboard on by itself, to the next window in its own order.
        // Nobody asked for the other monitor, so the keyboard goes back to the Workspace that had it.
        if (_windows.ForegroundLeft() is long gone && gone != front && (!_windows.Alive(gone) || _closing.ContainsKey(gone)))
        {
            Log.Information("Workspace", "the keyboard fell to window " + front + " on another monitor; workspace " + Workspaces.Current + " takes it back");

            // The window that closed may have been this Workspace's focused tile. The arrange drops it first.
            Arrange();
            FocusWorkspace(front);
            if (_windows.Foreground() == front)
            {
                // Nothing could take it, as while Super is held. It waits there, and is not followed.
                _keyboardLeft = front;
            }

            return false;
        }

        KeyboardTo(home, front);
        return true;
    }

    /// <summary>
    /// The keyboard is on <paramref name="window"/>, on the glass on another monitor, so <paramref name="home"/> is the Workspace with the keyboard.
    /// Nothing is shown or hidden: both Workspaces are in front.
    /// </summary>
    private void KeyboardTo(int home, long window)
    {
        Log.Information("Workspace", "window " + window + " on workspace " + home + " has the keyboard; the keyboard is on its monitor");
        _keyboardLeft = null;
        _placedSession = null;
        _windows.ResetFocusRefusal();
        _handoff.Clear();
        _travel.Go(home);

        // A dialog has no tile. The tile that owns it is the one the Chords act on while it has the keyboard.
        var tile = window;
        for (var depth = 0; depth < 8 && tile != 0 && !CurrentSession.Windows.Contains(tile); depth++)
        {
            tile = _windows.Owner(tile);
        }

        if (tile != 0)
        {
            CurrentSession.Focus(tile);
        }

        Scene++;
    }

    /// <summary>The Workspace <paramref name="id"/> is drawn with, when it is one in front that does not have the keyboard. Zero otherwise.</summary>
    private int FrontWithoutKeyboard(long id)
    {
        if (!Workspaces.SeveralInFront || _sticky.ContainsKey(id))
        {
            return 0;
        }

        // The scratchpad is drawn over one Workspace, and its windows are on no Workspace of their own.
        var home = _scratchpad.Contains(id) ? (_scratchpad.Open ? _scratchpadOver : 0) : WorkspaceOf(id);
        return home == 0 || home == Workspaces.Current || !InFront(home) || _travel.At(home).Tucked(id) ? 0 : home;
    }

    /// <summary>
    /// Hover focus (ADR 0034): the pointer moved, and the tile under it takes the keyboard unless something holds the keyboard where it is
    /// (<see cref="HoverChoice"/>). A tile of the Workspace in front on another monitor takes the keyboard to that monitor (ADR 0079).
    /// The desktop calls this only after the mouse moved, so a pointer that rests on one monitor never takes the keyboard back
    /// from the monitor a Chord sent it to.
    /// </summary>
    /// <param name="tile">The tile whose place holds the pointer, or null.</param>
    /// <param name="drawn">The top-level window drawn at the pointer, or null.</param>
    /// <param name="buttonHeld">True while a mouse button is down.</param>
    /// <param name="surfaceHeld">True while an Winoma panel has the keyboard.</param>
    /// <returns>True when the keyboard went to another Workspace, and the bar should paint.</returns>
    public bool Hover(long? tile, long? drawn, bool buttonHeld, bool surfaceHeld)
    {
        if (Switching || _arrangeDepth > 0)
        {
            return false;
        }

        // A tile on another monitor is compared with the focused tile of its own Workspace only once the keyboard is there.
        var facts = new HoverFacts(tile, drawn, CurrentSession.Focused, buttonHeld, surfaceHeld);
        if (HoverChoice.Target(facts) is not long under || !_windows.Activate(under))
        {
            return false;
        }

        if (FrontWithoutKeyboard(under) is var home && home != 0)
        {
            KeyboardTo(home, under);
            FlushLayout();
            return true;
        }

        CurrentSession.Focus(under);
        return false;
    }

    /// <summary>
    /// The left button went down on a monitor, and that monitor has the keyboard from then on (ADR 0079).
    /// On a tile of the Workspace in front there, that Workspace has the keyboard, and Windows gives the pressed window the keyboard itself.
    /// On the bare desktop there, the keyboard goes to that Workspace as a Chord sends it: to its focused window, or to no window when it is empty.
    /// A press on anything else moves nothing here: a dialog, a Sticky window, or a panel takes the keyboard itself,
    /// and <see cref="FollowShown"/> reads where it belongs.
    /// </summary>
    /// <param name="x">Where the pointer is, in physical pixels.</param>
    /// <param name="y">Where the pointer is, in physical pixels.</param>
    /// <param name="tile">The tile drawn at the pointer, or null when none is, or another window covers it there.</param>
    /// <param name="bare">True when no window but the desktop's own is drawn at the pointer.</param>
    /// <returns>True when the keyboard went to another Workspace, and the bar should paint.</returns>
    public bool Press(int x, int y, long? tile, bool bare)
    {
        if (Switching || _arrangeDepth > 0 || !_marksAdopted || !Workspaces.SeveralInFront || MonitorsChanged)
        {
            return false;
        }

        if (tile is long under)
        {
            if (FrontWithoutKeyboard(under) is var home && home != 0)
            {
                KeyboardTo(home, under);
                FlushLayout();
                return true;
            }

            return false;
        }

        return bare && KeyboardToDesktop(x, y, "a press on");
    }

    /// <summary>
    /// Hover focus on the bare desktop (ADR 0034): the pointer moved onto the desktop of another monitor, where no window is drawn under it,
    /// and that monitor has the keyboard from then on (ADR 0079). The keyboard goes to the Workspace in front there as a Chord sends it:
    /// to its focused window, or to no window when it is empty. A held button and an open panel keep the keyboard where it is,
    /// as they do for <see cref="Hover"/>. The desktop calls this only after the mouse moved.
    /// </summary>
    /// <param name="x">Where the pointer is, in physical pixels.</param>
    /// <param name="y">Where the pointer is, in physical pixels.</param>
    /// <param name="buttonHeld">True while a mouse button is down.</param>
    /// <param name="surfaceHeld">True while an Winoma panel has the keyboard.</param>
    /// <returns>True when the keyboard went to another Workspace, and the bar should paint.</returns>
    public bool HoverDesktop(int x, int y, bool buttonHeld, bool surfaceHeld)
    {
        if (buttonHeld || surfaceHeld || Switching || _arrangeDepth > 0 || !_marksAdopted || !Workspaces.SeveralInFront || MonitorsChanged)
        {
            return false;
        }

        return KeyboardToDesktop(x, y, "the pointer moved onto");
    }

    /// <summary>Sends the keyboard to the Workspace in front on the monitor that holds the point, when it is not there already.</summary>
    private bool KeyboardToDesktop(int x, int y, string why)
    {
        if (_travel.FrontAt(x, y) is not (var target and not 0) || target == Workspaces.Current)
        {
            return false;
        }

        Log.Information("Workspace", why + " the desktop of another monitor; the keyboard goes to workspace " + target);
        Scene++;
        Travel(target);
        FlushLayout();
        return true;
    }

    // The hidden window the keyboard last fell to when the window that had it went away.
    private long? _fellTo;

    // The window Winoma hid while it had the keyboard, because nothing could take the keyboard yet. Null once the keyboard has left it.
    private long? _keptKeyboard;

    /// <summary>Notes the window among <paramref name="hidden"/>, which were just hidden, that still has the keyboard, when one does.</summary>
    private void NoteKeptKeyboard(IReadOnlyList<long> hidden)
    {
        if (_windows.Foreground() is long front && hidden.Contains(front) && KeepsHidden(front))
        {
            _keptKeyboard = front;
        }
    }

    private void ForgetKeptKeyboard()
    {
        if (_keptKeyboard is long kept && _windows.Foreground() != kept)
        {
            _keptKeyboard = null;
        }
    }

    /// <summary>
    /// How long after one of Winoma's panels closed the keyboard's next move is taken for that panel handing it on, in milliseconds.
    /// A panel slides away before it hides.
    /// </summary>
    public const long HandoverMs = 1500;

    // When a panel last closed. Far enough back that it does not count at the start.
    private long _surfaceClosedAt = long.MinValue / 2;

    /// <summary>
    /// True when the keyboard fell to <paramref name="front"/>: the window that had it was already off the glass as the keyboard moved,
    /// and it was one whose leaving hands the keyboard on. That is a tile or an overlay of the Workspace in front, a window that closed,
    /// and a panel of Winoma's that just closed. Windows' own switcher and Start leave the glass too, after the person picked a window in them:
    /// they are none of these, so the window picked there is followed.
    /// </summary>
    private bool Fell(long front)
    {
        if (_windows.ForegroundLeft() is not long left || left == front)
        {
            return false;
        }

        return CurrentSession.Windows.Contains(left)
            || (Workspaces.SeveralInFront && InFront(_travel.HomeOf(left)))
            || IsOverlay(left)
            || _closing.ContainsKey(left)
            || !_windows.Alive(left)
            || _clock() - _surfaceClosedAt < HandoverMs;
    }

    /// <summary>True when Winoma keeps <paramref name="id"/> off the glass: a window of another Workspace, of the closed scratchpad, or behind the member its Group shows.</summary>
    private bool KeepsHidden(long id)
    {
        if (_scratchpad.Contains(id))
        {
            return !_scratchpad.Open;
        }

        if (_freeHome.TryGetValue(id, out var free))
        {
            return !InFront(free);
        }

        var home = _travel.HomeOf(id);
        return home != 0 && (!InFront(home) || _travel.At(home).Tucked(id));
    }

    /// <summary>Goes to where <paramref name="id"/> is kept, and shows it there with the keyboard on it.</summary>
    /// <returns>True when another Workspace came in front.</returns>
    private bool Follow(long id)
    {
        if (_scratchpad.Contains(id))
        {
            Log.Information("Shell", "scratchpad window " + id + " showed itself; the scratchpad is shown");
            _scratchpad.Toggle();
            _scratchpadOver = Workspaces.Current;
            _raised = id;
            Arrange();
            return false;
        }

        var home = _freeHome.TryGetValue(id, out var free) ? free : _travel.HomeOf(id);
        if (home == 0)
        {
            return false;
        }

        if (free != 0)
        {
            Log.Information("Workspace", "free window " + id + " showed itself; following it to workspace " + home);
            _followed = id;
            Scene++;
            Travel(home);
            FlushLayout();
            return true;
        }

        // In a Group, it becomes the member shown.
        _travel.At(home).Focus(id);
        if (home == Workspaces.Current)
        {
            Arrange();
            return false;
        }

        if (InFront(home))
        {
            // A Group member on another monitor. Its Workspace is in front already, and the keyboard goes to it.
            Log.Information("Workspace", "window " + id + " showed itself in its group on workspace " + home);
            _followed = id;
            Scene++;
            Travel(home);
            FlushLayout();
            return true;
        }

        Log.Information("Workspace", "window " + id + " showed itself; following it to workspace " + home);
        _followed = id;
        Scene++;
        Travel(home);
        FlushLayout();
        return true;
    }

    /// <summary>Jumps to a workspace the way Super and that number does.</summary>
    public void JumpWorkspace(int number) => Execute(new ShellCommand.JumpWorkspace(number));

    /// <summary>
    /// The sink a global bind reaches when the desktop has not installed its own.
    /// A plugin command and a host command run here. <c>winoma:escape</c> does not: the desktop's sink takes that.
    /// </summary>
    public IGlobalBindSink CommandSink => _commandSink;

    /// <summary>Sends later global binds to <paramref name="sink"/>.</summary>
    /// <param name="sink">The desktop's sink. A local bind still runs in this shell.</param>
    public void UseGlobalSink(IGlobalBindSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

    /// <summary>Handles a key event. Returns true when the bind table owns the chord. A held chord delivers both edges.</summary>
    /// <param name="chord">The chord.</param>
    /// <param name="down">True when the key went down.</param>
    /// <returns>True when the chord was owned.</returns>
    public bool HandleKey(Chord chord, bool down) => BindDispatch.Deliver(_binds, _sink, this, chord, down);

    /// <inheritdoc />
    public void Run(WinlandCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Execute(WinlandShell.ToShell(command));
    }

    /// <summary>
    /// Tiles once after a burst of Workspace changes. A switch shows and places the windows Winoma holds for the Workspace (<see cref="Switch"/>)
    /// and moves the keyboard. This reads the Host after it, so a window that opened or closed meanwhile is found.
    /// </summary>
    public void FlushLayout()
    {
        if (!_travel.LayoutDirty)
        {
            return;
        }

        _travel.LayoutDirty = false;
        Arrange(immediate: true);
    }

    /// <summary>
    /// Puts every window Winoma hid back on the glass: the windows of each Workspace that is not in front, the scratchpad's,
    /// and each inactive group member. The desktop calls this as it is asked to Stop. Each window keeps its mark, so the next
    /// desktop puts it back (ADR 0078). A window Explorer did not uncloak stays on the record, and the log says so:
    /// the watcher shows whatever the record still names once this desktop has exited (ADR 0059).
    /// </summary>
    public void ShowHidden()
    {
        _placedSession = null;
        var hidden = new List<long>(_scratchpad.Windows);
        foreach (var numbered in _travel.Numbered)
        {
            var session = numbered.Value;
            var front = InFront(numbered.Key);
            foreach (var id in session.Windows)
            {
                if (!front || session.Tucked(id) || _arriving.Contains(id))
                {
                    hidden.Add(id);
                }
            }
        }

        foreach (var pair in _freeHome)
        {
            if (!InFront(pair.Value) || _arriving.Contains(pair.Key))
            {
                hidden.Add(pair.Key);
            }
        }

        // One call for all of them: they come up in one frame.
        _windows.ShowAllAndWait(hidden);

        var recorded = _recorded.Count;
        _recorded.Clear();
        foreach (var id in hidden)
        {
            if (_windows.HiddenByCloak(id))
            {
                _recorded.Add(id);
            }
        }

        if (_recorded.Count > 0)
        {
            Log.Warning("Shell", "Explorer did not uncloak " + _recorded.Count + " of " + hidden.Count + " windows as the desktop stops; the record keeps them: " + string.Join(' ', _recorded));
        }

        if (recorded > 0 || _recorded.Count > 0)
        {
            _windows.RecordHidden(_recorded);
        }
    }

    /// <summary>
    /// The windows Winoma keeps hidden: every window of a Workspace that is in front on no monitor, the scratchpad's while it is closed,
    /// each window a placement hid, and a window a switch showed until the Host lists it.
    /// </summary>
    private List<long> Hidden()
    {
        // Asked three times or more per arrange. The answer is used before it is asked for again.
        var hidden = _hidden;
        hidden.Clear();
        if (!_scratchpad.Open)
        {
            hidden.AddRange(_scratchpad.Windows);
        }

        foreach (var numbered in _travel.Numbered)
        {
            var session = numbered.Value;
            if (!InFront(numbered.Key))
            {
                hidden.AddRange(session.Windows);
                continue;
            }

            if (!session.AnyTucked && _arriving.Count == 0)
            {
                continue;
            }

            foreach (var id in session.Windows)
            {
                if (session.Tucked(id) || _arriving.Contains(id))
                {
                    hidden.Add(id);
                }
            }
        }

        foreach (var pair in _freeHome)
        {
            if (!InFront(pair.Value) || _arriving.Contains(pair.Key))
            {
                hidden.Add(pair.Key);
            }
        }

        return hidden;
    }

    /// <summary>
    /// Gives each Free window that stands alone the Workspace in front, the first time it is on the glass, and writes that Workspace on it.
    /// It is hidden and shown with that Workspace from then on, as a tile is. One that closed is forgotten.
    /// </summary>
    private void NoteFree(AdmittedWindows world)
    {
        var alone = world.FreeAlone;
        if (_freeHome.Count == 0 && alone.Count == 0)
        {
            return;
        }

        var here = Workspaces.Current;
        List<long>? unshown = null;
        _freeGone.Clear();
        foreach (var pair in _freeHome)
        {
            if (!_windows.Alive(pair.Key) || _travel.HomeOf(pair.Key) != 0)
            {
                // Closed, or a tile now: its program gave it a frame that can be sized.
                _freeGone.Add(pair.Key);
            }
            else if (InFront(pair.Value) && _arriving.Contains(pair.Key))
            {
                if (alone.Contains(pair.Key) || _windows.Shown(pair.Key))
                {
                    _arriving.Remove(pair.Key);
                }
                else
                {
                    // A switch showed it and it is not on the glass. It is asked again.
                    (unshown ??= []).Add(pair.Key);
                }
            }
        }

        foreach (var id in _freeGone)
        {
            _freeHome.Remove(id);
            _arriving.Remove(id);
            _forgot = true;
        }

        if (unshown is not null)
        {
            _windows.ShowAll(unshown);
        }

        for (var i = 0; i < alone.Count; i++)
        {
            var id = alone[i];
            if (IsOverlay(id) || _travel.HomeOf(id) != 0)
            {
                continue;
            }

            // Another Workspace has it: the census is a moment old, or the window showed itself, and FollowShown goes to it.
            if (_freeHome.TryGetValue(id, out var home) && home != here)
            {
                continue;
            }

            _freeHome[id] = here;
            _windows.MarkWorkspace(id, here);
        }

        // A Workspace exists while it holds a window, and a Free window is one.
        _travel.Recount();
    }

    /// <summary>
    /// Runs before windows are hidden. It adds them to the record (<see cref="HiddenWindows"/>): a desktop killed between the record
    /// and the hide would leave a window that nothing can show again.
    /// </summary>
    /// <returns>Every window that is hidden now or about to be.</returns>
    private List<long> RecordHiding()
    {
        var hidden = Hidden();
        var grew = false;
        foreach (var id in hidden)
        {
            grew |= _recorded.Add(id);
        }

        if (grew)
        {
            _windows.RecordHidden(_recorded);
        }

        return hidden;
    }

    /// <summary>
    /// Runs before a placement hides windows. It records them (<see cref="RecordHiding"/>), then moves the keyboard off the one that has it:
    /// a cloaked window keeps the keyboard (ADR 0059), and nothing typed into it would show.
    /// </summary>
    private void BeforeHiding()
    {
        var hidden = RecordHiding();

        // Asked on every pass of every placement. A Group member this placement shows again counts as hidden until the Host lists it,
        // and it is the window the workspace focuses, so it keeps the keyboard. A window is asked to give the keyboard up once per arrange:
        // one that no window could take it from keeps it until Super goes up (KeyboardHandoff). A placement hides the scratchpad's windows
        // and this Workspace's Group members. A window of another Workspace that has the keyboard took it itself, and is followed (FollowShown).
        if (_windows.Foreground() is long front
            && front != _keyboardTakenOff
            && hidden.Contains(front)
            && CurrentSession.Focused != front
            && (_scratchpad.Contains(front) || CurrentSession.Windows.Contains(front)))
        {
            _keyboardTakenOff = front;
            FocusWorkspace(front);

            // Nothing could take it, as while Super is held. The placement hides the window with the keyboard on it.
            if (_windows.Foreground() == front)
            {
                _keptKeyboard = front;
            }
        }
    }

    // The window the arrange in progress took the keyboard off, before it hid it.
    private long? _keyboardTakenOff;

    /// <summary>Takes the windows that are shown again off the record, after the show.</summary>
    private void RecordShown()
    {
        var hidden = Hidden();
        if (_recorded.SetEquals(hidden))
        {
            return;
        }

        _recorded.Clear();
        _recorded.UnionWith(hidden);
        _windows.RecordHidden(_recorded);
    }

    /// <summary>The panel that held the keyboard closed. A workspace switch it held through gets the keyboard now.</summary>
    public void SurfaceClosed()
    {
        _surfaceClosedAt = _clock();
        if (!_focusAfterSurface)
        {
            return;
        }

        _focusAfterSurface = false;
        TakeFocus();
    }

    /// <summary>
    /// Finishes a keyboard move that waited for Super to go up.
    /// The shell stays back while Super is the chord. A window that opened here is focused instead.
    /// </summary>
    public void SettleKeyboard()
    {
        var super = _superHeld();
        if (_handoff.TakeWaitingWindow(super) is long waiting && _windows.Alive(waiting) && CurrentSession.Windows.Contains(waiting))
        {
            if (FocusArrived(waiting))
            {
                _handoff.DropShell();
                return;
            }
        }

        if (_handoff.TakeWaitingHidden(super) || (_handoff.TakeWaitingShell(super) && _windows.ForegroundCloaked()))
        {
            // The keyboard went to another monitor, and waits on a window that is still on the glass on the one it left.
            var stranded = _keyboardLeft is long left && _windows.Foreground() == left ? _keyboardLeft : null;

            // Super is up, so a focus may join the thread that has the keyboard (ADR 0020). The Workspace's own window is asked
            // once more before the keyboard is dropped: Windows may have refused it only because nothing could join.
            if ((stranded is not null || _windows.ForegroundCloaked()) && CurrentSession.Focused is long focused && FocusArrived(focused))
            {
                return;
            }

            Log.Information("Workspace", "focus released");
            if (stranded is long kept)
            {
                _windows.ReleaseFocus(kept);
            }
            else
            {
                _windows.ReleaseFocus();
            }
        }

        ForgetKeptKeyboard();
    }

    /// <summary>
    /// Applies a Carry's release or a Super and right-button resize to the current workspace.
    /// A carried tile is dropped among the tiles where the pointer is. A carried Sticky window keeps the rectangle it was let go on,
    /// and so does a floated one: no arrange places a floated window, so it is left there.
    /// </summary>
    /// <param name="drag">The gesture the placer reported.</param>
    public void ApplyDrag(TileDrag drag)
    {
        // The Workspace in front that holds the window. With one monitor it is the one with the keyboard.
        var home = Workspaces.SeveralInFront && _travel.HomeOf(drag.Id) is var held && held != 0 && InFront(held) ? held : Workspaces.Current;
        var session = _travel.At(home);
        if (drag.Drop && Workspaces.SeveralInFront && !_sticky.ContainsKey(drag.Id) && session.Windows.Contains(drag.Id)
            && _travel.FrontAt(drag.X, drag.Y) is var target && target != 0 && target != home)
        {
            CarryAcross(drag, session, target);
            return;
        }

        if (drag.Drop && session.Floated(drag.Id))
        {
            // Dropped among the tiles, it would take its tile there when it is tiled again, and not the one it floated from.
            Log.Information("Tiling", "floated window " + drag.Id + " let go at " + Describe(drag.Rect));
            return;
        }

        if (drag.Drop && _sticky.ContainsKey(drag.Id))
        {
            // The carry drew it there. That rectangle is what every arrange places it on, on every workspace.
            if (drag.Rect is not { Width: > 0, Height: > 0 } let)
            {
                return;
            }

            _sticky[drag.Id] = let;
            Log.Information("Tiling", "sticky window " + drag.Id + " let go at " + Describe(drag.Rect));

            // The rectangle is not in the mark, so nothing else tells the next arrange to place.
            _placedSession = null;
        }
        else if (drag.Drop)
        {
            session.Drop(drag.Id, _travel.Area(home), drag.X, drag.Y);
        }
        else if (session.Windows.Contains(drag.Id))
        {
            // Resize follows the focused window. The drag names the window under the pointer, which may be another tile.
            session.Focus(drag.Id);
            session.Resize(drag.Dx, drag.Dy, _travel.Area(home));
        }
        else
        {
            return;
        }

        Arrange();
    }

    /// <summary>
    /// A Carry let a window go on another monitor. It joins the Workspace in front there, among the tiles where the pointer is,
    /// and a floated window stays floated where it was let go. The keyboard goes with it.
    /// </summary>
    private void CarryAcross(TileDrag drag, TilingSession from, int target)
    {
        var floated = from.Floated(drag.Id);
        Log.Information("Workspace", "window " + drag.Id + " was carried onto workspace " + target + " on another monitor");
        _travel.Put(drag.Id, target);
        var session = _travel.At(target);
        if (floated)
        {
            session.Focus(drag.Id);
            session.ToggleFloat();
        }
        else
        {
            session.Drop(drag.Id, _travel.Area(target), drag.X, drag.Y);
        }

        Scene++;
        _placedSession = null;
        if (target != Workspaces.Current)
        {
            _followed = drag.Id;
            Travel(target);
        }

        Arrange();
    }

    private static string Describe(TileRect? rect) =>
        rect is TileRect at ? at.X + "," + at.Y + " " + at.Width + "x" + at.Height : "an unknown place";

    /// <summary>Reconciles the current workspace with the windows that are actually open, then places them.</summary>
    public void Arrange() => Arrange(immediate: false);

    private void Arrange(bool immediate)
    {
        if (_arrangeDepth > 0)
        {
            return;
        }

        NoteMonitors();
        if (_marksAdopted && !Switching)
        {
            FollowMonitors();
        }

        _arrangeDepth++;
        var started = _pace is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
        var tiled = 0;
        try
        {
            tiled = PlaceLiveWindows(immediate, out var placed);

            // A hidden window that closed leaves the record too, though no tile moved for it.
            if (placed || _forgot)
            {
                _forgot = false;
                RecordShown();
            }
        }
        finally
        {
            _arrangeDepth--;
            _keyboardTakenOff = null;
            if (_pace is not null)
            {
                var milliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (_pace.NoteArrange(_clock(), milliseconds, tiled) is string summary)
                {
                    Log.Information("Bar", summary);
                }
            }
        }

        // A window that could not be hidden left what held it while this arrange ran. It is on the glass, and takes a tile now.
        if (_again && _arrangeDepth == 0)
        {
            _again = false;
            Arrange(immediate);
        }
    }

    private int PlaceLiveWindows(bool immediate, out bool placed)
    {
        var world = _windows.World();
        if (!_marksAdopted)
        {
            // Before anything is placed or marked: every window goes onto the Workspace it carries, and the first placement
            // marks each tile it places with the Workspace in front.
            _marksAdopted = true;
            AdoptMarks(world);
        }

        // A window that closed while its Workspace was hidden. The bar paints from the book after this.
        var pruned = _travel.PruneClosed();
        _forgot |= pruned;
        var players = world.PictureInPicture;
        for (var i = 0; i < players.Count; i++)
        {
            // Drawn with every Workspace, so it carries none. One that had a tile until now still carries that Workspace.
            _windows.MarkWorkspace(players[i], WorkspaceMark.None);
        }

        PruneOverlays();
        NoteFree(world);

        // Every Workspace in front is tiled, each on its own monitor. The one with the keyboard is last, and a window that just opened joins it.
        var fronts = Fronts();
        var keyboard = fronts[^1];
        var session = keyboard.Session;
        _before.Clear();
        foreach (var front in fronts)
        {
            _before.AddRange(front.Session.Windows);
        }

        var added = false;
        var framed = false;
        var live = world.Tiles;
        _liveIds.Clear();
        for (var i = 0; i < live.Count; i++)
        {
            _liveIds.Add(live[i].Id);
        }

        HashSet<long>? appFullscreen = null;
        List<long>? unshown = null;
        List<long>? sentAway = null;
        foreach (var front in fronts)
        {
            DropGone(front, world, ref appFullscreen, ref unshown);
        }

        if (unshown is not null)
        {
            _windows.ShowAll(unshown);
        }

        if (session.Windows.Count == 0 && Recall(session, live))
        {
            added = true;
        }

        foreach (var window in live)
        {
            if (IsOverlay(window.Id) || Closing(window.Id))
            {
                continue;
            }

            var front = keyboard;
            if (!session.Windows.Contains(window.Id))
            {
                var home = _travel.HomeOf(window.Id);
                if (home != 0)
                {
                    // Another Workspace holds it, and where a window is, is Winoma's own record (ADR 0003). One in front on another monitor
                    // tiles it there. For any other, the census was taken a moment before the window was hidden, or the window showed itself,
                    // and then FollowShown goes to it.
                    if (!InFront(home) || !FrontOf(fronts, home, out front))
                    {
                        continue;
                    }
                }
                else if (_launches.Take(_clock()) is var asked && asked != 0 && asked != keyboard.Number)
                {
                    // It just opened, and its program was started from a Workspace the keyboard has left since. It opens there (ADR 0038).
                    Log.Information("Workspace", "window " + window.Id + " opens on workspace " + asked + ", where it was started; the keyboard is on workspace " + keyboard.Number);
                    _launched = window.Id;
                    _launchedUntil = _clock() + LaunchedKeyboardMs;
                    if (!InFront(asked) || !FrontOf(fronts, asked, out front))
                    {
                        _travel.Put(window.Id, asked);
                        (sentAway ??= []).Add(window.Id);
                        continue;
                    }
                }
            }

            var tiles = front.Session;
            tiles.NoteListed(window.Id);
            _arriving.Remove(window.Id);

            // It is on the glass with the tiles of a Workspace in front, so this is its Workspace.
            _windows.MarkWorkspace(window.Id, front.Number);

            // It has a caption again, so its app left fullscreen. It leaves the monitor with it.
            if (_inApp.Remove(window.Id))
            {
                tiles.SetFullscreen(window.Id, covers: false);
            }

            if (tiles.Fullscreen != window.Id)
            {
                // On a monitor with no bar the tiles reach every edge, and a lone tile covers it. Covering it then says nothing.
                framed |= _windows.ApplyCaption(window.Id, front.Area == front.Screen ? default : front.Screen);
            }

            if (!tiles.Windows.Contains(window.Id))
            {
                tiles.Add(window.Id, front.Area);
                added = true;
            }
        }

        foreach (var front in fronts)
        {
            if (_restoring.Count > 0)
            {
                RegroupRestored(front.Number, front.Session, wait: true);
            }

            _travel.Sync(front.Number);
        }

        WriteTiling();

        if (sentAway is not null)
        {
            // Its Workspace is hidden, so it is hidden with it. The record names it first, and a keyboard already on it goes to a window here.
            RecordHiding();
            if (_windows.Foreground() is long taken && sentAway.Contains(taken))
            {
                FocusWorkspace(taken);
            }

            HideAway(sentAway);
            NoteKeptKeyboard(sentAway);
            Scene++;
        }

        Mark(world);
        // A window whose frame was just taken off reaches past its tile by another border, so it is placed.
        if (!immediate && !added && !framed && appFullscreen is null && Settled(fronts))
        {
            if (pruned)
            {
                Scene++;
            }

            // A title can still have changed, and a group's strip shows it.
            if (AnyStrips(fronts))
            {
                Placed?.Invoke();
            }

            placed = false;
            return live.Count;
        }

        placed = true;
        _placedSession = null;
        _placedOthers.Clear();
        var overlay = PrepareOverlay();
        for (var i = 0; i < fronts.Count; i++)
        {
            // A placement names every tile on the glass, so each Workspace is placed with the tiles of the others beside its own.
            var front = fronts[i];
            var placedTiles = TileFit.Place(front.Session, _windows, front.Area, front.Screen, immediate, WithOthers(overlay, fronts, i), appFullscreen, BeforeHiding);
            if (i < fronts.Count - 1)
            {
                _placedOthers.Add(new PlacedFront(front.Session, placedTiles, front.Area, front.Screen));
            }
            else
            {
                _placedTiles = placedTiles;
            }
        }

        _placedSession = session;
        _placedArea = keyboard.Area;
        _placedScreen = keyboard.Screen;
        (_placedMark, _mark) = (_mark, _placedMark);
        NoteFloated(fronts);

        if (added && session.Focused is long arrived && _handoff.Claim(arrived, KeyboardHere(), _superHeld()) is long claim && FocusArrived(claim))
        {
            _handoff.DropShell();
        }

        if (pruned || !SameWindows(_before, fronts))
        {
            Scene++;
        }

        if (_refocus)
        {
            _refocus = false;
            FocusWorkspace();
        }

        Placed?.Invoke();
        return live.Count;
    }

    /// <summary>
    /// Takes the windows that are no longer among the tiles on the glass out of a Workspace in front, but for the ones that are still its own:
    /// a window a switch showed that the Host has not listed yet, a window fullscreen in its own app, and a Group member behind the one shown.
    /// </summary>
    /// <param name="front">A Workspace in front.</param>
    /// <param name="world">The census this arrange reads.</param>
    /// <param name="appFullscreen">Given each window found fullscreen in its own app.</param>
    /// <param name="unshown">Given each window a switch showed that is still hidden. The caller asks for them again.</param>
    private void DropGone(Front front, AdmittedWindows world, ref HashSet<long>? appFullscreen, ref List<long>? unshown)
    {
        var session = front.Session;
        var area = front.Area;
        var screen = front.Screen;
        var players = world.PictureInPicture;
        _gone.Clear();
        foreach (var id in session.Windows)
        {
            if (!_liveIds.Contains(id))
            {
                _gone.Add(id);
            }
        }

        foreach (var gone in _gone)
        {
            // A player that is kept above the windows again is Picture-in-picture, and has no tile (ADR 0076).
            if (players.Contains(gone))
            {
                _inApp.Remove(gone);
                _arriving.Remove(gone);
                session.Remove(gone, area);
                continue;
            }

            // A desktop that starts takes every marked window it finds hidden for a tile. This one is Free, now that it can be seen.
            if (_arriving.Contains(gone) && world.FreeAlone.Contains(gone))
            {
                _arriving.Remove(gone);
                session.Remove(gone, area);
                _freeHome[gone] = front.Number;
                continue;
            }

            // A switch showed it, and the Host does not list it.
            if (_arriving.Contains(gone) && _windows.Alive(gone))
            {
                var shown = _windows.Shown(gone);
                if (shown && !_inApp.Contains(gone))
                {
                    // On the glass. The census was taken a moment before it was shown.
                    // One that is fullscreen in its own app is never listed, and is met below.
                    _arriving.Remove(gone);
                    continue;
                }

                if (!shown && !_windows.OnAnotherDesktop(gone))
                {
                    // Still hidden: Explorer did not take the cloak off. It keeps its tile and is asked again.
                    (unshown ??= []).Add(gone);
                    continue;
                }

                if (!shown)
                {
                    // Winoma's cloak is off and Windows' is on: the window was taken to another virtual desktop. It is not Winoma's there (ADR 0003).
                    Log.Information("Workspace", "window " + gone + " is on another Windows desktop; it left workspace " + front.Number);
                }
            }

            _arriving.Remove(gone);

            // An app that goes fullscreen itself, such as F11 or a video, drops its caption and fills the monitor.
            // It stays in the session, and covers the monitor as the fullscreen chord would have it (ADR 0076).
            if (_windows.Shown(gone))
            {
                appFullscreen ??= [];
                appFullscreen.Add(gone);
                if (_windows.Frame(gone) is TileRect frame && ScreenCover.Fills(frame, screen) && _inApp.Add(gone) && !AskedTiled(gone))
                {
                    session.SetFullscreen(gone, covers: true);
                }

                continue;
            }

            // An inactive group member is hidden by the tiler, not closed. It stays while its window does.
            if (session.Tucked(gone) && _windows.Alive(gone))
            {
                continue;
            }

            _inApp.Remove(gone);
            session.Remove(gone, area);
        }
    }

    private static bool FrontOf(List<Front> fronts, int number, out Front front)
    {
        foreach (var candidate in fronts)
        {
            if (candidate.Number == number)
            {
                front = candidate;
                return true;
            }
        }

        front = default;
        return false;
    }

    private static bool AnyStrips(List<Front> fronts)
    {
        foreach (var front in fronts)
        {
            if (front.Session.HasStrips)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The windows drawn with one Workspace's tiles: <paramref name="overlay"/>, and the tiles of every other Workspace in front.</summary>
    private static IReadOnlyList<Placement> WithOthers(IReadOnlyList<Placement> overlay, List<Front> fronts, int index)
    {
        if (fronts.Count == 1)
        {
            return overlay;
        }

        var also = new List<Placement>(overlay);
        for (var i = 0; i < fronts.Count; i++)
        {
            if (i != index)
            {
                also.AddRange(fronts[i].Session.Arrange(fronts[i].Area, fronts[i].Screen));
            }
        }

        return also;
    }

    /// <summary>What the last placement put on another monitor: a Workspace in front that does not have the keyboard.</summary>
    private readonly record struct PlacedFront(TilingSession Session, IReadOnlyList<Placement> Tiles, TileRect Area, TileRect Screen);

    private readonly List<PlacedFront> _placedOthers = [];
    private readonly List<int> _frontsWere = [];

    // The monitors the last arrange read.
    private readonly List<TileRect> _arrangedMonitors = [];

    /// <summary>
    /// True when the monitors are not the ones the last arrange read: one came, went, moved, or changed size. An arrange is what gives
    /// a new monitor its Workspace and takes the windows of a removed one off the glass (<see cref="WorkspaceTravel.Settle"/>), and nothing
    /// else asks for one: the tiles on the monitors that stayed did not move. The desktop asks on every turn of its loop, and arranges.
    /// </summary>
    public bool MonitorsChanged
    {
        get
        {
            var monitors = _windows.Monitors;
            if (monitors.Count != _arrangedMonitors.Count)
            {
                return true;
            }

            for (var i = 0; i < monitors.Count; i++)
            {
                if (monitors[i] != _arrangedMonitors[i])
                {
                    return true;
                }
            }

            return false;
        }
    }

    private void NoteMonitors()
    {
        if (!MonitorsChanged)
        {
            return;
        }

        _arrangedMonitors.Clear();
        _arrangedMonitors.AddRange(_windows.Monitors);
    }

    /// <summary>
    /// Follows the monitors as they come and go, before an arrange reads them (<see cref="WorkspaceTravel.Settle"/>).
    /// The windows of a Workspace that came in front are shown, and those of one that no longer is are hidden.
    /// </summary>
    private void FollowMonitors()
    {
        // Asked before every arrange, so the list is kept.
        var before = _frontsWere;
        _travel.Fronts(before);
        var from = Workspaces.Current;
        if (!_travel.Settle())
        {
            return;
        }

        Switching = true;
        try
        {
            if (Switch(before, from))
            {
                Scene++;
            }
        }
        finally
        {
            _placedSession = null;
            Switching = false;
        }
    }

    // False until this shell has read what the desktop before it left on the windows.
    private bool _marksAdopted;

    /// <summary>
    /// Reads what the desktop before this one left on the windows, when nothing has yet. The first arrange does it with the census it reads anyway.
    /// A command that comes before that arrange asks here first: it would act on Workspaces that hold none of their windows yet.
    /// </summary>
    private void Adopt()
    {
        if (!_marksAdopted)
        {
            _marksAdopted = true;
            AdoptMarks(_windows.World());
        }
    }

    /// <summary>
    /// Takes over what the desktop before this one wrote on the windows (ADR 0078), before anything is placed or marked.
    /// Every marked window goes onto its Workspace, whether it is on the glass or the replaced desktop left it hidden, and a window
    /// that carries the scratchpad's mark goes back into the scratchpad, closed. The Workspace in front is the one whose windows are
    /// on the glass (<see cref="WorkspaceFront"/>). A window on the glass that carries no mark is found by the placement that follows,
    /// and joins the Workspace in front.
    /// After a replace nothing is shown or hidden here. After a crash or a Stop every window was shown (ADR 0059), so the windows
    /// of every other Workspace are hidden again, and the placement that follows hides the scratchpad's.
    /// </summary>
    private void AdoptMarks(AdmittedWindows world)
    {
        var marks = _windows.WorkspaceMarks();
        var left = LeftHidden(marks, world);
        List<long>? inScratchpad = null;
        foreach (var pair in marks)
        {
            if (pair.Value == WorkspaceMark.Scratchpad && _windows.Alive(pair.Key))
            {
                (inScratchpad ??= []).Add(pair.Key);
            }
        }

        if (inScratchpad is not null)
        {
            // They go in by handle, since the order they were sent in went with the old desktop.
            inScratchpad.Sort();
            foreach (var id in inScratchpad)
            {
                _scratchpad.Add(id);
            }

            // One of them may have the keyboard. The placement that hides it gives the keyboard to the Workspace first (BeforeHiding).
            Log.Information("Shell", "the scratchpad took back " + inScratchpad.Count + " windows");
        }

        // Each Workspace's windows in the order they are found: the glass first, in stacking order, then the ones left hidden.
        var homes = new List<long>?[WorkspaceBook.Maximum + 1];
        var onGlass = new int[WorkspaceBook.Maximum + 1];
        var held = new int[WorkspaceBook.Maximum + 1];
        foreach (var window in world.Tiles)
        {
            if (marks.TryGetValue(window.Id, out var mark) && WorkspaceMark.IsWorkspace(mark))
            {
                (homes[mark] ??= []).Add(window.Id);
                onGlass[mark]++;
                held[mark]++;
            }
        }

        // A Free window that stands alone is on its Workspace too, and takes no tile there.
        foreach (var id in world.FreeAlone)
        {
            if (marks.TryGetValue(id, out var mark) && WorkspaceMark.IsWorkspace(mark))
            {
                _freeHome[id] = mark;
            }
        }

        _travel.Recount();

        foreach (var id in left)
        {
            if (marks.TryGetValue(id, out var mark) && WorkspaceMark.IsWorkspace(mark) && _windows.Alive(id) && (homes[mark] is not { } known || !known.Contains(id)))
            {
                (homes[mark] ??= []).Add(id);
                held[mark]++;
            }
        }

        // Each Workspace's Layout, before any tile is built or placed (ADR 0050). Its Groups are made after its tiles are.
        ReadTiling(homes);

        // The window that has the keyboard says where the person was working, when it is on the glass: a tile, or a Free window of its Workspace.
        var keyboard = _windows.Foreground() is long front
            && marks.TryGetValue(front, out var worked)
            && (Lists(world.Tiles, front) || world.FreeAlone.Contains(front))
                ? worked
                : 0;
        var number = WorkspaceFront.Pick(onGlass, held, keyboard);

        // No file says which monitor a Workspace is on (ADR 0079). It is where its windows are, and a hidden window has not moved.
        var several = _travel.MonitorCount > 1;
        if (several)
        {
            for (var home = 1; home <= WorkspaceBook.Maximum; home++)
            {
                if (homes[home] is { } windows && MostOf(windows) is TileRect where)
                {
                    _travel.Belong(home, where);
                }
            }
        }

        for (var home = 1; home <= WorkspaceBook.Maximum; home++)
        {
            // The Workspace with the keyboard is rebuilt by the placement that follows, from the same rectangles (Recall).
            if (home != number && homes[home] is { } windows)
            {
                Rebuild(home, windows);
            }
        }

        // On each other monitor, the Workspace in front is the one with the most windows on the glass there.
        _travel.Start(number, several ? WorkspaceFront.PerMonitor(onGlass, _travel.Monitor, number) : null);
        var away = 0;
        var batch = _batch;
        batch.Clear();
        for (var home = 1; home <= WorkspaceBook.Maximum; home++)
        {
            if (InFront(home) || homes[home] is not { } windows)
            {
                continue;
            }

            away += windows.Count;
            foreach (var id in windows)
            {
                if (Lists(world.Tiles, id))
                {
                    batch.Add(id);
                }
            }
        }

        foreach (var pair in _freeHome)
        {
            if (!InFront(pair.Value))
            {
                batch.Add(pair.Key);
            }
        }

        Log.Information("Workspace", "read the marks of " + marks.Count + " windows; " + away + " are on workspaces that are not in front");
        if (batch.Count > 0)
        {
            // Shown by a desktop that stopped, or by the watcher of one that crashed. The record names them before they are hidden again.
            RecordHiding();
            LeaveKeyboard(batch);
            HideAway(batch);
            NoteKeptKeyboard(batch);
        }

        SettleLeftHidden(left);
    }

    /// <summary>
    /// Every window that is hidden Winoma's way as this desktop starts. The record names some, when the desktop before this one was replaced.
    /// The rest are found on the windows themselves: each one that carries a mark, is not on the glass, and is under Winoma's kind of cloak.
    /// No record has to name such a window (ADR 0078): a record can be lost, or never written, and the window would stay cloaked with nothing to show it.
    /// </summary>
    private List<long> LeftHidden(IReadOnlyDictionary<long, int> marks, AdmittedWindows world)
    {
        var left = new List<long>(_windows.LeftHidden());
        var unrecorded = 0;
        foreach (var pair in marks)
        {
            var id = pair.Key;
            if (!left.Contains(id) && !Lists(world.Tiles, id) && !world.Free.Contains(id) && _windows.HiddenByCloak(id))
            {
                left.Add(id);
                unrecorded++;
            }
        }

        if (unrecorded > 0)
        {
            Log.Information("Shell", "found " + unrecorded + " marked windows cloaked that no record named; they are this desktop's now");
        }

        return left;
    }

    /// <summary>A rectangle one of <paramref name="windows"/> covers, on the monitor that holds the most of them. Null when none has a rectangle.</summary>
    private TileRect? MostOf(List<long> windows)
    {
        TileRect? where = null;
        var most = 0;
        Dictionary<int, int>? tally = null;
        foreach (var id in windows)
        {
            if (_windows.Frame(id) is not { Width: > 0, Height: > 0 } frame || _travel.MonitorOf(frame) is not (>= 0 and var monitor))
            {
                continue;
            }

            tally ??= [];
            var count = tally.GetValueOrDefault(monitor) + 1;
            tally[monitor] = count;
            if (count > most)
            {
                most = count;
                where = frame;
            }
        }

        return where;
    }

    /// <summary>
    /// Gives <paramref name="home"/> its windows as a desktop starts. Its tiles are read from the rectangles the windows cover, as the Workspace
    /// in front's are (ADR 0050): a hidden window has not moved since the last desktop placed it. A window with no rectangle is added after.
    /// </summary>
    private void Rebuild(int home, List<long> windows)
    {
        var session = _travel.At(home);
        var seen = new List<Placement>(windows.Count);
        foreach (var id in windows)
        {
            if (_windows.Frame(id) is { Width: > 0, Height: > 0 } frame)
            {
                seen.Add(new Placement(id, frame.X, frame.Y, frame.Width, frame.Height, true));
            }
        }

        if (seen.Count >= 2 && session.Recall(seen))
        {
            Log.Information("Tiling", "workspace " + home + " kept the layout of " + seen.Count + " windows");
        }

        var area = _travel.Area(home);
        foreach (var id in windows)
        {
            if (!session.Windows.Contains(id))
            {
                session.Add(id, area);
            }
        }

        // Every window of the Workspace is in the session now, one that is hidden too.
        RegroupRestored(home, session, wait: false);
        _travel.Sync(home);
    }

    /// <summary>
    /// Settles the windows this desktop found hidden as it started (<see cref="LeftHidden(IReadOnlyDictionary{long, int}, AdmittedWindows)"/>, ADR 0059).
    /// One this shell keeps hidden stays as it is, never shown in between, and is on the record: a window of a Workspace that is not in front,
    /// and a scratchpad window. Every other is shown, before the record lets go of it: a window of the Workspace in front, such as a Group member
    /// that was behind the one shown, which the placement after the Group is made hides again, and a window that carries no mark, which is in nothing that would ever show it again.
    /// </summary>
    private void SettleLeftHidden(List<long> left)
    {
        if (left.Count == 0)
        {
            return;
        }

        var hidden = Hidden();
        List<long>? shown = null;
        List<long>? kept = null;
        foreach (var id in left)
        {
            if (hidden.Contains(id))
            {
                _recorded.Add(id);
                (kept ??= []).Add(id);
            }
            else
            {
                (shown ??= []).Add(id);
            }
        }

        if (shown is not null)
        {
            _windows.ShowAllAndWait(shown);
        }

        _windows.RecordHidden(_recorded);
        if (kept is not null)
        {
            // Each is hidden already. Asking again puts one no record named on this desktop's ledger, and leaves the others alone.
            _ = _windows.HideAll(kept);
        }

        Log.Information("Shell", "kept " + _recorded.Count + " of the " + left.Count + " windows found hidden as the desktop started, and showed the rest");
    }

    /// <summary>
    /// Tells the gateway which windows a Carry may take though no tile is under them: the floated windows of each Workspace in front
    /// and the Sticky ones. Floating a window and making one Sticky both place, so the set is read after a placement, and told when it changed.
    /// </summary>
    private void NoteFloated(List<Front> fronts)
    {
        var floated = _floated;
        floated.Clear();
        for (var i = fronts.Count - 1; i >= 0; i--)
        {
            var session = fronts[i].Session;
            foreach (var id in session.Windows)
            {
                if (session.Floated(id))
                {
                    floated.Add(id);
                }
            }
        }

        foreach (var id in _sticky.Keys)
        {
            floated.Add(id);
        }

        if (floated.SequenceEqual(_floatedTold))
        {
            return;
        }

        (_floatedTold, _floated) = (floated, _floatedTold);
        _windows.NoteFloated(_floatedTold);
    }

    /// <summary>
    /// True when a placement would put the same tiles where they already are: the session, its tiles, the rectangles, and the windows
    /// <see cref="Mark"/> listed are those of the last placement, nothing waits to close or to be asked its minimum, and every tile holds.
    /// A fullscreen tile is always placed, since a placement is what keeps it above the bar.
    /// </summary>
    private bool Settled(TilingSession session, TileRect area, TileRect screen) =>
        ReferenceEquals(session, _placedSession)
        && area == _placedArea
        && screen == _placedScreen
        && _closing.Count == 0
        && !_refocus
        && session.Fullscreen is null
        && !session.AsksMinimum(area)
        && SameMark()
        && ReferenceEquals(session.Arrange(area, screen), _placedTiles)
        && _windows.TilesHold();

    /// <summary><see cref="Settled(TilingSession, TileRect, TileRect)"/> for every Workspace in front: each monitor shows what its Workspace holds.</summary>
    private bool Settled(List<Front> fronts)
    {
        if (fronts.Count - 1 != _placedOthers.Count)
        {
            return false;
        }

        for (var i = 0; i < _placedOthers.Count; i++)
        {
            var front = fronts[i];
            var was = _placedOthers[i];
            if (!ReferenceEquals(front.Session, was.Session)
                || front.Area != was.Area
                || front.Screen != was.Screen
                || front.Session.Fullscreen is not null
                || front.Session.AsksMinimum(front.Area)
                || !ReferenceEquals(front.Session.Arrange(front.Area, front.Screen), was.Tiles))
            {
                return false;
            }
        }

        var keyboard = fronts[^1];
        return Settled(keyboard.Session, keyboard.Area, keyboard.Screen);
    }

    /// <summary>
    /// Writes down the windows a placement depends on besides the session's own: the tiles, players, and free windows the census found,
    /// in its order, and the scratchpad and sticky windows drawn with them. Titles are left out, since a title moves no tile.
    /// </summary>
    private void Mark(AdmittedWindows world)
    {
        var mark = _mark;
        mark.Clear();
        mark.Add(world.Tiles.Count);
        for (var i = 0; i < world.Tiles.Count; i++)
        {
            mark.Add(world.Tiles[i].Id);
        }

        mark.Add(world.PictureInPicture.Count);
        for (var i = 0; i < world.PictureInPicture.Count; i++)
        {
            mark.Add(world.PictureInPicture[i]);
        }

        mark.Add(world.Free.Count);
        for (var i = 0; i < world.Free.Count; i++)
        {
            mark.Add(world.Free[i]);
        }

        mark.Add(_scratchpad.Open ? 1 : 0);
        mark.Add(_scratchpad.Windows.Count);
        for (var i = 0; i < _scratchpad.Windows.Count; i++)
        {
            mark.Add(_scratchpad.Windows[i]);
        }

        foreach (var id in _sticky.Keys)
        {
            mark.Add(id);
        }
    }

    private bool SameMark()
    {
        if (_mark.Count != _placedMark.Count)
        {
            return false;
        }

        for (var i = 0; i < _mark.Count; i++)
        {
            if (_mark[i] != _placedMark[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// An empty workspace takes the layout its windows already show, so a desktop that starts leaves them where they are.
    /// Added one at a time they would be tiled in stacking order, and the window used last would take the first tile.
    /// </summary>
    private bool Recall(TilingSession session, IReadOnlyList<TrackedWindow> live)
    {
        var seen = new List<Placement>();
        foreach (var window in live)
        {
            // A window asked to close is leaving, and one another Workspace holds is not this one's, wherever the census found it.
            if (!IsOverlay(window.Id)
                && !Closing(window.Id)
                && _travel.HomeOf(window.Id) == 0
                && _windows.Frame(window.Id) is { Width: > 0, Height: > 0 } frame)
            {
                seen.Add(new Placement(window.Id, frame.X, frame.Y, frame.Width, frame.Height, true));
            }
        }

        if (seen.Count < 2 || !session.Recall(seen, _windows.Foreground()))
        {
            return false;
        }

        Log.Information("Tiling", "workspace " + Workspaces.Current + " kept the layout of " + seen.Count + " windows");
        return true;
    }

    private TileRect HomeArea() => _travel.Area(Workspaces.Current);

    /// <summary>The work area and the monitor of the workspace in front, from one read of the monitors.</summary>
    private void HomeRects(out TileRect area, out TileRect screen)
    {
        var monitors = _windows.Monitors;
        var fallback = _windows.FirstBarArea;
        area = _travel.Area(monitors, fallback, Workspaces.Current);
        var screens = _windows.Screens;
        if (screens.Count == 0)
        {
            screen = area;
            return;
        }

        var index = _travel.Monitor(monitors.Count > 0 ? monitors : screens, fallback, Workspaces.Current);
        screen = index >= 0 && index < screens.Count ? screens[index] : screens[0];
    }

    private TileRect HomeScreen()
    {
        var screens = _windows.Screens;
        if (screens.Count == 0)
        {
            return HomeArea();
        }

        var monitors = _windows.Monitors;
        var index = _travel.Monitor(monitors.Count > 0 ? monitors : screens, _windows.FirstBarArea, Workspaces.Current);
        return index >= 0 && index < screens.Count ? screens[index] : screens[0];
    }

    private static bool SameScreen(TileRect left, TileRect right) =>
        left.Width > 0
        && left.Height > 0
        && left.X == right.X
        && left.Y == right.Y
        && left.Width == right.Width
        && left.Height == right.Height;

    private void PruneOverlays()
    {
        for (var i = _scratchpad.Windows.Count - 1; i >= 0; i--)
        {
            var id = _scratchpad.Windows[i];
            if (!_windows.Alive(id))
            {
                _scratchpad.Remove(id);
            }
        }

        if (_sticky.Count > 0)
        {
            foreach (var id in _sticky.Keys.ToArray())
            {
                if (!_windows.Alive(id))
                {
                    _sticky.Remove(id);
                }
            }
        }

        // A window that closed is never asked about again, so its entry would stay for the life of the desktop.
        if (_closing.Count > 0)
        {
            var now = _clock();
            foreach (var pair in _closing.ToArray())
            {
                var gone = !_windows.Alive(pair.Key);
                if (gone || now >= pair.Value)
                {
                    _closing.Remove(pair.Key);
                    if (_closingGrouped.Remove(pair.Key) && gone)
                    {
                        _refocus = true;
                    }
                }
            }
        }
    }

    // The Workspace the scratchpad was last shown over. It is drawn on that Workspace's monitor, and hides when another comes in front there.
    private int _scratchpadOver;

    private IReadOnlyList<Placement> PrepareOverlay()
    {
        BeforeHiding();
        if (_scratchpad.Open)
        {
            // One call for all of them: they come up in the same frame, and the desktop waits one frame for it.
            _windows.ShowAllAndWait(_scratchpad.Windows);
        }
        else if (_scratchpad.Windows.Count > 0)
        {
            // Asked on every arrange. A window that is already hidden is left alone.
            HideAway(_scratchpad.Windows);
        }

        if (!_scratchpad.Open && _sticky.Count == 0)
        {
            return [];
        }

        return Overlay() ?? [];
    }

    /// <summary>The windows drawn over the tiles: the scratchpad's while it is shown, on the monitor of the Workspace it is over, and the Sticky ones.</summary>
    private List<Placement>? Overlay()
    {
        var placements = StickyTiles();
        if (_scratchpad.Open && _scratchpad.Windows.Count > 0)
        {
            (placements ??= []).InsertRange(0, OverlayTiles.Cover(_scratchpad.Windows, _travel.Area(_scratchpadOver == 0 ? Workspaces.Current : _scratchpadOver)));
        }

        return placements;
    }

    /// <summary>Focuses the foreground window when it belongs to this workspace. A sticky or scratchpad window in front is left for its own chords.</summary>
    /// <returns>True when a tiled window on this workspace is the target.</returns>
    private bool UseFrontWindow()
    {
        if (ActiveWindow() is not long id || IsOverlay(id) || !CurrentSession.Windows.Contains(id))
        {
            return false;
        }

        CurrentSession.Focus(id);
        return true;
    }

    /// <summary>Runs <paramref name="change"/> on the front window of this workspace, then places the windows. Does nothing when <see cref="UseFrontWindow"/> finds none.</summary>
    private void WithFrontWindow(Action change)
    {
        if (!UseFrontWindow())
        {
            return;
        }

        change();
        Arrange();
    }

    private void CloseFront()
    {
        if (ActiveWindow() is not long id)
        {
            return;
        }

        _windows.Close(id);
        _closing[id] = _clock() + ClosingGraceMs;
        _scratchpad.Remove(id);
        if (_sticky.Remove(id))
        {
            if (_raised == id)
            {
                _raised = null;
            }
        }

        var area = HomeArea();
        if (CurrentSession.Windows.Contains(id))
        {
            // The window in front is the one being closed, whatever the session last focused.
            CurrentSession.Focus(id);
            if (CurrentSession.Grouped(id))
            {
                _closingGrouped.Add(id);
            }
        }

        CurrentSession.Remove(id, area);
        Arrange();
    }

    // Grouped windows that were asked to close. When one is gone, the member its group shows next is given the keyboard:
    // it was hidden until then, and a window that is shown is not activated. Not before, since a window that asks
    // "Save changes?" has not closed, and its prompt must keep the keyboard.
    private readonly HashSet<long> _closingGrouped = [];
    private bool _refocus;

    /// <summary>True while <paramref name="id"/> was asked to close less than <see cref="ClosingGraceMs"/> ago. Tiled again, it would jump back in beside its neighbors, or into an armed group, for the moment it takes to close.</summary>
    private bool Closing(long id)
    {
        if (!_closing.TryGetValue(id, out var until))
        {
            return false;
        }

        if (_clock() < until)
        {
            return true;
        }

        _closing.Remove(id);

        // It refused to close, so no member of its group waits for the keyboard. Nothing else would drop the entry.
        _closingGrouped.Remove(id);
        return false;
    }

    /// <summary>True when <paramref name="id"/> is sticky or in the scratchpad, so the tiler leaves it alone.</summary>
    private bool IsOverlay(long id) => _sticky.ContainsKey(id) || _scratchpad.Contains(id);

    /// <summary>
    /// True when <paramref name="id"/> is an overlay that is drawn now: a sticky window, or a scratchpad window while the scratchpad is shown.
    /// The window is asked too. A scratchpad shown on one workspace stays there after a jump, and a chord on the next workspace
    /// would close or move a window nobody can see.
    /// </summary>
    private bool OnGlass(long id) =>
        (_sticky.ContainsKey(id) || (_scratchpad.Open && _scratchpad.Contains(id))) && _windows.Shown(id);

    private long? ActiveWindow()
    {
        if (_windows.Foreground() is long front
            && _windows.Alive(front)
            && (CurrentSession.Windows.Contains(front) || OnGlass(front)))
        {
            return front;
        }

        if (CurrentSession.Focused is long id && CurrentSession.Windows.Contains(id))
        {
            return id;
        }

        return _raised is long raised && _windows.Alive(raised) && OnGlass(raised) ? raised : null;
    }

    private void SendToScratchpad()
    {
        if (ActiveWindow() is not long id)
        {
            return;
        }

        ReleaseSticky(id);
        CurrentSession.Remove(id, HomeArea());
        _scratchpad.Add(id);
        _windows.MarkWorkspace(id, WorkspaceMark.Scratchpad);
        if (_scratchpad.Open)
        {
            _raised = id;
            Arrange();
            return;
        }

        if (_raised == id)
        {
            _raised = null;
        }

        // The arrange hides the window, and takes the keyboard off it first (BeforeHiding).
        Arrange();
    }

    /// <summary>Gives the keyboard to this workspace's focused window, or to no window when it has none.</summary>
    /// <param name="leaving">The window that has the keyboard and is about to be hidden. Null when the keyboard is on a window that is hidden already.</param>
    private void FocusWorkspace(long? leaving = null)
    {
        if (CurrentSession.Focused is long id && FocusArrived(id))
        {
            return;
        }

        // The window that is hidden, or about to be, has the keyboard. Releasing it while Super is down swallows that key-up.
        if (_handoff.ReleaseHidden(_superHeld()))
        {
            Log.Information("Workspace", "focus released");
            if (leaving is long hidden)
            {
                _windows.ReleaseFocus(hidden);
            }
            else
            {
                _windows.ReleaseFocus();
            }
        }
    }

    private void ToggleSticky()
    {
        if (ActiveWindow() is not long id || _scratchpad.Contains(id))
        {
            return;
        }

        if (_sticky.ContainsKey(id))
        {
            ReleaseSticky(id);
            if (!CurrentSession.Windows.Contains(id))
            {
                CurrentSession.Add(id, HomeArea());
            }
        }
        else
        {
            var area = HomeArea();
            var placed = CurrentSession.Arrange(area).FirstOrDefault(item => item.Id == id && item.Visible && item.Width > 0);
            var rect = placed.Id == id
                ? new TileRect(placed.X, placed.Y, placed.Width, placed.Height)
                : new TileRect(area.X + (area.Width / 4), area.Y + (area.Height / 4), Math.Max(1, area.Width / 2), Math.Max(1, area.Height / 2));
            CurrentSession.Remove(id, area);
            _sticky[id] = rect;
            _raised = id;

            // It is on every Workspace now, so it carries none. A desktop that starts finds it a plain window on the Workspace in front.
            _windows.MarkWorkspace(id, WorkspaceMark.None);
        }

        Arrange();
    }

    private void ReleaseSticky(long id)
    {
        if (!_sticky.Remove(id))
        {
            return;
        }

        if (_raised == id)
        {
            _raised = null;
        }
    }

    private static bool SameWindows(List<long> before, List<Front> fronts)
    {
        if (fronts.Count == 1)
        {
            return SameWindows(before, fronts[0].Session.Windows);
        }

        var count = 0;
        foreach (var front in fronts)
        {
            count += front.Session.Windows.Count;
            foreach (var id in front.Session.Windows)
            {
                if (before.IndexOf(id) < 0)
                {
                    return false;
                }
            }
        }

        return count == before.Count;
    }

    private static bool SameWindows(List<long> before, IReadOnlyCollection<long> after)
    {
        if (before.Count != after.Count)
        {
            return false;
        }

        foreach (var id in after)
        {
            if (before.IndexOf(id) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static long DefaultClock() => Environment.TickCount64;

    /// <summary>Runs <paramref name="command"/> the same way a matched chord does, then flushes a dirty layout.</summary>
    /// <param name="command">A command <see cref="ChordCommands.TryParse"/> produced, or one edge of a held plugin command.</param>
    public void Run(ShellCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Execute(command);
        FlushLayout();
    }

    /// <summary>
    /// Asks every window to close, then goes to Workspace 1, as Omarchy's close-all does: the tiles of every Workspace,
    /// the players and Free windows, the scratchpad's windows while it is hidden too, and the Group members behind the one shown.
    /// A window that refuses to close stays as it is, hidden or shown. A window Windows keeps on another virtual desktop is not Winoma's, and is left alone.
    /// </summary>
    public void CloseAll()
    {
        var world = _windows.World();

        // The Host lists a window that is on the glass. A window Winoma hid is not, and one that is fullscreen in its own app
        // has no caption to be listed by. The scratchpad and the Workspaces hold those.
        var ids = world.Tiles.Select(window => window.Id)
            .Concat(world.PictureInPicture)
            .Concat(world.Free)
            .Concat(_scratchpad.Windows)
            .Concat(_freeHome.Keys)
            .Concat(_travel.Sessions.SelectMany(session => session.Windows))
            .Distinct()
            .ToArray();
        foreach (var id in ids)
        {
            _windows.Close(id);
        }

        if (Workspaces.Current == 1)
        {
            Arrange();
            return;
        }

        Run(new ShellCommand.JumpWorkspace(1));
    }

    /// <summary>Focuses the first tracked window whose title contains <paramref name="name"/>.</summary>
    /// <param name="name">Part of the window title.</param>
    /// <returns>False when no window matches.</returns>
    public bool FocusApp(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        foreach (var window in _windows.Snapshot())
        {
            if (window.Title.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                _windows.Focus(window.Id);
                return true;
            }
        }

        return false;
    }

    /// <summary>The monitor with the keyboard: the one the current workspace is in front on.</summary>
    /// <returns>A 0-based monitor index.</returns>
    public int FocusedMonitor() => _travel.Monitor(Workspaces.Current);

    /// <summary>The monitor <paramref name="number"/> is in front on. The Bar there marks it (ADR 0080).</summary>
    /// <param name="number">Workspace number.</param>
    /// <returns>A 0-based monitor index, or -1 for a Workspace that is hidden.</returns>
    public int FrontMonitor(int number) => InFront(number) ? _travel.Monitor(number) : -1;

    /// <summary>Which monitor <paramref name="screen"/> is, as <c>winoma desktop</c> numbers the monitors.</summary>
    /// <param name="screen">A whole monitor, in physical pixels, such as the one a Bar is on.</param>
    /// <returns>A 0-based monitor index. Zero for a monitor the Host does not list.</returns>
    public int MonitorAt(TileRect screen) => BarSet.On(_windows.Screens, screen);

    /// <summary>The monitor with the keyboard: the whole monitor of the current workspace, the bar's strip included.</summary>
    /// <returns>That monitor's rectangle, in physical pixels.</returns>
    public TileRect FrontScreen() => HomeScreen();

    private void Execute(ShellCommand command)
    {
        Log.Information("Shell", Describe(command));
        Adopt();
        if (Workspaces.SeveralInFront)
        {
            // A chord acts where the keyboard is. A window on another monitor that just took it has moved it there.
            FollowKeyboard();
        }

        switch (command)
        {
            case ShellCommand.JumpWorkspace jump:
                Scene++;
                Travel(jump.Number, here: true);
                break;
            case ShellCommand.MoveWindow move:
                MoveFrontWindow(move);
                break;
            case ShellCommand.CycleWorkspace cycle:
                Scene++;
                Travel(CycleTarget(cycle.Cycle), here: true);
                break;
            case ShellCommand.Focus focus:
                FocusToward(focus.Direction);
                break;
            case ShellCommand.Swap swap:
                WithFrontWindow(() => CurrentSession.Swap(swap.Direction, HomeArea()));
                break;
            case ShellCommand.Resize resize:
                WithFrontWindow(() => CurrentSession.Resize(resize.X, resize.Y, HomeArea()));
                break;
            case ShellCommand.GroupStep step:
                WithFrontWindow(() =>
                {
                    CurrentSession.CycleGroup(step.Delta, HomeArea());
                    FocusCurrent();
                });
                break;
            case ShellCommand.GroupIndex index:
                WithFrontWindow(() =>
                {
                    CurrentSession.ShowGroupMember(index.Index, HomeArea());
                    FocusCurrent();
                });
                break;
            case ShellCommand.JoinGroup join:
                WithFrontWindow(() => CurrentSession.JoinNeighbor(join.Direction, HomeArea()));
                break;
            case ShellCommand.MoveMonitor monitor:
                MoveToMonitor(monitor.Direction);
                break;
            case ShellCommand.Window window:
                RunWindow(window.Action);
                break;
            case ShellCommand.Host { Action: HostAction.PrivateBrowser }:
                NoteLaunch();
                OpenPrivateBrowser();
                break;
            case ShellCommand.Host { Action: HostAction.FilesHere }:
                NoteLaunch();
                _actions.OpenFolder(FolderFor(_actions.Front()));
                break;
            case ShellCommand.Host host:
                if (host.Action is HostAction.Terminal or HostAction.Browser or HostAction.Files or HostAction.Calculator or HostAction.Agent)
                {
                    NoteLaunch();
                }

                _actions.Run(host.Action);
                break;
            case ShellCommand.AppRole app:
                NoteLaunch();
                _actions.RunApp(app.Role);
                break;
            case ShellCommand.PluginCommand plugin:
                _actions.RunPlugin(plugin);
                break;
            case ShellCommand.OpenPanel panel:
                _actions.OpenPanel(panel.Id);
                break;
            case ShellCommand.OpenRightPanel right:
                _actions.OpenRightPanel(right.Number);
                break;
            case ShellCommand.Nothing:
                break;
            default:
                Log.Warning("Shell", "unknown command " + command.GetType().Name);
                break;
        }
    }

    private void OpenPrivateBrowser()
    {
        // The browser the Host names wins over the one Windows names.
        var browser = string.IsNullOrWhiteSpace(_settings.Browser) ? _defaultBrowser?.Name() ?? "" : _settings.Browser;
        var flag = PrivateFlag(browser);
        if (flag.Length == 0)
        {
            Log.Information("Shell", "private browser: Winoma knows no private flag for \"" + browser + "\", so it opens plainly");
        }

        _actions.OpenBrowser(browser, flag);
    }

    /// <summary>
    /// The focus chord. Past the edge of the monitor it reaches the window nearest across the edge, on the Workspace in front
    /// on the monitor that way, and the keyboard goes to that monitor. With no window there the keyboard goes to the monitor all the same.
    /// The pointer stays where it is (ADR 0034).
    /// </summary>
    private void FocusToward(Direction direction)
    {
        var area = HomeArea();
        TileRect? origin = null;
        if (UseFrontWindow())
        {
            var before = CurrentSession.Focused;
            var target = Workspaces.SeveralInFront ? _travel.FrontToward(direction) : 0;
            if (CurrentSession.FocusDirection(direction, area) != before || target == 0)
            {
                FocusCurrent();
                Arrange();
                return;
            }

            foreach (var tile in CurrentSession.Arrange(area))
            {
                if (tile.Id == before && tile.Visible)
                {
                    origin = new TileRect(tile.X, tile.Y, tile.Width, tile.Height);
                }
            }
        }

        var across = Workspaces.SeveralInFront ? _travel.FrontToward(direction) : 0;
        if (across == 0)
        {
            return;
        }

        // The window whose tile is nearest the one the keyboard leaves. From an empty Workspace, nearest the middle of its monitor.
        var from = origin ?? area;
        var x = from.X + (from.Width / 2);
        var y = from.Y + (from.Height / 2);
        long? nearest = null;
        var least = long.MaxValue;
        foreach (var tile in _travel.At(across).Arrange(_travel.Area(across)))
        {
            if (!tile.Visible || tile.Width <= 0 || tile.Height <= 0)
            {
                continue;
            }

            long dx = Math.Max(Math.Max(tile.X - x, 0), x - (tile.X + tile.Width));
            long dy = Math.Max(Math.Max(tile.Y - y, 0), y - (tile.Y + tile.Height));
            var distance = (dx * dx) + (dy * dy);
            if (distance < least)
            {
                least = distance;
                nearest = tile.Id;
            }
        }

        _followed = nearest;
        Scene++;
        Travel(across);
    }

    /// <summary>
    /// Moves the Workspace with the keyboard onto the monitor in <paramref name="direction"/>. It is in front there, and the keyboard goes with it.
    /// The windows of the Workspace that was in front there are hidden, and the monitor that was left shows another Workspace.
    /// </summary>
    private void MoveToMonitor(Direction direction)
    {
        var before = new List<int>(2);
        _travel.Fronts(before);
        var from = Workspaces.Current;
        if (!_travel.Move(direction))
        {
            return;
        }

        Scene++;
        Switching = true;
        try
        {
            Switch(before, from);
        }
        finally
        {
            _placedSession = null;
            Switching = false;
        }

        Arrange();
    }

    private void MoveFrontWindow(ShellCommand.MoveWindow move)
    {
        // A scratchpad window leaves the scratchpad for the workspace it is moved to. That is its only way out.
        if (_scratchpad.Open && ActiveWindow() is long shown && _scratchpad.Remove(shown))
        {
            if (_raised == shown)
            {
                _raised = null;
            }

            CurrentSession.Add(shown, HomeArea());
            if (move.Number == Workspaces.Current)
            {
                Arrange();
                return;
            }
        }

        if (!UseFrontWindow() || CurrentSession.Focused is not long id)
        {
            return;
        }

        var from = Workspaces.Current;
        if (move.Number == from)
        {
            Log.Information("Workspace", "move to " + move.Number + " ignored; that workspace has the keyboard");
            return;
        }

        Log.Information("Workspace", "moving window " + id + " from " + from + " to " + move.Number + (move.Follow ? ", following" : ""));
        Scene++;
        _travel.Put(id, move.Number);
        if (move.Follow)
        {
            // It goes along on the glass. The switch places it on its tile there and leaves the keyboard on it.
            _followed = id;
            Travel(move.Number, here: true);
            return;
        }

        if (InFront(move.Number))
        {
            // Sent to the Workspace in front on another monitor. It is drawn there, and the keyboard stays here, on a window left here.
            _placedSession = null;
            if (_windows.Foreground() == id)
            {
                FocusWorkspace(id);
                _keyboardLeft = _windows.Foreground() == id ? id : _keyboardLeft;
            }

            Arrange();
            return;
        }

        // Sent away. The record names it, and the keyboard goes to a window left here, before it is hidden.
        _placedSession = null;
        RecordHiding();
        if (_windows.Foreground() == id)
        {
            FocusWorkspace(id);
        }

        var batch = _batch;
        batch.Clear();
        batch.Add(id);
        HideAway(batch);
        NoteKeptKeyboard(batch);
        Arrange();
    }

    /// <summary>
    /// The Workspace a cycle goes to. Next and previous visit only the Workspaces that exist, on every monitor in number order:
    /// one that holds a window, or one in front. Former is the Workspace that had the keyboard before, on whichever monitor.
    /// </summary>
    private int CycleTarget(WorkspaceCycle cycle)
    {
        // A window that closed while its Workspace was hidden no longer holds that Workspace open.
        _travel.PruneClosed();
        var book = _travel.Book;
        return cycle switch
        {
            WorkspaceCycle.Next => book.Next(),
            WorkspaceCycle.Previous => book.Previous(),
            _ => book.Former,
        };
    }

    /// <summary>
    /// Omarchy's focus on the next monitor, and on the previous. The keyboard goes to the Workspace in front on that monitor,
    /// in the monitors' order, wrapping. Nothing is shown or hidden, and the pointer stays where it is (ADR 0034).
    /// With one monitor nothing moves.
    /// </summary>
    /// <param name="steps">1 for the next monitor, -1 for the previous.</param>
    private void StepMonitor(int steps)
    {
        var target = _travel.FrontBeside(steps);
        if (target == Workspaces.Current)
        {
            return;
        }

        Scene++;
        Travel(target);
    }

    private void RunWindow(WindowAction action)
    {
        var session = CurrentSession;

        // No discard arm: a WindowAction without an arm fails the build (CS8509). A number that names none throws.
#pragma warning disable CS8524
        Action run = action switch
        {
            WindowAction.Close => CloseFront,
            WindowAction.ToggleFloat => ToggleFloat,
            WindowAction.ToggleFullscreen => () => WithFrontWindow(() => ToggleFullscreen(session)),
            WindowAction.ToggleTiledFullscreen => ToggleTiledFullscreen,
            WindowAction.ToggleFullWidth => () => WithFrontWindow(() => session.ToggleFullWidth()),
            WindowAction.ToggleLayout => ToggleLayout,
            WindowAction.ToggleSplit => () => WithFrontWindow(() => session.ToggleSplit(HomeArea())),
            WindowAction.ToggleGroup => () => WithFrontWindow(() => session.ToggleGroup()),
            WindowAction.LeaveGroup => () => WithFrontWindow(() => session.LeaveGroup()),
            WindowAction.TogglePseudo => () => WithFrontWindow(() => session.TogglePseudo(HomeArea())),
            WindowAction.ToggleSpacing => ToggleSpacing,
            WindowAction.ToggleSquareTile => ToggleSquareTile,
            WindowAction.ToggleScratchpad => ToggleScratchpad,
            WindowAction.SendScratchpad => SendToScratchpad,
            WindowAction.ToggleSticky => ToggleSticky,
            WindowAction.ToggleFullscreenDesktop => ToggleFullscreenDesktop,
            WindowAction.NextMonitor => () => StepMonitor(1),
            WindowAction.PreviousMonitor => () => StepMonitor(-1),
            WindowAction.SaveWidth => () => WithFrontWindow(() => session.SaveWidth(HomeArea())),
            WindowAction.RestoreWidth => () => WithFrontWindow(() => session.RestoreWidth(HomeArea())),
        };
#pragma warning restore CS8524
        run();
    }

    private void ToggleLayout()
    {
        CurrentSession.ToggleLayout();
        Arrange();
    }

    /// <summary>
    /// The float chord. A picture-in-picture player that has the keyboard takes a tile: it is no longer kept above the windows
    /// or drawn with every Workspace, and from there it is a window like any other. A player that has a tile is kept above again.
    /// Any other tile is floated, or tiled again (ADR 0076).
    /// </summary>
    private void ToggleFloat()
    {
        if (_windows.Foreground() is long front && _windows.World().PictureInPicture.Contains(front))
        {
            Log.Information("Workspace", "player " + front + " takes a tile");
            _windows.KeepAbove(front, above: false);
            Arrange();
            return;
        }

        if (!UseFrontWindow() || CurrentSession.Focused is not long id)
        {
            return;
        }

        if (WindowAdmission.IsPlayerTitle(_windows.Caption(id)))
        {
            Log.Information("Workspace", "player " + id + " floats above the windows");
            CurrentSession.Remove(id, HomeArea());
            _windows.KeepAbove(id, above: true);
        }
        else
        {
            CurrentSession.ToggleFloat();
        }

        Arrange();
    }

    /// <summary>The fullscreen chord. A window its app keeps over the monitor is left there: Winoma cannot hold it on a tile (ADR 0076).</summary>
    private void ToggleFullscreen(TilingSession session)
    {
        if (session.Focused is long id && _windows.FullscreenTakesMonitor(id) && InApp(id))
        {
            return;
        }

        session.ToggleFullscreen();
    }

    /// <summary>
    /// Omarchy's tiled fullscreen (ADR 0076). Windows has no message that tells an app it is fullscreen, so the app is asked with its own key,
    /// and its window is left on its tile when it answers. A window already fullscreen in its app is asked to leave,
    /// and the window the fullscreen chord put over the monitor is tiled again, as Omarchy's chord does for both.
    /// An app that keeps its fullscreen over the monitor is asked nothing (ADR 0076).
    /// </summary>
    private void ToggleTiledFullscreen()
    {
        if (!UseFrontWindow() || CurrentSession.Focused is not long id || _windows.FullscreenTakesMonitor(id))
        {
            return;
        }

        var session = CurrentSession;
        _tiledAsk = null;
        if (InApp(id))
        {
            _windows.AskFullscreen(id);
            return;
        }

        if (session.Fullscreen == id)
        {
            session.SetFullscreen(id, covers: false);
            Arrange();
            return;
        }

        if (_windows.AskFullscreen(id))
        {
            _tiledAsk = (id, _clock() + TiledAskMs);
        }
    }

    /// <summary>True when <paramref name="id"/> is on screen without a caption, so the census that lists tiles leaves it out: its app made it fullscreen.</summary>
    private bool InApp(long id)
    {
        var tiles = _windows.World().Tiles;
        for (var i = 0; i < tiles.Count; i++)
        {
            if (tiles[i].Id == id)
            {
                return false;
            }
        }

        return _windows.Shown(id);
    }

    /// <summary>True when the tiled fullscreen chord asked for the fullscreen <paramref name="id"/> just entered. The asking is over either way.</summary>
    private bool AskedTiled(long id)
    {
        if (_tiledAsk is not { } ask || ask.Id != id)
        {
            return false;
        }

        _tiledAsk = null;
        return _clock() <= ask.Until;
    }

    private void ToggleScratchpad()
    {
        if (_scratchpad.Open && _scratchpadOver != Workspaces.Current && InFront(_scratchpadOver))
        {
            // There is one scratchpad. Shown on another monitor, it is taken from there and shown on the one with the keyboard.
            Log.Information("Shell", "the scratchpad comes from workspace " + _scratchpadOver + " to workspace " + Workspaces.Current);
            _scratchpadOver = Workspaces.Current;
            _placedSession = null;
            Arrange();
            if (_scratchpad.Windows.Count > 0)
            {
                _raised = _scratchpad.Windows[^1];
                _windows.Activate(_raised.Value);
            }

            return;
        }

        var open = _scratchpad.Toggle();
        if (open)
        {
            _scratchpadOver = Workspaces.Current;
        }

        // Hiding the scratchpad takes the keyboard off its window first (BeforeHiding).
        Arrange();
        if (open)
        {
            // The scratchpad is over the tiles, so the window sent there last takes the keyboard.
            if (_scratchpad.Windows.Count > 0)
            {
                _raised = _scratchpad.Windows[^1];
                _windows.Activate(_raised.Value);
            }

            return;
        }

        if (_raised is long raised && _scratchpad.Contains(raised))
        {
            _raised = null;
        }
    }

    private void ToggleSpacing()
    {
        foreach (var item in _travel.Sessions)
        {
            item.ToggleSpacing();
        }

        _settings.SpacingEnabled = CurrentSession.SpacingEnabled;
        Arrange();
    }

    private void ToggleSquareTile()
    {
        foreach (var item in _travel.Sessions)
        {
            item.ToggleSquareTile();
        }

        _settings.SquareTileEnabled = CurrentSession.SquareTile;
        Arrange();
    }

    private readonly FullscreenDesktop _fullscreenDesktop = new();

    /// <summary>The Bar and the Spacing go together, and come back together (<see cref="FullscreenDesktop"/>). The Host hides and shows the Bar.</summary>
    private void ToggleFullscreenDesktop()
    {
        var now = new DesktopLook(_settings.BarVisible, CurrentSession.SpacingEnabled);
        var next = _fullscreenDesktop.Toggle(now);
        if (next.BarVisible != now.BarVisible)
        {
            _actions.Run(HostAction.ToggleBar);
        }

        if (next.Spacing != now.Spacing)
        {
            ToggleSpacing();
        }
    }

    private static string Describe(ShellCommand command) => command switch
    {
        ShellCommand.JumpWorkspace jump => "jump workspace " + jump.Number,
        ShellCommand.MoveWindow move => "move window to " + move.Number + (move.Follow ? " and follow" : ""),
        ShellCommand.CycleWorkspace cycle => "cycle workspace " + cycle.Cycle,
        ShellCommand.Focus focus => "focus " + focus.Direction,
        ShellCommand.Swap swap => "swap " + swap.Direction,
        ShellCommand.Resize resize => "resize " + resize.X + "," + resize.Y,
        ShellCommand.GroupStep step => "group step " + step.Delta,
        ShellCommand.GroupIndex index => "group member " + index.Index,
        ShellCommand.JoinGroup join => "join group " + join.Direction,
        ShellCommand.MoveMonitor monitor => "move workspace " + monitor.Direction,
        ShellCommand.Window window => window.Action.ToString(),
        ShellCommand.Host host => host.Action.ToString(),
        ShellCommand.AppRole app => "app " + app.Role,
        ShellCommand.PluginCommand plugin => plugin.Phase switch
        {
            BindPhase.Down => "plugin " + plugin.Plugin + " " + plugin.Name + " down",
            BindPhase.Up => "plugin " + plugin.Plugin + " " + plugin.Name + " up",
            _ => "plugin " + plugin.Plugin + " " + plugin.Name,
        },
        ShellCommand.OpenPanel panel => "open panel " + panel.Id,
        ShellCommand.OpenRightPanel index => "open right panel " + index.Number,
        ShellCommand.Nothing => "nothing",
        _ => command.GetType().Name,
    };

    /// <summary>
    /// Hides the scratchpad when the Workspace it was shown over is no longer in front, as Omarchy does
    /// (<c>hide_special_on_workspace_change</c>). Left open, it reads as shown with nothing of it drawn.
    /// </summary>
    private void CloseScratchpad()
    {
        if (!_scratchpad.Open)
        {
            return;
        }

        _scratchpad.Toggle();
        if (_raised is long raised && _scratchpad.Contains(raised))
        {
            _raised = null;
        }

        Log.Information("Shell", "the workspace changed; scratchpad hidden");
    }

    /// <summary>
    /// Gives <paramref name="number"/> the keyboard, in front on its monitor, and brings the glass along when that is a change (<see cref="Switch"/>).
    /// With <paramref name="here"/>, a Workspace that is hidden comes to the monitor with the keyboard first (ADR 0079): a Chord asked for it by number.
    /// </summary>
    private void Travel(int number, bool here = false)
    {
        Switching = true;
        _placedSession = null;
        var from = Workspaces.Current;
        var before = new List<int>(2);
        _travel.Fronts(before);
        try
        {
            _travel.Go(number, here);

            // A jump to the Workspace that already has the keyboard changes nothing, and leaves the scratchpad as it is.
            Switch(before, from);
        }
        finally
        {
            // A workspace change puts other tiles on the glass, so the next arrange places.
            _placedSession = null;
            Switching = false;
        }
    }

    /// <summary>
    /// Brings the Workspace the book now has in front onto the glass and takes the one that was there off it, in one order (ADR 0003):
    /// the new Workspace's windows are placed while they are still hidden, then shown, then one of them takes the keyboard,
    /// and only then are the old ones hidden. A hidden window keeps the keyboard it had, so the keyboard has left before anything is hidden.
    /// The Host is asked for no frame and for no window's answer in between, so the two sets change in one frame.
    /// Everything is read from what Winoma holds. The arrange that follows reads the Host for what opened or closed meanwhile.
    /// </summary>
    /// <remarks>
    /// With several monitors one change can bring a Workspace in front on one and take another off a second, and the keyboard can go
    /// to a Workspace that was in front all along. Then nothing is shown or hidden, and only the keyboard moves.
    /// </remarks>
    /// <param name="before">The Workspaces that were in front, one for each monitor.</param>
    /// <param name="from">The Workspace that had the keyboard.</param>
    /// <returns>False when nothing changed: the same Workspaces are in front, and the same one has the keyboard.</returns>
    private bool Switch(List<int> before, int from)
    {
        var crossed = Workspaces.Current != from;
        var now = new List<int>(2);
        _travel.Fronts(now);
        var changed = before.Count != now.Count;
        foreach (var number in before)
        {
            changed |= !now.Contains(number);
        }

        if (!crossed && !changed)
        {
            return false;
        }

        // The scratchpad hides when the Workspace it is shown over is no longer in front. The keyboard going to another monitor leaves it.
        var scratchpadShown = _scratchpad.Open && !InFront(_scratchpadOver);
        if (scratchpadShown)
        {
            CloseScratchpad();
        }

        _windows.ResetFocusRefusal();

        // The record names a window before it is hidden. It names the ones about to be shown a moment longer.
        RecordHiding();
        var batch = _batch;
        batch.Clear();
        var fronts = Fronts();
        for (var i = 0; i < fronts.Count; i++)
        {
            var front = fronts[i];
            if (before.Contains(front.Number))
            {
                continue;
            }

            var session = front.Session;
            TileFit.Place(session, _windows, front.Area, front.Screen, jump: true, fronts.Count == 1 ? StickyTiles() : WithOthers((IReadOnlyList<Placement>?)Overlay() ?? [], fronts, i));
            foreach (var id in session.Windows)
            {
                // A Group member behind the one shown stays hidden, as the placement left it.
                if (!session.Tucked(id))
                {
                    batch.Add(id);
                    _arriving.Add(id);
                }
            }

            foreach (var pair in _freeHome)
            {
                if (pair.Value == front.Number)
                {
                    batch.Add(pair.Key);
                    _arriving.Add(pair.Key);
                }
            }
        }

        _windows.ShowAll(batch);
        if (crossed)
        {
            TakeFocus();
        }

        batch.Clear();
        foreach (var number in before)
        {
            if (now.Contains(number))
            {
                continue;
            }

            var old = _travel.At(number);
            foreach (var id in old.Windows)
            {
                if (!old.Tucked(id))
                {
                    batch.Add(id);
                }
            }

            foreach (var pair in _freeHome)
            {
                if (pair.Value == number)
                {
                    batch.Add(pair.Key);
                }
            }
        }

        if (scratchpadShown)
        {
            batch.AddRange(_scratchpad.Windows);
        }

        LeaveKeyboard(batch);
        HideAway(batch);
        NoteKeptKeyboard(batch);
        if (crossed)
        {
            NoteKeyboardLeft();
        }

        return true;
    }

    /// <summary>
    /// The keyboard went to a Workspace on another monitor, and is still on a window of the Workspace it left: no window took it there,
    /// as on an empty Workspace, or Windows refused. That window is on the glass, so nothing hides it. The keyboard is dropped, and while
    /// Super is held it waits until Super goes up (ADR 0020). Until it has left, that window is not taken to have asked for the keyboard.
    /// </summary>
    private void NoteKeyboardLeft()
    {
        if (_windows.Foreground() is not long front
            || CurrentSession.Windows.Contains(front)
            || _sticky.ContainsKey(front)
            || !_windows.Shown(front))
        {
            return;
        }

        var home = _scratchpad.Contains(front) ? _scratchpadOver : WorkspaceOf(front);
        if (home == 0 || home == Workspaces.Current || !InFront(home))
        {
            return;
        }

        _keyboardLeft = front;
        if (!_surfaceHeld() && _handoff.ReleaseHidden(_superHeld()))
        {
            Log.Information("Workspace", "focus released");
            _windows.ReleaseFocus(front);
            _keyboardLeft = null;
        }
    }

    /// <summary>
    /// The keyboard is still on a window that is about to be hidden: no window of the Workspace in front took it, as on an empty one.
    /// It is dropped. While Super is held it stays until Super goes up (ADR 0020), and <see cref="SettleKeyboard"/> drops it then.
    /// </summary>
    private void LeaveKeyboard(List<long> leaving)
    {
        if (_windows.Foreground() is not long front || !leaving.Contains(front))
        {
            return;
        }

        if (_handoff.ReleaseHidden(_superHeld()))
        {
            Log.Information("Workspace", "focus released");
            _windows.ReleaseFocus(front);
        }
    }

    /// <summary>
    /// Hides <paramref name="ids"/>, which the record already names. A window that could not be hidden is still on the glass,
    /// so it is on the Workspace in front (ADR 0003): it leaves the Workspace or the Scratchpad that held it, the record lets go of it,
    /// and the next arrange gives it a tile, as it does any window it finds on the glass.
    /// </summary>
    private void HideAway(IReadOnlyList<long> ids)
    {
        var stuck = _windows.HideAll(ids);
        if (stuck.Count == 0)
        {
            return;
        }

        foreach (var id in stuck)
        {
            _travel.Drop(id);
            _scratchpad.Remove(id);
            _freeHome.Remove(id);
            Log.Warning("Workspace", "window " + id + " could not be hidden; it stays with the workspace in front");
        }

        _travel.Recount();

        _placedSession = null;
        _again = _arrangeDepth > 0;
        RecordShown();
    }

    /// <summary>The Sticky windows, each on the rectangle it keeps. They are drawn with every Workspace's tiles, and no switch hides them.</summary>
    private List<Placement>? StickyTiles()
    {
        if (_sticky.Count == 0)
        {
            return null;
        }

        var placements = new List<Placement>(_sticky.Count);
        foreach (var (id, rect) in _sticky)
        {
            placements.Add(new Placement(id, rect.X, rect.Y, rect.Width, rect.Height, true, true));
        }

        return placements;
    }

    private void FocusCurrent()
    {
        if (CurrentSession.Focused is long id)
        {
            _windows.Focus(id);
        }
    }

    private void TakeFocus()
    {
        if (_surfaceHeld())
        {
            // The panel came along to this workspace and keeps typing. This workspace gets the keyboard when it closes.
            _focusAfterSurface = true;
            Log.Information("Workspace", "focus waits for the panel");
            return;
        }

        // A window that was moved here keeps the keyboard, wherever the pointer rests.
        if (_followed is long followed)
        {
            _followed = null;
            if (CurrentSession.Windows.Contains(followed) && FocusArrived(followed))
            {
                _handoff.Clear();
                return;
            }

            // A Free window that was followed here has no tile to be focused by.
            if (_freeHome.ContainsKey(followed) && _windows.Activate(followed))
            {
                _handoff.Clear();
                return;
            }
        }

        var pointer = PointerWindow();
        if (pointer is long under)
        {
            CurrentSession.Focus(under);
            Log.Information("Workspace", "focus window " + under + " under the pointer");
            if (_windows.Activate(under))
            {
                _handoff.Clear();
                return;
            }
        }

        if (CurrentSession.Focused is long id && id != pointer)
        {
            Log.Information("Workspace", "focus window " + id);
            if (_windows.Activate(id))
            {
                _handoff.Clear();
                return;
            }
        }

        if (_windows.ForegroundCloaked() && _handoff.ReleaseShell(_superHeld()))
        {
            Log.Information("Workspace", "focus released");
            _windows.ReleaseFocus();
        }
    }

    /// <summary>True when the keyboard is already on a window of this workspace, or on no window at all.</summary>
    private bool KeyboardHere()
    {
        if (_windows.ForegroundCloaked())
        {
            return false;
        }

        if (_windows.Foreground() is not long id)
        {
            return true;
        }

        return CurrentSession.Windows.Contains(id) || IsOverlay(id);
    }

    /// <summary>Gives <paramref name="id"/> the keyboard. False when it stayed back.</summary>
    private bool FocusArrived(long id)
    {
        CurrentSession.Focus(id);
        if (_surfaceHeld())
        {
            // The panel keeps typing. The window gets the keyboard when it closes.
            _focusAfterSurface = true;
            return false;
        }

        Log.Information("Workspace", "focus window " + id);
        return _windows.Activate(id);
    }

    private long? PointerWindow()
    {
        if (_windows.WindowUnderPointer() is not long under || !CurrentSession.Windows.Contains(under))
        {
            return null;
        }

        return under;
    }
}
