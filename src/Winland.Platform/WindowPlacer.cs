using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Winland.Core.Tiling;

namespace Winland.Platform;

/// <summary>
/// Moves tiled windows so the visible frame sits on the tile and does not cover a neighbor.
/// A window whose tile moved or resized glides there. A window that has never landed on a tile jumps there.
/// </summary>
internal sealed class WindowPlacer
{
    private const uint MoveFlags = Native.SwpNoZOrder | Native.SwpNoActivate;
    private const int MaxBorder = 32;
    private readonly object _gate = new();

    // Held for a whole arrange or tick, moves included, and always taken before _gate. The desktop's thread arranges
    // while the tile clock ticks on the pool, and without this their moves interleave and the sets below are written by both.
    private readonly object _playGate = new();
    private readonly TileMotion _motion = new();
    private readonly Func<long, TileRect?> _frame;
    private readonly Dictionary<long, Native.Rect> _borders = [];
    private Placement[] _latest = [];
    private readonly HashSet<long> _regionCleared = [];
    private readonly HashSet<long> _quieted = [];
    // Windows this placer hid, each with the border it had while it could still be measured.
    private readonly Dictionary<long, Native.Rect> _tucked = [];
    private System.Threading.Timer? _clock;
    private int _busy;
    private int _again;
    private bool _fine;
    private Native.Rect _carryBorder;
    // Tiles that were fullscreen in their own app when they were last placed on their tile. Only these are held there:
    // a tile whose app has just made it fullscreen is left over the monitor, for the shell to cover it with (ADR 0076).
    private readonly HashSet<long> _held = [];
    private readonly ConcurrentQueue<(bool Down, int X, int Y, long Id, bool Floated)> _carryPresses = new();

    // The floated and Sticky windows, which a Carry takes though no tile is under them. Replaced whole, and read without the lock by the mouse hook.
    private long[] _floated = [];
    private readonly HashSet<long> _above = [];
    private readonly List<long> _untop = [];
    private Placement[] _covered = [];
    private readonly Dictionary<long, int> _borderTries = [];
    // Also written by the window motion thread, which holds only _gate.
    private readonly ConcurrentDictionary<long, (TileRect Outer, int Width, int Height)> _aligned = new();

    // The content child of a tile, remembered until the frame changes. Zero means the window has none.
    // Negative means more than one, so the next align still walks the children.
    private readonly ConcurrentDictionary<long, nint> _surface = new();
    private TilePace _armed = TilePace.Parked;
    private volatile TileRect[] _bars = [];

    // Set from any thread when a top-level window moved or the tiles changed. Holds looks at the windows again only then.
    private int _moved = 1;

    // True once the window motion thread has reported a move, so its hook is known to deliver.
    private volatile bool _watched;
    private const uint GwHwndPrev = 3;

    public WindowPlacer() => _frame = Frame;

    /// <summary>
    /// Takes a left press while Super is held on the tile Windows draws at the pointer, or on a floated or Sticky window there.
    /// The window then follows the pointer. Runs inside the mouse hook: it reads the last tiles and the floated windows without the lock
    /// and only queues the press.
    /// </summary>
    /// <param name="x">Pointer x, in pixels.</param>
    /// <param name="y">Pointer y, in pixels.</param>
    /// <returns>True when the press is a carry, so no window sees it.</returns>
    public bool ClaimCarry(int x, int y)
    {
        if (!KeyboardHook.SuperIsHeld)
        {
            return false;
        }

        var tile = TileHit.At(Volatile.Read(ref _latest), x, y);
        var floated = Volatile.Read(ref _floated);
        if (tile is null && floated.Length == 0)
        {
            return false;
        }

        long drawn = Native.GetAncestor(Native.WindowFromPoint(new Native.Point { X = x, Y = y }), Native.GaRoot);
        var isFloated = drawn != 0 && Array.IndexOf(floated, drawn) >= 0;
        if (!TileCarry.Claims(superHeld: true, tile, drawn == 0 ? null : drawn, isFloated))
        {
            return false;
        }

        _carryPresses.Enqueue((true, x, y, drawn, isFloated));
        Wake();
        return true;
    }

    /// <summary>Names the floated and Sticky windows: the ones <see cref="ClaimCarry"/> takes wherever they are.</summary>
    /// <param name="ids">Every such window. The caller may change the collection afterwards.</param>
    public void NoteFloated(IReadOnlyCollection<long> ids) => Volatile.Write(ref _floated, [.. ids]);

    /// <summary>The tiles last applied. Read without the lock, so the keyboard hook can ask.</summary>
    public IReadOnlyList<Placement> Tiles => Volatile.Read(ref _latest);

    /// <summary>True when <paramref name="hwnd"/> has nowhere left to go: it sits on its tile, or it is no longer a shown tile.</summary>
    /// <param name="hwnd">A window.</param>
    /// <returns>False only for a shown tile whose window is still off it.</returns>
    public bool OnTile(nint hwnd)
    {
        foreach (var placement in Volatile.Read(ref _latest))
        {
            if (placement.Id == (long)hwnd)
            {
                return !Shown(placement) || (Native.GetWindowRect(hwnd, out var current) && Near(current, Outer(hwnd, placement)));
            }
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="hwnd"/>'s app keeps a fullscreen window over the monitor. Chromium, and every app built on it,
    /// puts the window back there each time the keyboard moves onto it or off it, and lays its page out for the monitor as it does,
    /// so such a window cannot be held on a tile (ADR 0076). The class is the one Chromium gives its top-level windows.
    /// </summary>
    /// <param name="hwnd">A window.</param>
    public static bool TakesMonitor(nint hwnd)
    {
        Span<char> buffer = stackalloc char[32];
        int length;
        unsafe
        {
            fixed (char* chars = buffer)
            {
                length = Native.GetClassName(hwnd, chars, buffer.Length);
            }
        }

        ReadOnlySpan<char> name = buffer[..Math.Max(length, 0)];
        return name is "Chrome_WidgetWin_1";
    }

    /// <summary>True when <paramref name="hwnd"/> is fullscreen in its own app and that app's fullscreen follows its window, so Winoma can hold it on a tile.</summary>
    /// <param name="hwnd">A window.</param>
    internal static bool HeldInApp(nint hwnd) => hwnd != 0 && InAppFullscreen(hwnd) && !TakesMonitor(hwnd);

    /// <summary>
    /// True when <paramref name="hwnd"/> is fullscreen in an app that keeps it over the monitor. Winoma does not move it, raise it,
    /// or ask whether it sits on its tile: it is where its app puts it until the app leaves fullscreen (ADR 0076).
    /// </summary>
    /// <param name="hwnd">A window.</param>
    internal static bool LeftToItsApp(nint hwnd) => hwnd != 0 && InAppFullscreen(hwnd) && TakesMonitor(hwnd);

    /// <summary>
    /// Puts a tile back on its tile as soon as it moves, ahead of the bar loop, when the app changed it itself.
    /// A tile that is fullscreen in its own app and was placed on its tile that way is moved back, so the frame where it filled the monitor is not drawn.
    /// One that has just gone fullscreen is left where its app put it, and so is one whose app keeps it over the monitor.
    /// A tile that put its resize frame back, such as a terminal leaving its own fullscreen, loses that frame again,
    /// because the frame's invisible border would leave a strip of its tile empty. Runs on the window motion thread.
    /// </summary>
    /// <param name="hwnd">A top-level window that moved or resized.</param>
    public void HoldTile(nint hwnd)
    {
        _watched = true;

        // Most windows that move are no tile: a tooltip, a menu, a dialog. One that is not placed needs no lock.
        var placed = false;
        foreach (var placement in Volatile.Read(ref _latest))
        {
            if (placement.Id == (long)hwnd)
            {
                placed = true;
                break;
            }
        }

        if (!placed)
        {
            return;
        }

        // A tile moved, by its app, by a person, or by this placer. The bar loop asks whether the tiles hold once more.
        Volatile.Write(ref _moved, 1);
        var style = (long)Native.GetWindowLongPtr(hwnd, Native.GwlStyle);
        var appFullscreen = (style & Native.WsCaption) == 0;
        var framed = CaptionStyle.WithoutButtons(style, (style & Native.WsPopup) != 0);
        if ((!appFullscreen && framed == style) || (appFullscreen && TakesMonitor(hwnd)))
        {
            return;
        }

        // A move waits on the window's own thread. One that has stopped answering would hold the lock every arrange needs.
        if (Native.IsHungAppWindow(hwnd))
        {
            return;
        }

        // The style decision is taken under the lock. The move itself is not: SetWindowPos waits on the window,
        // and Arrange needs this lock.
        var restyle = false;
        var gliding = false;
        Placement? home = null;
        lock (_gate)
        {
            if (_motion.Carried == (long)hwnd || (appFullscreen && !_held.Contains((long)hwnd)))
            {
                return;
            }

            foreach (var placement in _latest)
            {
                if (placement.Id != (long)hwnd || !Shown(placement) || placement.Raised)
                {
                    continue;
                }

                restyle = !appFullscreen;
                gliding = _motion.IsGliding((long)hwnd);
                home = placement;
                break;
            }
        }

        if (home is not Placement tile)
        {
            return;
        }

        var patience = Patience(hwnd);
        if (restyle)
        {
            CaptionReturn.Note(hwnd, style, framed);
            Native.SetWindowLongPtr(hwnd, Native.GwlStyle, (nint)framed);
            Native.SetWindowPos(hwnd, 0, 0, 0, 0, 0, MoveFlags | Native.SwpFrameChanged | Native.SwpNoMove | Native.SwpNoSize | patience);
            // The client grew inside the same rect, and the app does not lay its content out again for that.
            Relayout(hwnd);
            ForgetAlignment((long)hwnd);
        }

        // A window easing to a new tile is moved by the tile clock. Snapping it here would fight each frame.
        if (gliding)
        {
            return;
        }

        var target = Outer(hwnd, tile);
        if (Native.GetWindowRect(hwnd, out var current) && !SameOuter(current, target))
        {
            Native.SetWindowPos(hwnd, 0, target.X, target.Y, target.Width, target.Height, MoveFlags | patience | (appFullscreen ? Native.SwpNoSendChanging : 0));
        }
    }

    /// <summary>
    /// Resizes <paramref name="hwnd"/> by a pixel and back. Taking a frame off grows the client inside the same window rect,
    /// and an app such as Windows Terminal keeps its content laid out for the old client until it is resized.
    /// </summary>
    /// <param name="hwnd">A window whose frame just changed.</param>
    internal static void Relayout(nint hwnd)
    {
        if (!Native.GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        var patience = Patience(hwnd);
        Native.SetWindowPos(hwnd, 0, 0, 0, width, height - 1, MoveFlags | Native.SwpNoMove | patience);
        Native.SetWindowPos(hwnd, 0, 0, 0, width, height, MoveFlags | Native.SwpNoMove | patience);
    }

    /// <summary>
    /// Drops <paramref name="id"/> from the tiles last applied, at once. A window that stops being a tile between two placements,
    /// such as a player that floats again, is then no longer held to its tile or to a tile's frame.
    /// </summary>
    /// <param name="id">Window identity.</param>
    public void Release(long id)
    {
        lock (_gate)
        {
            var at = Array.FindIndex(_latest, placement => placement.Id == id);
            if (at < 0)
            {
                return;
            }

            var kept = new Placement[_latest.Length - 1];
            Array.Copy(_latest, 0, kept, 0, at);
            Array.Copy(_latest, at + 1, kept, at, kept.Length - at);
            _latest = kept;
            _held.Remove(id);
        }
    }

    /// <summary>Forgets that <paramref name="id"/>'s content was aligned. Its frame changed, so the next place checks the content again.</summary>
    /// <param name="id">Window identity.</param>
    public void ForgetAlignment(long id)
    {
        _aligned.TryRemove(id, out _);
        _surface.TryRemove(id, out _);
        Volatile.Write(ref _moved, 1);
    }

    /// <summary>
    /// A child window of <paramref name="hwnd"/> moved. When <paramref name="hwnd"/> is a tile and the child is its XAML content bridge,
    /// such as Windows Terminal's, the bridge is pulled onto the client again, because the app can size it after Winoma aligned it.
    /// Chromium's surface is left alone: it keeps it larger than the window on purpose. Runs on the window motion thread.
    /// </summary>
    /// <param name="hwnd">The top-level window.</param>
    /// <param name="child">The child window that moved.</param>
    public void ChildMoved(nint hwnd, nint child)
    {
        var tile = false;
        foreach (var placement in Volatile.Read(ref _latest))
        {
            if (placement.Id == (long)hwnd)
            {
                tile = Shown(placement) && !placement.Raised;
                break;
            }
        }

        if (!tile || Native.GetParent(child) != hwnd || !ContentBridge(child))
        {
            return;
        }

        if (!Native.GetClientRect(hwnd, out var client) || !Native.GetWindowRect(child, out var rect))
        {
            return;
        }

        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        if (width > 0 && height > 0 && (rect.Right - rect.Left != width || rect.Bottom - rect.Top != height))
        {
            // A window that has stopped answering must not stall the motion thread. One that is answering is moved and waited for.
            Native.SetWindowPos(child, 0, 0, 0, width, height, MoveFlags | Patience(hwnd));
        }
    }

    private static bool ContentBridge(nint window)
    {
        Span<char> buffer = stackalloc char[64];
        int length;
        unsafe
        {
            fixed (char* chars = buffer)
            {
                length = Native.GetClassName(window, chars, buffer.Length);
            }
        }

        ReadOnlySpan<char> name = buffer[..Math.Max(length, 0)];
        return name is "Windows.UI.Composition.DesktopWindowContentBridge";
    }

    /// <summary>The release of a press <see cref="ClaimCarry"/> took. Runs inside the mouse hook.</summary>
    /// <param name="x">Pointer x, in pixels.</param>
    /// <param name="y">Pointer y, in pixels.</param>
    public void ReleaseCarry(int x, int y)
    {
        _carryPresses.Enqueue((false, x, y, 0, false));
        Wake();
    }

    /// <summary>Raised when a Super-drag should change the tree. Invoked outside the placer lock.</summary>
    public event Action<TileDrag>? Gesture;

    /// <summary>
    /// Asks each window for the smallest size it accepts. <c>SetWindowPos</c> cannot go below that size.
    /// The answer is the visible frame, the same measure as a tile. A hung window is skipped.
    /// </summary>
    public IReadOnlyList<WindowMinimum> Minimums(IReadOnlyCollection<long> ids)
    {
        var found = new List<WindowMinimum>(ids.Count);
        foreach (var id in ids)
        {
            var hwnd = (nint)id;
            var dpi = Native.GetDpiForWindow(hwnd);
            var info = new Native.MinMaxInfo
            {
                MinTrackSize = new Native.Point
                {
                    X = dpi == 0 ? Native.GetSystemMetrics(Native.SmCxMinTrack) : Native.GetSystemMetricsForDpi(Native.SmCxMinTrack, dpi),
                    Y = dpi == 0 ? Native.GetSystemMetrics(Native.SmCyMinTrack) : Native.GetSystemMetricsForDpi(Native.SmCyMinTrack, dpi),
                },
            };
            if (Native.SendMessageTimeout(hwnd, Native.WmGetMinMaxInfo, 0, ref info, Native.SmtoAbortIfHung, 50, out _) == 0)
            {
                continue;
            }

            var border = Border(hwnd);
            var width = info.MinTrackSize.X - border.Left - border.Right;
            var height = info.MinTrackSize.Y - border.Top - border.Bottom;
            if (width > 0 && height > 0)
            {
                found.Add(new WindowMinimum(id, width, height));
            }
        }

        return found;
    }

    /// <summary>
    /// Windows that took the tile's position but kept a larger size. Only the side that overflowed is
    /// reported. A window still easing, one that has not moved to its tile, or one the pointer may be
    /// resizing says nothing about its minimum.
    /// </summary>
    public IReadOnlyList<WindowMinimum> Overflows(IReadOnlyList<Placement> placements)
    {
        var found = new List<WindowMinimum>();
        if (MouseDown())
        {
            return found;
        }

        foreach (var placement in placements)
        {
            if (!Shown(placement) || Gliding(placement.Id))
            {
                continue;
            }

            var hwnd = (nint)placement.Id;
            if (!Native.GetWindowRect(hwnd, out var rect))
            {
                continue;
            }

            var target = Outer(hwnd, placement);
            var landed = Math.Abs(rect.Left - target.X) <= TileReturn.SettleTolerance
                && Math.Abs(rect.Top - target.Y) <= TileReturn.SettleTolerance;
            if (!landed)
            {
                continue;
            }

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            var wider = width > target.Width + TileReturn.SettleTolerance;
            var taller = height > target.Height + TileReturn.SettleTolerance;
            if (wider || taller)
            {
                var border = Border(hwnd);
                var keptWidth = width - border.Left - border.Right;
                var keptHeight = height - border.Top - border.Bottom;
                found.Add(new WindowMinimum(placement.Id, wider ? keptWidth : 0, taller ? keptHeight : 0));
            }
        }

        return found;
    }

    private bool Gliding(long id)
    {
        lock (_gate)
        {
            return _motion.IsGliding(id);
        }
    }

    /// <summary>The visible tile that contains the point, using the tiles last applied.</summary>
    public long? TileAt(int x, int y)
    {
        Placement[] placed;
        lock (_gate)
        {
            placed = _latest;
        }

        return TileHit.At(placed, x, y);
    }

    /// <summary>Moves a window from this process. The elevated helper calls this for administrator windows.</summary>
    public void Force(nint hwnd, int x, int y, int width, int height)
    {
        // The helper reads one act at a time. A window that has stopped answering must not hold the acts behind it.
        Native.SetWindowPos(hwnd, 0, x, y, width, height, MoveFlags | Patience(hwnd));
        HideBorder(hwnd);
        AlignContent(hwnd);
    }

    /// <summary>True when every placed window still sits on its tile, within the settle tolerance.</summary>
    /// <returns>
    /// True when nothing has been placed, a drag or a placement is in progress, or no tile has moved since the last look.
    /// False when a placed window has left its tile and has not already refused to go back.
    /// </returns>
    public bool Holds()
    {
        // The window motion thread holds this lock while it moves a tile back, and the tile clock while it reads the tiles,
        // and a move waits on the window's program. A lock that is taken means a placement is in progress,
        // so the desktop's thread does not wait for it and asks again on its next turn.
        if (!Monitor.TryEnter(_gate))
        {
            return true;
        }

        try
        {
            if (_motion.Carried is not null)
            {
                return true;
            }

            if (_latest.Length == 0)
            {
                return true;
            }

            // The bar loop asks on every turn, and looking costs about six calls per shown tile. A tile leaves its tile by moving,
            // which the window motion thread reports, or by being shown or hidden, which moves the generation.
            // Without either, the last look still stands. It is trusted for five seconds, and not at all until the motion thread has reported once.
            var generation = WindowSignals.Generation;
            var now = Now();
            var moved = Interlocked.Exchange(ref _moved, 0) != 0;
            if (_motion.Quiet(generation, now, moved, _watched))
            {
                return true;
            }

            var again = false;
            foreach (var placement in _latest)
            {
                var hwnd = (nint)placement.Id;
                if (!Shown(placement))
                {
                    // A hidden tile is cloaked, and Windows still calls a cloaked window visible.
                    if (ShellBridge.Hiding.Showing(hwnd))
                    {
                        Volatile.Write(ref _moved, 1);
                        return false;
                    }

                    continue;
                }

                // Over the monitor, where its app keeps it, whatever its tile is.
                if (LeftToItsApp(hwnd))
                {
                    continue;
                }

                // A window that has stopped answering takes its move when it wakes. Asking again every turn would not hurry it.
                if (Native.IsHungAppWindow(hwnd))
                {
                    again = true;
                    continue;
                }

                if (_frame(placement.Id) is not TileRect frame)
                {
                    Volatile.Write(ref _moved, 1);
                    return false;
                }

                if (_motion.OffTile(placement, frame))
                {
                    Volatile.Write(ref _moved, 1);
                    return false;
                }
            }

            if (again)
            {
                Volatile.Write(ref _moved, 1);
            }
            else
            {
                _motion.Noted(generation, now);
            }

            return true;
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    public void Apply(IReadOnlyList<Placement> placements, IReadOnlyList<long> free)
    {
        lock (_playGate)
        {
            List<Move> moves;
            List<nint>? surfacing;
            lock (_gate)
            {
                _latest = placements.ToArray();
                NoteHeld();
                _motion.Arranged();
                Volatile.Write(ref _moved, 1);
                surfacing = Surfacing();
                EnsureClock();
                _borders.Clear();
                var step = _motion.Arrange(_latest, Facts(KeyboardHook.SuperIsHeld), _frame);
                moves = Perform(step);
                DropClosed();
                Arm(step.Pace);
            }

            Surface(surfacing);
            Play(moves);
            Cover();
            RestoreRegions();
            AlignContent();
        }

        // Outside the lock: it touches nothing of the placer's, and a free window that has stopped answering must not hold the tile clock.
        LiftFree(free);
    }

    /// <summary>Lands on <paramref name="placements"/> in one step. A workspace change uses this instead of a glide.</summary>
    /// <param name="placements">Tiles to land on, with no glide.</param>
    /// <param name="free">Free windows from the census that decided these tiles.</param>
    public void PlaceNow(IReadOnlyList<Placement> placements, IReadOnlyList<long> free)
    {
        lock (_playGate)
        {
            List<Move> moves;
            List<nint>? surfacing;
            lock (_gate)
            {
                _latest = placements.ToArray();
                NoteHeld();
                _motion.Arranged();
                Volatile.Write(ref _moved, 1);
                surfacing = Surfacing();
                EnsureClock();
                _borders.Clear();
                var step = _motion.Land(_latest, Facts(KeyboardHook.SuperIsHeld), _frame);
                moves = Perform(step);
                DropClosed();
                Arm(step.Pace);
            }

            Surface(surfacing);
            Play(moves);
            Cover();
            RestoreRegions();
            AlignContent();
        }

        LiftFree(free);
    }

    /// <summary>Writes down which of the tiles just applied are fullscreen in their own app while they sit on a tile. Called under the lock.</summary>
    private void NoteHeld()
    {
        _held.Clear();
        foreach (var placement in _latest)
        {
            if (Shown(placement) && !placement.Raised && HeldInApp((nint)placement.Id))
            {
                _held.Add(placement.Id);
            }
        }
    }

    /// <summary>The flag that makes a move return at once, for a window that has stopped answering. Any other window is moved and waited for.</summary>
    private static uint Patience(nint hwnd) => Native.IsHungAppWindow(hwnd) ? Native.SwpAsyncWindowPos : 0;

    /// <summary>
    /// The windows this placer hid as inactive group members that have a tile again. Called under the lock.
    /// A tile that kept its rectangle gets no move, so the show cannot ride on one.
    /// </summary>
    private List<nint>? Surfacing()
    {
        if (_tucked.Count == 0)
        {
            return null;
        }

        foreach (var id in _tucked.Keys.ToArray())
        {
            if (!Native.IsWindow((nint)id))
            {
                _tucked.Remove(id);
            }
        }

        List<nint>? found = null;
        foreach (var placement in _latest)
        {
            if (Shown(placement) && _tucked.Remove(placement.Id))
            {
                found ??= [];
                found.Add((nint)placement.Id);
            }
        }

        return found;
    }

    /// <summary>
    /// Shows <paramref name="windows"/> the way each was hidden, without giving them the keyboard.
    /// Outside the lock, since a show asks Explorer or waits on the window's thread.
    /// </summary>
    private static void Surface(List<nint>? windows)
    {
        if (windows is null)
        {
            return;
        }

        foreach (var hwnd in windows)
        {
            // A window the shell showed first, as it does before it gives one the keyboard, is off the ledger and is left alone.
            ShellBridge.Hiding.Show(hwnd);
        }
    }

    /// <summary>
    /// Polls a Super-drag on the thread pool. The hook calls this while Super is held so the clock is already running.
    /// It returns at once. Moving a window waits on that window's thread, and the keyboard hook must not.
    /// </summary>
    public void Wake()
    {
        EnsureClock();
        ThreadPool.UnsafeQueueUserWorkItem(_ => OnClock(null), null);
    }

    private void OnClock(object? state)
    {
        // A wake that lands during a tick is kept. The hook wakes the clock once per Super press,
        // and a tick that sampled Super before that press would otherwise park the clock for the whole hold.
        // Only the thread that holds _busy clears the flag, so a wake that finds the clock busy leaves it for that thread.
        Interlocked.Exchange(ref _again, 1);
        while (Volatile.Read(ref _again) == 1 && Interlocked.CompareExchange(ref _busy, 1, 0) == 0)
        {
            try
            {
                while (Interlocked.Exchange(ref _again, 0) == 1)
                {
                    Tick();
                }
            }
            catch (Exception ex)
            {
                // Nothing above a pool thread catches. A tick that fails is logged, and the next one starts clean.
                Log.Error("Tiling", "tile clock failed", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }
    }

    private void Tick()
    {
        // One sample. The drag poll, the idle return, and the coast decision must agree.
        var superHeld = KeyboardHook.SuperIsHeld;
        TileDrag? drag;
        lock (_playGate)
        {
            List<Move> moves;
            bool waiting;
            lock (_gate)
            {
                _borders.Clear();
                var facts = Facts(superHeld) with { RightDown = RightDown(), Pointer = Pointer(), Presses = Presses() };
                var step = _motion.Tick(facts, _frame);
                if (step.Kind == TileStepKind.Idle)
                {
                    Park();
                    return;
                }

                drag = step.Gesture;

                // Super alone, or a coast with nothing moving: the clock only waits for the right button.
                if (step.Moves.Count == 0 && step.Gliding.Count == 0)
                {
                    moves = NoMoves;
                    Arm(step.Pace);
                    waiting = true;
                }
                else
                {
                    moves = Perform(step);
                    Arm(step.Pace);
                    waiting = step.Kind == TileStepKind.Held && moves.Count == 0 && !facts.MouseDown;
                    if (moves.Count > 0)
                    {
                        Volatile.Write(ref _moved, 1);
                    }
                }
            }

            if (!waiting)
            {
                Play(moves);
                Cover();
                AlignContent(moves);
            }
        }

        if (drag is TileDrag gesture)
        {
            Gesture?.Invoke(gesture);
        }
    }

    /// <summary>What <see cref="TileMotion"/> is told for one step. The button is sampled once, so the step agrees with itself.</summary>
    private static TileFacts Facts(bool superHeld) =>
        new(Now(), superHeld, MouseDown());

    /// <summary>
    /// The clock <see cref="TileMotion"/> is told, in milliseconds. <c>Environment.TickCount64</c> steps by 15 or 16 ms whatever the timer period,
    /// so every second tick of the 8 ms clock read the time of the tick before it and drew the same sample of a glide again.
    /// </summary>
    private static long Now() => (long)System.Diagnostics.Stopwatch.GetElapsedTime(0).TotalMilliseconds;

    private static TilePoint? Pointer() =>
        Native.GetCursorPos(out var point) ? new TilePoint(point.X, point.Y) : null;

    /// <summary>
    /// The presses the mouse hook queued, each with the pressed window's frame. A pressed window is lifted above the other tiles,
    /// and its border is kept so the carry is drawn with it.
    /// </summary>
    private IReadOnlyList<TilePress> Presses()
    {
        List<TilePress>? presses = null;
        while (_carryPresses.TryDequeue(out var press))
        {
            presses ??= [];
            if (!press.Down)
            {
                presses.Add(new TilePress(false, press.X, press.Y));
                continue;
            }

            var hwnd = (nint)press.Id;
            if (!Native.GetWindowRect(hwnd, out var rect))
            {
                continue;
            }

            _carryBorder = Border(hwnd);
            presses.Add(new TilePress(true, press.X, press.Y, press.Id, Inner(rect, _carryBorder), press.Floated));
            // Above the other tiles while it crosses them. No activation: the keyboard stays where it was.
            Native.SetWindowPos(hwnd, 0, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Patience(hwnd));
        }

        return presses ?? (IReadOnlyList<TilePress>)[];
    }

    /// <summary>
    /// A tile's visible frame, for the step in progress. The border it was measured with is kept,
    /// so a glide that step answers is drawn with the same border.
    /// </summary>
    private TileRect? Frame(long id)
    {
        var hwnd = (nint)id;
        HideBorder(hwnd);
        if (!Native.GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        var border = Border(hwnd);
        _borders[id] = border;
        return Inner(rect, border);
    }

    /// <summary>The visible frame of a window rect: the rect without the border.</summary>
    private static TileRect Inner(Native.Rect rect, Native.Rect border) =>
        new(
            rect.Left + border.Left,
            rect.Top + border.Top,
            rect.Right - rect.Left - border.Left - border.Right,
            rect.Bottom - rect.Top - border.Top - border.Bottom);

    /// <summary>
    /// Turns the moves <see cref="TileMotion"/> answered into window moves. A frame gains the window's border,
    /// and a move that would change nothing is left out.
    /// </summary>
    private List<Move> Perform(TileStep step)
    {
        foreach (var id in step.Gliding)
        {
            var hwnd = (nint)id;
            ReleaseChrome(hwnd);
            HideBorder(hwnd);
        }

        var moves = new List<Move>(step.Moves.Count);
        foreach (var move in step.Moves)
        {
            var hwnd = (nint)move.Id;
            switch (move.Kind)
            {
                case TileMoveKind.Hide:
                    var showing = ShellBridge.Hiding.Showing(hwnd);
                    if (showing)
                    {
                        _tucked[move.Id] = Border(hwnd);
                    }

                    // A hidden window holds its tile's rectangle, so it comes back at that size and not at the one it left with.
                    var kept = Outer(move.Rect, _tucked.TryGetValue(move.Id, out var edge) ? edge : default);
                    var off = move.Rect.Width > 0 && move.Rect.Height > 0 && Native.GetWindowRect(hwnd, out var lies) && !SameOuter(lies, kept);
                    if (showing || off)
                    {
                        moves.Add(off ? new Move(hwnd, false, kept.X, kept.Y, kept.Width, kept.Height) : new Move(hwnd, false, 0, 0, 0, 0));
                    }

                    break;
                case TileMoveKind.Land:
                    AddInstant(moves, hwnd, move.Rect, move.Raised);
                    break;
                case TileMoveKind.Glide:
                    var outer = Outer(move.Rect, _borders.TryGetValue(move.Id, out var border) ? border : Border(hwnd));
                    moves.Add(new Move(hwnd, true, outer.X, outer.Y, outer.Width, outer.Height, move.Raised));
                    break;
                case TileMoveKind.Carry:
                    var carried = Outer(move.Rect, _carryBorder);
                    moves.Add(new Move(hwnd, true, carried.X, carried.Y, carried.Width, carried.Height));
                    break;
            }
        }

        return moves;
    }

    /// <summary>
    /// Makes the tile clock once. The hook's thread asks without the lock while the desktop's thread asks under it,
    /// and a second clock that lost the field would keep the pace it was armed with and tick on with nothing to stop it.
    /// </summary>
    private void EnsureClock()
    {
        if (Volatile.Read(ref _clock) is not null)
        {
            return;
        }

        var clock = new System.Threading.Timer(OnClock, null, Timeout.Infinite, Timeout.Infinite);
        if (Interlocked.CompareExchange(ref _clock, clock, null) is not null)
        {
            clock.Dispose();
        }
    }

    private static readonly List<Move> NoMoves = [];

    // The 8 ms glide clock. The period matches the clock, so a glide does not ask the system for a 1 ms timer.
    private const uint FinePeriod = 8;

    private void Arm(TilePace pace)
    {
        if (pace == _armed)
        {
            return;
        }

        switch (pace)
        {
            case TilePace.Fine:
                RaisePeriod();
                break;
            case TilePace.Coast:
                Coast();
                break;
            default:
                Park();
                break;
        }
    }

    private void RaisePeriod()
    {
        if (!_fine)
        {
            _fine = true;
            _ = Native.TimeBeginPeriod(FinePeriod);
        }

        _armed = TilePace.Fine;
        _clock?.Change(FinePeriod, FinePeriod);
    }

    /// <summary>16ms while a drag is held. The fine timer period stays off.</summary>
    private void Coast()
    {
        if (_fine)
        {
            _fine = false;
            _ = Native.TimeEndPeriod(FinePeriod);
        }

        _armed = TilePace.Coast;
        _clock?.Change(16, 16);
    }

    /// <summary>Stops the tile clock once nothing is dragging or easing.</summary>
    private void Park()
    {
        if (_fine)
        {
            _fine = false;
            _ = Native.TimeEndPeriod(FinePeriod);
        }

        _armed = TilePace.Parked;
        _clock?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Forgets windows after the tiles changed. What was done once to a window, its region, its border, its transitions, and its content,
    /// is kept while the window lives: a workspace change takes its tiles off the glass and brings them back, and doing it all again
    /// redraws every window on the workspace arrived at. What belongs to the tiles on the glass goes with them. Called under the lock.
    /// </summary>
    private void DropClosed()
    {
        if (_regionCleared.Count == 0
            && _quieted.Count == 0
            && _above.Count == 0
            && _borderTries.Count == 0
            && _aligned.IsEmpty
            && _motion.Refused == 0)
        {
            return;
        }

        DropDead(_regionCleared);
        DropDead(_quieted);
        DropDead(_borderTries);
        foreach (var id in _aligned.Keys)
        {
            if (!Native.IsWindow((nint)id))
            {
                _aligned.TryRemove(id, out _);
                _surface.TryRemove(id, out _);
            }
        }

        var live = new HashSet<long>(_latest.Length);
        foreach (var placement in _latest)
        {
            live.Add(placement.Id);
        }

        _motion.Keep(live);
        if (_above.Count == 0)
        {
            return;
        }
        List<long>? gone = null;
        foreach (var id in _above)
        {
            if (!live.Contains(id))
            {
                gone ??= [];
                gone.Add(id);
            }
        }

        if (gone is null)
        {
            return;
        }

        foreach (var id in gone)
        {
            _above.Remove(id);
            _untop.Add(id);
        }
    }

    private static void DropDead(HashSet<long> ids)
    {
        if (ids.Count > 0)
        {
            ids.RemoveWhere(static id => !Native.IsWindow((nint)id));
        }
    }

    private static void DropDead<T>(Dictionary<long, T> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        List<long>? gone = null;
        foreach (var id in ids.Keys)
        {
            if (!Native.IsWindow((nint)id))
            {
                gone ??= [];
                gone.Add(id);
            }
        }

        if (gone is null)
        {
            return;
        }

        foreach (var id in gone)
        {
            ids.Remove(id);
        }
    }

    /// <summary>
    /// Gives each window its whole painted area back, once. An earlier Winoma process may have
    /// clipped it to a tile, and the clip outlives that process.
    /// </summary>
    private void RestoreRegions()
    {
        Placement[] placed;
        lock (_gate)
        {
            placed = _latest;
        }

        foreach (var placement in placed)
        {
            // A window that has stopped answering is asked on a later arrange.
            if (Shown(placement) && !_regionCleared.Contains(placement.Id) && !Native.IsHungAppWindow((nint)placement.Id))
            {
                _regionCleared.Add(placement.Id);
                _ = Native.SetWindowRgn((nint)placement.Id, 0, true);
            }
        }
    }

    /// <summary>
    /// Pulls a window's composition surface onto its client. Windows Terminal and Chromium paint through
    /// a child window, and moving the outer window can leave that child behind, so the rest of the tile stays empty.
    /// </summary>
    private void AlignContent()
    {
        Placement[] placed;
        lock (_gate)
        {
            placed = _latest;
        }

        foreach (var placement in placed)
        {
            if (Shown(placement))
            {
                AlignContent((nint)placement.Id);
            }
        }
    }

    /// <summary>Aligns the content of the windows a tick moved. The others have not moved since they were aligned.</summary>
    private void AlignContent(List<Move> moves)
    {
        foreach (var move in moves)
        {
            if (move.Show)
            {
                AlignContent(move.Hwnd);
            }
        }
    }

    /// <summary>What <see cref="AlignChild"/> is told, handed to it by address for the length of one enumeration.</summary>
    private struct Alignment
    {
        public Native.Point Origin;
        public int Width;
        public int Height;
        public bool Aligned;
        public nint Child;
        public int Surfaces;
    }

    // One delegate for the life of the process. A fresh lambda per window built a new native thunk for every moving tile on every tick of a glide.
    private static readonly Native.EnumWindowsProc AlignChild = static (child, state) =>
    {
        if (!ContentSurface(child) || !Native.GetWindowRect(child, out var rect))
        {
            return true;
        }

        unsafe
        {
            var want = (Alignment*)state;
            want->Surfaces++;
            if (want->Surfaces == 1)
            {
                want->Child = child;
            }

            var same = rect.Left == want->Origin.X
                && rect.Top == want->Origin.Y
                && rect.Right - rect.Left == want->Width
                && rect.Bottom - rect.Top == want->Height;
            if (!same && !Native.SetWindowPos(child, 0, 0, 0, want->Width, want->Height, MoveFlags | Patience(child)))
            {
                want->Aligned = false;
            }
        }

        return true;
    };

    private void AlignContent(nint window)
    {
        if (!Native.GetWindowRect(window, out var outer))
        {
            return;
        }

        if (!Native.GetClientRect(window, out var client))
        {
            return;
        }

        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;

        // The client can change inside the same outer rect, such as when a frame is taken off. That moves the content too.
        var bounds = (new TileRect(outer.Left, outer.Top, outer.Right - outer.Left, outer.Bottom - outer.Top), width, height);
        if (_aligned.TryGetValue((long)window, out var last) && last == bounds)
        {
            return;
        }

        if (width <= 0 || height <= 0)
        {
            return;
        }

        var origin = default(Native.Point);
        if (!Native.ClientToScreen(window, ref origin))
        {
            return;
        }

        var id = (long)window;
        if (_surface.TryGetValue(id, out var known) && known != -1)
        {
            if (known == 0)
            {
                _aligned[id] = bounds;
                return;
            }

            if (Native.IsWindow(known) && Native.GetParent(known) == window)
            {
                if (PlaceSurface(known, origin, width, height))
                {
                    _aligned[id] = bounds;
                }

                return;
            }

            _surface.TryRemove(id, out _);
        }

        // The enumeration calls back before it returns, so the local outlives every use of its address.
        var want = new Alignment { Origin = origin, Width = width, Height = height, Aligned = true };
        unsafe
        {
            Native.EnumChildWindows(window, AlignChild, (nint)(&want));
        }

        if (want.Aligned)
        {
            _aligned[id] = bounds;
            _surface[id] = want.Surfaces switch
            {
                0 => 0,
                1 => want.Child,
                _ => -1,
            };
        }
    }

    /// <summary>Pulls one remembered content child onto the client. False when the move did not land.</summary>
    private static bool PlaceSurface(nint child, Native.Point origin, int width, int height)
    {
        if (!Native.GetWindowRect(child, out var rect))
        {
            return false;
        }

        var same = rect.Left == origin.X
            && rect.Top == origin.Y
            && rect.Right - rect.Left == width
            && rect.Bottom - rect.Top == height;
        return same || Native.SetWindowPos(child, 0, 0, 0, width, height, MoveFlags | Patience(child));
    }

    private const uint LiftFlags = Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate;

    /// <summary>Puts dialogs and other free windows above the tiles so a notice is not buried.</summary>
    /// <param name="free">Free windows from the census that placed the tiles. This does not enumerate.</param>
    private void LiftFree(IReadOnlyList<long> free)
    {
        if (free.Count == 0)
        {
            return;
        }

        var tiles = Volatile.Read(ref _latest);
        foreach (var id in free)
        {
            var hwnd = (nint)id;

            // A lift waits on the window's program, and one arrange places up to four times. A window no tile is drawn above is where a lift would put it.
            if (!AboveTiles(hwnd, tiles))
            {
                RecordRaise(id, Native.SetWindowPos(hwnd, 0, 0, 0, 0, 0, LiftFlags | Patience(hwnd)));
            }
        }
    }

    /// <summary>True when no shown tile is drawn above <paramref name="hwnd"/>. False when one is, or when there are too many windows above it to tell.</summary>
    private static bool AboveTiles(nint hwnd, Placement[] tiles)
    {
        var above = hwnd;
        for (var step = 0; step < 512; step++)
        {
            above = Native.GetWindow(above, GwHwndPrev);
            if (above == 0)
            {
                return true;
            }

            foreach (var placement in tiles)
            {
                if (placement.Id == (long)above && Shown(placement))
                {
                    return false;
                }
            }
        }

        return false;
    }

    // Asked for every child of every moving tile on each tick of a glide, so it reads the name on the stack.
    private static bool ContentSurface(nint window)
    {
        Span<char> buffer = stackalloc char[80];
        int length;
        unsafe
        {
            fixed (char* chars = buffer)
            {
                length = Native.GetClassName(window, chars, buffer.Length);
            }
        }

        ReadOnlySpan<char> name = buffer[..Math.Max(length, 0)];
        return name is "Windows.UI.Composition.DesktopWindowContentBridge" or "Intermediate D3D Window";
    }

    /// <summary>
    /// The window rect whose painted area covers the tile. Windows 11 keeps an invisible resize
    /// border outside the visible frame, and <see cref="HideBorder"/> leaves the frame's own edge unpainted.
    /// </summary>
    /// <summary>
    /// The rectangle <paramref name="hwnd"/> covers as a tile: its window rectangle without the border that reaches past what it paints.
    /// A window that sits on its tile gives that tile back. Null for a missing or minimized window.
    /// </summary>
    public static TileRect? Frame(nint hwnd)
    {
        if (hwnd == 0 || Native.IsIconic(hwnd) || !Native.GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        var border = Border(hwnd);
        return new TileRect(
            rect.Left + border.Left,
            rect.Top + border.Top,
            rect.Right - rect.Left - border.Left - border.Right,
            rect.Bottom - rect.Top - border.Top - border.Bottom);
    }

    /// <summary>
    /// The window rectangle that puts <paramref name="hwnd"/>, which is not shown yet, on <paramref name="tile"/>.
    /// DWM may not have measured a window that was never drawn. Its border is then the sizing border its style gives it,
    /// which is what DWM reports once it is drawn. The arrange that follows the show measures again.
    /// </summary>
    internal static TileRect OuterUnshown(nint hwnd, TileRect tile)
    {
        var border = Border(hwnd);
        var measured = border.Left != 0 || border.Right != 0 || border.Bottom != 0;
        if (!measured && ((long)Native.GetWindowLongPtr(hwnd, Native.GwlStyle) & Native.WsThickFrame) != 0)
        {
            var dpi = Native.GetDpiForWindow(hwnd);
            var edge = Native.GetSystemMetricsForDpi(SmCxSizeFrame, dpi) + Native.GetSystemMetricsForDpi(SmCxPaddedBorder, dpi);
            if (edge is > 0 and <= MaxBorder)
            {
                border = new Native.Rect { Left = edge, Top = border.Top, Right = edge, Bottom = edge };
            }
        }

        Log.Debug("Tiling", "window " + hwnd + " not shown yet: border " + border.Left + "," + border.Top + "," + border.Right + "," + border.Bottom + (measured ? "" : " from its style"));
        return Outer(tile, border);
    }

    private const int SmCxSizeFrame = 32;
    private const int SmCxPaddedBorder = 92;

    private static TileRect Outer(nint hwnd, Placement placement) =>
        Outer(new TileRect(placement.X, placement.Y, placement.Width, placement.Height), Border(hwnd));

    private static TileRect Outer(TileRect frame, Native.Rect border) =>
        new(
            frame.X - border.Left,
            frame.Y - border.Top,
            frame.Width + border.Left + border.Right,
            frame.Height + border.Top + border.Bottom);

    /// <summary>
    /// How far the window rect reaches past what the window paints, on each side. Zero when DWM does not say.
    /// The top still extends by one pixel when the client already meets the frame, so that clear pixel
    /// sits under the bar instead of showing the wallpaper.
    /// </summary>
    private static Native.Rect Border(nint hwnd)
    {
        if (!Native.GetWindowRect(hwnd, out var window)
            || Native.DwmGetWindowRect(hwnd, Native.DwmwaExtendedFrameBounds, out var frame, Marshal.SizeOf<Native.Rect>()) != 0)
        {
            return default;
        }

        var border = new Native.Rect
        {
            Left = frame.Left - window.Left,
            Top = frame.Top - window.Top,
            Right = window.Right - frame.Right,
            Bottom = window.Bottom - frame.Bottom,
        };
        if (Native.DwmGetWindowAttribute(hwnd, Native.DwmwaVisibleFrameBorderThickness, out var edge, sizeof(int)) == 0 && edge is > 0 and <= MaxBorder)
        {
            border.Left += edge;
            border.Right += edge;
            border.Bottom += edge;
            var origin = default(Native.Point);
            var below = !Native.ClientToScreen(hwnd, ref origin) || origin.Y > frame.Top;
            border.Top += FrameEdge.Top(below, edge);
        }

        var sane = border.Left is >= 0 and <= MaxBorder
            && border.Top is >= 0 and <= MaxBorder
            && border.Right is >= 0 and <= MaxBorder
            && border.Bottom is >= 0 and <= MaxBorder;
        return sane ? border : default;
    }

    private void AddInstant(List<Move> moves, nint hwnd, TileRect tile, bool raised)
    {
        // The step measured the border before this. A window that was minimized or maximized then has another border now.
        var restored = ReleaseChrome(hwnd);

        HideBorder(hwnd);
        var target = Outer(tile, !restored && _borders.TryGetValue((long)hwnd, out var border) ? border : Border(hwnd));
        // Equality, not the settle tolerance. The top cover is one pixel, and the tolerance treats that as already placed.
        if (Native.GetWindowRect(hwnd, out var current) && SameOuter(current, target))
        {
            return;
        }

        moves.Add(new Move(hwnd, true, target.X, target.Y, target.Width, target.Height, raised));
    }

    private static bool Shown(Placement placement) =>
        placement.Visible && placement.Width > 0 && placement.Height > 0;

    private static bool SameOuter(Native.Rect current, TileRect target) =>
        current.Left == target.X
        && current.Top == target.Y
        && current.Right - current.Left == target.Width
        && current.Bottom - current.Top == target.Height;

    private static bool Near(Native.Rect current, TileRect target) =>
        Math.Abs(current.Left - target.X) <= TileReturn.SettleTolerance
        && Math.Abs(current.Top - target.Y) <= TileReturn.SettleTolerance
        && Math.Abs(current.Right - current.Left - target.Width) <= TileReturn.SettleTolerance
        && Math.Abs(current.Bottom - current.Top - target.Height) <= TileReturn.SettleTolerance;

    private static bool MouseDown() => (Native.GetAsyncKeyState(0x01) & 0x8000) != 0;

    private static bool RightDown() => (Native.GetAsyncKeyState(0x02) & 0x8000) != 0;

    private void Quiet(nint hwnd)
    {
        if (hwnd == 0 || !_quieted.Add((long)hwnd))
        {
            return;
        }

        var disable = 1;
        _ = Native.DwmSetWindowAttribute(hwnd, Native.DwmwaTransitionsForcedDisabled, ref disable, sizeof(int));
    }

    private void Play(List<Move> moves)
    {
        if (moves.Count == 0)
        {
            return;
        }

        nint deferred = 0;
        foreach (var move in moves)
        {
            Quiet(move.Hwnd);
            // A move waits on the window's own thread. A window that has stopped answering is asked without the wait,
            // so it cannot hold the desktop's thread or the other tiles' moves.
            var hung = Native.IsHungAppWindow(move.Hwnd);
            if (!move.Show)
            {
                deferred = End(deferred);

                // The cloak, or SW_HIDE where the cloak is not used (ADR 0059). The shell moved the keyboard off the window before this.
                if (ShellBridge.Hiding.Showing(move.Hwnd))
                {
                    ShellBridge.Hiding.Hide(move.Hwnd);
                }

                if (move.Width > 0 && move.Height > 0)
                {
                    Native.SetWindowPos(move.Hwnd, 0, move.X, move.Y, move.Width, move.Height, MoveFlags | (hung ? Native.SwpAsyncWindowPos : 0));
                }

                continue;
            }

            // Fullscreen in an app that keeps it over the monitor: any move is answered with the monitor, and its page is laid out again.
            if (LeftToItsApp(move.Hwnd))
            {
                continue;
            }

            var flags = Flags(move) | (hung ? Native.SwpAsyncWindowPos : 0);
            var alone = hung || (flags & Native.SwpNoSendChanging) != 0;
            if (!alone && deferred == 0)
            {
                deferred = Native.BeginDeferWindowPos(moves.Count);
            }

            // A deferred batch refuses SWP_NOSENDCHANGING, so an app's own fullscreen moves on its own.
            if (alone || deferred == 0)
            {
                var succeeded = Native.SetWindowPos(move.Hwnd, Layer(move), move.X, move.Y, move.Width, move.Height, flags);
                RecordPlace(move, succeeded);
                continue;
            }

            var next = Native.DeferWindowPos(deferred, move.Hwnd, Layer(move), move.X, move.Y, move.Width, move.Height, flags);
            if (next != 0)
            {
                deferred = next;
            }
            else
            {
                RecordPlace(move, succeeded: false);
            }
        }

        End(deferred);
    }

    /// <summary>Asks the executor about a direct place. A refused place is recorded on the broker.</summary>
    private static void RecordPlace(Move move, bool succeeded)
    {
        var error = succeeded ? 0 : Marshal.GetLastWin32Error();
        if (WindowExecutor.Place((long)move.Hwnd, move.X, move.Y, move.Width, move.Height, succeeded, error) is ElevatedAct act)
        {
            ElevatedBroker.Submit((nint)act.Id, act.X, act.Y, act.Width, act.Height);
        }
    }

    /// <summary>Asks the executor about a direct raise. A refused raise is recorded on the broker.</summary>
    private static void RecordRaise(long id, bool succeeded)
    {
        var error = succeeded ? 0 : Marshal.GetLastWin32Error();
        if (WindowExecutor.Raise(id, succeeded, error) is ElevatedAct act)
        {
            ElevatedBroker.Raise((nint)act.Id);
        }
    }

    private static uint Flags(Move move) => (move.Raise ? Native.SwpNoActivate : MoveFlags) | AppFullscreen(move.Hwnd);

    /// <summary>
    /// A tile without a caption is fullscreen in its own app, such as F11. An app can answer a move then with a rectangle of its own,
    /// so the move skips that question and the window goes where Winoma puts it (ADR 0076).
    /// </summary>
    private static uint AppFullscreen(nint hwnd) => InAppFullscreen(hwnd) ? Native.SwpNoSendChanging : 0;

    /// <summary>True when <paramref name="hwnd"/> has no caption: its app made it fullscreen, such as F11 or a video.</summary>
    /// <param name="hwnd">A window.</param>
    internal static bool InAppFullscreen(nint hwnd) =>
        ((long)Native.GetWindowLongPtr(hwnd, Native.GwlStyle) & Native.WsCaption) == 0;

    private static nint Layer(Move move) => move.Raise ? Native.HwndTopMost : 0;

    /// <summary>The Bar rectangles the gateway measured, one for each monitor, so a raised window stays above a Bar until the rectangles part.</summary>
    /// <param name="bars">Each Bar's outer rectangle. The array is not changed after it is handed over.</param>
    public void NoteBars(TileRect[] bars) => _bars = bars;

    /// <summary>
    /// A raised window is drawn above the bar. It stays there until it no longer crosses the bar,
    /// so the bar is uncovered by the window moving, rather than by popping back in front.
    /// A tile kept above the others (<see cref="Placement.Above"/>), such as a full-width window, is held in the same layer.
    /// Its rectangle is clear of the bar, so the bar stays up, and no tile that opens or takes the keyboard is drawn over it.
    /// </summary>
    private void Cover()
    {
        const uint hold = Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate;
        if (_untop.Count > 0)
        {
            var pending = _untop.ToArray();
            _untop.Clear();
            foreach (var id in pending)
            {
                Native.SetWindowPos((nint)id, Native.HwndNoTopMost, 0, 0, 0, 0, hold | Patience((nint)id));
            }
        }

        var placed = _latest;
        var fresh = !ReferenceEquals(placed, _covered);
        foreach (var placement in placed)
        {
            // A window its app keeps over the monitor is not kept above the others: the bar goes behind a window that fills the screen,
            // and a window that takes the keyboard from it must be seen.
            if (!(placement.Raised || placement.Above) || !Shown(placement) || LeftToItsApp((nint)placement.Id))
            {
                continue;
            }

            if (fresh || !_above.Contains(placement.Id))
            {
                Native.SetWindowPos((nint)placement.Id, Native.HwndTopMost, 0, 0, 0, 0, hold | Patience((nint)placement.Id));
            }

            _above.Add(placement.Id);
        }

        _covered = placed;

        foreach (var id in _above.ToArray())
        {
            if (!LeftToItsApp((nint)id) && (HeldAbove(id) || OverBar((nint)id)))
            {
                continue;
            }

            Native.SetWindowPos((nint)id, Native.HwndNoTopMost, 0, 0, 0, 0, hold | Patience((nint)id));
            _above.Remove(id);
        }
    }

    private bool OverBar(nint hwnd)
    {
        if (!Native.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        var window = new TileRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        foreach (var bar in _bars)
        {
            if (BarLayer.Crosses(window, bar))
            {
                return true;
            }
        }

        return false;
    }

    private bool HeldAbove(long id)
    {
        foreach (var placement in _latest)
        {
            if (placement.Id == id)
            {
                return placement.Raised || placement.Above;
            }
        }

        return false;
    }

    private static nint End(nint deferred)
    {
        if (deferred != 0)
        {
            Native.EndDeferWindowPos(deferred);
        }

        return 0;
    }

    /// <summary>
    /// Puts a minimized or maximized window back before it is measured.
    /// The caption buttons are not a tile change, and a maximized frame hides the caption.
    /// </summary>
    /// <returns>True when the window was minimized or maximized, so a border measured before this no longer holds.</returns>
    private bool ReleaseChrome(nint hwnd)
    {
        if (!Native.IsIconic(hwnd) && !Native.IsZoomed(hwnd))
        {
            return false;
        }

        Quiet(hwnd);
        Native.SendMessageTimeout(hwnd, Native.WmSysCommand, Native.ScRestore, 0, Native.SmtoAbortIfHung, 50, out _);
        return true;
    }

    /// <summary>Squares the corners and drops the frame border. A new window ignores the first call, so each window is sent twice.</summary>
    private void HideBorder(nint window)
    {
        var id = (long)window;
        _borderTries.TryGetValue(id, out var sent);
        if (sent >= 2)
        {
            return;
        }

        _borderTries[id] = sent + 1;
        var none = unchecked((int)0xFFFFFFFE);
        _ = Native.DwmSetWindowAttribute(window, Native.DwmwaBorderColor, ref none, sizeof(int));
        var square = 1;
        _ = Native.DwmSetWindowAttribute(window, Native.DwmwaWindowCornerPreference, ref square, sizeof(int));
    }

    /// <summary>Gives every window <see cref="HideBorder"/> changed the border and corners Windows draws by default.</summary>
    public void RestoreBorders()
    {
        lock (_gate)
        {
            foreach (var id in _borderTries.Keys)
            {
                var window = (nint)id;
                if (!Native.IsWindow(window))
                {
                    continue;
                }

                var standard = unchecked((int)0xFFFFFFFF);
                _ = Native.DwmSetWindowAttribute(window, Native.DwmwaBorderColor, ref standard, sizeof(int));
                var corners = 0;
                _ = Native.DwmSetWindowAttribute(window, Native.DwmwaWindowCornerPreference, ref corners, sizeof(int));
            }

            _borderTries.Clear();
        }
    }

    private readonly record struct Move(nint Hwnd, bool Show, int X, int Y, int Width, int Height, bool Raise = false);
}
