using System.Runtime.InteropServices;

using Winland.Core.Tiling;

namespace Winland.Platform.Win32;

/// <summary>A monitor rectangle in physical pixels.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct ScreenBounds(int X, int Y, int Width, int Height);

/// <summary>Tiles real top-level windows and strips the caption buttons.</summary>
public sealed class WindowGateway : IWindowGateway
{
    private int _barThickness = 48;
    private ScreenEdge _barEdge = ScreenEdge.Top;
    private nint _barWindow;

    // Every Bar on the glass and its outer rectangle, in the order the desktop names them. The rectangle array is replaced, never written,
    // so the work areas made from it are kept while no Bar moved, and the placer may read it from its own thread.
    private nint[] _barWindows = [];
    private TileRect[] _barRects = [];

    // The window each Bar went behind and was made topmost for, by Bar.
    private readonly Dictionary<nint, nint> _covers = [];
    private long[]? _free;
    private IReadOnlyList<long>? _freeFrom;
    private readonly AdmissionCache _admission = new(WindowCensus.Readings, static () => Environment.TickCount64);
    private readonly TaskbarGuard _taskbar = new();
    private readonly NotificationAnchor _notifications = new();
    private readonly CaptionGuard _captions = new();

    // Read and written only inside the mouse hook.
    private readonly WorkspaceWheel _wheel = new();
    private readonly FocusArbiter _focus = new();
    private readonly WindowPlacer _placer = new();
    private readonly WindowFade _fade = new();
    private FullscreenKey? _fullscreenKey;

    /// <inheritdoc />
    public TileRect FirstBarArea
    {
        get
        {
            var monitor = ScreenMetrics.MonitorRect(_barWindow);
            var screen = new TileRect(
                monitor.Left,
                monitor.Top,
                Math.Max(1, monitor.Right - monitor.Left),
                Math.Max(1, monitor.Bottom - monitor.Top));
            return BarFootprint.Of(screen, _barEdge, _barThickness).Open;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<TileRect> Monitors => ScreenMetrics.Areas(_barRects, _barEdge);

    /// <inheritdoc />
    public IReadOnlyList<TileRect> Screens => ScreenMetrics.Screens();

    /// <inheritdoc />
    public bool WindowFills(TileRect screen) => ScreenFill.Any(screen);

    /// <inheritdoc />
    public bool OnAnotherDesktop(long id) => WindowsDesktops.KeepsElsewhere((nint)id);

    /// <inheritdoc />
    public bool HiddenByCloak(long id) => HiddenLedger.Windows.Cloaked(id);

    /// <inheritdoc />
    public bool Shown(long id)
    {
        var hwnd = (nint)id;
        return hwnd != 0
            && Native.IsWindowVisible(hwnd)
            && !Native.IsIconic(hwnd)
            && (Native.DwmGetWindowAttribute(hwnd, Native.DwmwaCloaked, out var cloak, sizeof(int)) != 0 || cloak == 0);
    }

    /// <inheritdoc />
    public bool Alive(long id) => id != 0 && Native.IsWindow((nint)id);

    /// <summary>Reads <paramref name="window"/>'s outer rectangle.</summary>
    /// <param name="window">A window handle. Zero has no rectangle.</param>
    /// <param name="rect">The rectangle when this returns true.</param>
    /// <returns>False when the window is missing.</returns>
    private static bool TryReadRect(nint window, out TileRect rect)
    {
        if (window == 0 || !Native.GetWindowRect(window, out var native))
        {
            rect = default;
            return false;
        }

        rect = new TileRect(native.Left, native.Top, native.Right - native.Left, native.Bottom - native.Top);
        return true;
    }

    /// <summary>
    /// Insets each monitor by the Bar that lies on it, as thick as that Bar's window is on <paramref name="edge"/>, in physical pixels (ADR 0080).
    /// </summary>
    /// <param name="bars">Every Bar window, the first one first. A window that cannot be read reserves nothing.</param>
    /// <param name="edge">The screen edge every Bar occupies.</param>
    public void ReserveBars(IReadOnlyList<nint> bars, ScreenEdge edge)
    {
        ArgumentNullException.ThrowIfNull(bars);
        _barEdge = edge;
        if (Reserved(bars))
        {
            // The loop reserves on every turn, and on nearly every one no Bar has moved.
            _notifications.Follow(_barWindows);
            return;
        }

        var rects = new List<TileRect>(bars.Count);
        var windows = new List<nint>(bars.Count);
        foreach (var bar in bars)
        {
            if (TryReadRect(bar, out var rect))
            {
                windows.Add(bar);
                rects.Add(rect);
            }
        }

        _barWindow = bars.Count > 0 ? bars[0] : 0;
        if (windows.Count > 0 && windows[0] == _barWindow)
        {
            var along = edge is ScreenEdge.Left or ScreenEdge.Right ? rects[0].Width : rects[0].Height;
            _barThickness = Math.Max(1, along);
        }

        if (!windows.SequenceEqual(_barWindows))
        {
            _barWindows = [.. windows];
        }

        NoteBars(rects);
        _notifications.Follow(_barWindows);
    }

    /// <summary>True when <paramref name="bars"/> are the Bars already reserved, each where it was. Nothing is made to find out.</summary>
    private bool Reserved(IReadOnlyList<nint> bars)
    {
        if (bars.Count == 0 || bars.Count != _barWindows.Length || bars.Count != _barRects.Length || bars[0] != _barWindow)
        {
            return false;
        }

        for (var i = 0; i < bars.Count; i++)
        {
            if (bars[i] != _barWindows[i] || !TryReadRect(bars[i], out var rect) || rect != _barRects[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Keeps <paramref name="rects"/> as the Bars' rectangles. The array in use stays when none moved.</summary>
    private void NoteBars(List<TileRect> rects)
    {
        if (rects.SequenceEqual(_barRects))
        {
            return;
        }

        _barRects = [.. rects];
        _placer.NoteBars(_barRects);
    }

    /// <summary>Reads <paramref name="bar"/>'s rectangle again and keeps it, when it is a Bar that reserves a strip.</summary>
    private bool TryNoteBar(nint bar, out TileRect rect)
    {
        if (!TryReadRect(bar, out rect))
        {
            return false;
        }

        var at = Array.IndexOf(_barWindows, bar);
        if (at >= 0 && at < _barRects.Length && _barRects[at] != rect)
        {
            var rects = (TileRect[])_barRects.Clone();
            rects[at] = rect;
            _barRects = rects;
            _placer.NoteBars(rects);
        }

        return true;
    }

    /// <summary>Windows showed a banner or the notification center, so what it holds has probably changed. Raised on the desktop's thread.</summary>
    public event Action? NotificationShown
    {
        add => _notifications.Shown += value;
        remove => _notifications.Shown -= value;
    }

    /// <summary>Parks Windows' toast banners off screen while Winoma draws its own toasts, or lets them follow the bar again.</summary>
    /// <param name="conceal">True while Winoma draws its own toasts.</param>
    public void ConcealToasts(bool conceal) => _notifications.Conceal(conceal);

    /// <summary>True when a topmost window still crosses <paramref name="bar"/>.</summary>
    /// <param name="bar">The bar window.</param>
    public bool TopmostOverBar(nint bar)
    {
        return TryNoteBar(bar, out var rect) && ScreenFill.TopmostOverlaps(rect);
    }

    /// <summary>The window that fills <paramref name="screen"/>, or zero. One walk of the top-level windows.</summary>
    /// <param name="screen">A monitor rectangle.</param>
    /// <returns>The window, or zero.</returns>
    public nint Cover(TileRect screen) => ScreenFill.Find(screen);

    /// <summary>Puts <paramref name="bar"/> just behind <paramref name="cover"/>, the window <see cref="Cover"/> found on that Bar's monitor.</summary>
    /// <param name="bar">The bar window.</param>
    /// <param name="cover">The window that fills that Bar's monitor, or zero.</param>
    public void PlaceBarBehind(nint bar, nint cover)
    {
        TryNoteBar(bar, out _);
        const uint hold = Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate;
        if (_covers.TryGetValue(bar, out var held) && held != cover)
        {
            Native.SetWindowPos(held, Native.HwndNoTopMost, 0, 0, 0, 0, hold);
            _covers.Remove(bar);
        }

        if (cover == 0 || cover == bar)
        {
            return;
        }

        var extended = (long)Native.GetWindowLongPtr(cover, Native.GwlExStyle);
        if ((extended & Native.WsExTopmost) != 0 && Native.GetWindow(cover, Native.GwHwndNext) == bar)
        {
            return;
        }

        Native.SetWindowPos(cover, Native.HwndTopMost, 0, 0, 0, 0, hold);
        Native.SetWindowPos(bar, cover, 0, 0, 0, 0, hold);

        // A window that was topmost before keeps that when the cover ends. The flag is its own.
        if ((extended & Native.WsExTopmost) == 0)
        {
            _covers[bar] = cover;
        }
    }

    /// <summary>
    /// Takes back the topmost flag <see cref="PlaceBarBehind"/> gave the window that covered <paramref name="bar"/>.
    /// Left on, the window would stay above every dialog after it returned to its tile.
    /// </summary>
    /// <param name="bar">The bar window. One that went behind nothing is left alone.</param>
    public void ReleaseCover(nint bar)
    {
        if (_covers.Remove(bar, out var cover))
        {
            Native.SetWindowPos(cover, Native.HwndNoTopMost, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate);
        }
    }

    /// <summary>Takes back the topmost flag of every window a Bar went behind, as when the desktop stops.</summary>
    public void ReleaseCovers()
    {
        foreach (var bar in _covers.Keys.ToArray())
        {
            ReleaseCover(bar);
        }
    }

    /// <summary>
    /// Says how thick the first Bar is before any Bar is on its edge, and that no Bar reserves a strip: while the Bar is hidden,
    /// and before the first <see cref="ReserveBars"/>.
    /// </summary>
    /// <param name="thickness">Bar thickness in physical pixels. Zero for a hidden Bar.</param>
    /// <param name="edge">The screen edge the Bar occupies.</param>
    public void SetBar(int thickness, ScreenEdge edge)
    {
        _barThickness = Math.Max(0, thickness);
        _barEdge = edge;
        if (_barRects.Length > 0)
        {
            _barWindows = [];
            _barRects = [];
            _placer.NoteBars(_barRects);
        }
    }

    /// <summary>The pointer moved. Raised on the mouse hook thread for every move, while the taskbar is hidden; the handler must only signal and return.</summary>
    public event Action? PointerMoved
    {
        add => _captions.PointerMoved += value;
        remove => _captions.PointerMoved -= value;
    }

    /// <summary>The mouse or touchpad moved, pressed, or scrolled, with the event's tick time. Raised on the mouse hook thread; the handler must only signal and return.</summary>
    public event Action<uint>? PointerInput
    {
        add => _captions.PointerInput += value;
        remove => _captions.PointerInput -= value;
    }

    /// <summary>
    /// The left button went down, with the pointer, and the press goes on to the window under it. Raised on the mouse hook thread;
    /// the handler must only signal and return.
    /// </summary>
    public event Action<int, int>? PointerPressed
    {
        add => _captions.Pressed += value;
        remove => _captions.Pressed -= value;
    }

    /// <summary>
    /// What is drawn at a point the left button went down on, as <see cref="ShellController.Press"/> asks: the tile there when no other
    /// window covers it, and whether nothing but the desktop's own window is there.
    /// </summary>
    /// <param name="x">Horizontal position in physical pixels.</param>
    /// <param name="y">Vertical position in physical pixels.</param>
    /// <returns>No tile and not bare for any other window, one of Winoma's own included.</returns>
    public (long? Tile, bool Bare) PressedAt(int x, int y)
    {
        var hit = Native.WindowFromPoint(new Native.Point { X = x, Y = y });
        if (hit == 0)
        {
            return (null, true);
        }

        var root = Native.GetAncestor(hit, Native.GaRoot);
        if (root == 0)
        {
            root = hit;
        }

        if (_placer.TileAt(x, y) is long tile && tile == root)
        {
            return (tile, false);
        }

        return (null, root == Native.GetShellWindow() || DesktopClass(root));
    }

    /// <summary>True for the windows Explorer draws the wallpaper and the desktop icons in.</summary>
    private static bool DesktopClass(nint hwnd)
    {
        Span<char> buffer = stackalloc char[16];
        int length;
        unsafe
        {
            fixed (char* chars = buffer)
            {
                length = Native.GetClassName(hwnd, chars, buffer.Length);
            }
        }

        if (length <= 0)
        {
            return false;
        }

        ReadOnlySpan<char> name = buffer[..length];
        return name is "Progman" or "WorkerW";
    }

    /// <inheritdoc />
    public long Owner(long id) => id == 0 ? 0 : Native.GetWindow((nint)id, GwOwner);

    private const uint GwOwner = 4;

    /// <summary>
    /// Super and the wheel asked for the next or the previous Workspace (<see cref="WorkspaceWheel"/>).
    /// Raised on the mouse hook thread; the handler must only queue and return.
    /// </summary>
    public event Action<WorkspaceCycle>? WorkspaceWheeled;

    /// <summary>Asked inside the mouse hook for each move of the wheel. True when Super alone is held, so the move is Winoma's.</summary>
    private bool TurnWheel(int delta)
    {
        var other = KeyHeld(Native.VkShift) || KeyHeld(Native.VkControl) || KeyHeld(Native.VkMenu);
        var turn = _wheel.Turn(KeyboardHook.SuperIsHeld, other, delta, Environment.TickCount64);
        if (turn.Cycle is { } cycle)
        {
            WorkspaceWheeled?.Invoke(cycle);
        }

        return turn.Taken;
    }

    private static bool KeyHeld(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;

    /// <summary>A Super-drag the shell should apply to the tile tree.</summary>
    public event Action<TileDrag>? Dragged
    {
        add => _placer.Gesture += value;
        remove => _placer.Gesture -= value;
    }

    /// <summary>Starts the placer's drag poll. The keyboard hook calls this when Super goes down, and it returns at once.</summary>
    public void WakePlacer() => _placer.Wake();

    /// <summary>A child window of <paramref name="hwnd"/> moved. A tile's content is aligned with its client again. Runs on the window motion thread.</summary>
    /// <param name="hwnd">The top-level window.</param>
    /// <param name="child">The child window that moved.</param>
    public void ChildMoved(nint hwnd, nint child)
    {
        if (!ShellProperty.IsTagged(hwnd))
        {
            _placer.ChildMoved(hwnd, child);
        }
    }

    /// <summary>Puts a tile the app changed itself back on its tile at once. Runs on the window motion thread.</summary>
    /// <param name="hwnd">A top-level window that moved or resized.</param>
    public void HoldTile(nint hwnd)
    {
        if (ShellProperty.IsTagged(hwnd))
        {
            return;
        }

        NoteMoved(hwnd);
        _placer.HoldTile(hwnd);
        _fullscreenKey?.Moved(hwnd);
    }

    /// <summary>
    /// A plain F11 went down. True when Winoma takes it: a still picture of the monitor covers an app that leaves its own fullscreen on a tile,
    /// and Winoma sends the key itself (ADR 0076). An app that keeps its fullscreen over the monitor gets the key as it is.
    /// Runs inside the keyboard hook.
    /// </summary>
    public bool TakeFullscreenKey() => Volatile.Read(ref _fullscreenKey)?.Take() ?? false;

    /// <inheritdoc />
    public bool FullscreenTakesMonitor(long id) => id != 0 && WindowPlacer.TakesMonitor((nint)id);

    // Where each player floated before it took a tile. Read and written on the desktop's thread.
    private readonly Dictionary<nint, TileRect> _floatedAt = [];

    /// <inheritdoc />
    public void KeepAbove(long id, bool above)
    {
        var hwnd = (nint)id;
        if (hwnd == 0)
        {
            return;
        }

        var patience = Native.IsHungAppWindow(hwnd) ? Native.SwpAsyncWindowPos : 0;
        if (!above)
        {
            // Where it floats now is where it floats again.
            if (TryReadRect(hwnd, out var floating))
            {
                if (_floatedAt.Count > 16)
                {
                    foreach (var gone in _floatedAt.Keys.Where(static window => !Native.IsWindow(window)).ToArray())
                    {
                        _floatedAt.Remove(gone);
                    }
                }

                _floatedAt[hwnd] = floating;
            }

            Native.SetWindowPos(hwnd, Native.HwndNoTopMost, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | patience);
        }
        else
        {
            // No longer a tile, from this moment: the watcher that holds a tile to its tile would take its frame off again.
            _placer.Release(id);

            // Above the windows again, it is sized and moved by hand, so it gets back the frame Winoma took off it as a tile (ADR 0074).
            CaptionReturn.Give(hwnd);
            if (_floatedAt.Remove(hwnd, out var home))
            {
                Native.SetWindowPos(hwnd, Native.HwndTopMost, home.X, home.Y, home.Width, home.Height, Native.SwpNoActivate | patience);
            }
            else
            {
                Native.SetWindowPos(hwnd, Native.HwndTopMost, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | patience);
            }
        }

        // Whether a player is Picture-in-picture or a tile is read from this, and no event says it changed.
        _admission.Forget();
    }

    /// <inheritdoc />
    public bool AskFullscreen(long id)
    {
        var hwnd = (nint)id;

        // The key goes to whichever window has the keyboard. Sent while another one has it, F11 would act on that window.
        if (hwnd == 0 || Native.GetForegroundWindow() != hwnd)
        {
            return false;
        }

        // Without the picture the key is still sent. It can wait on the chord's modifiers, so not on this thread.
        if (Volatile.Read(ref _fullscreenKey)?.Ask(hwnd) != true)
        {
            ThreadPool.QueueUserWorkItem(static _ => FullscreenKey.PressKey());
        }

        return true;
    }

    /// <inheritdoc />
    public TileRect? Frame(long id) => WindowPlacer.Frame((nint)id);

    /// <inheritdoc />
    public TileRect? Unshown(long id)
    {
        var hwnd = (nint)id;
        if (hwnd == 0 || Native.IsWindowVisible(hwnd) || Native.GetWindow(hwnd, GwOwner) != 0 || !MayBecomeTile(hwnd))
        {
            return null;
        }

        return Native.GetWindowRect(hwnd, out var rect)
            ? new TileRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : null;
    }

    /// <inheritdoc />
    public void Seat(long id, TileRect rect, bool tile)
    {
        var hwnd = (nint)id;
        if (ShellProperty.IsTagged(hwnd))
        {
            return;
        }

        var outer = tile ? WindowPlacer.OuterUnshown(hwnd, rect) : rect;

        // The move waits on the window's own thread, which is busy starting its program. The desktop's thread does not wait with it.
        Task.Run(() => Native.SetWindowPos(hwnd, 0, outer.X, outer.Y, outer.Width, outer.Height, Native.SwpNoZOrder | Native.SwpNoActivate))
            .Forget("Tiling", "seat a new window");
    }

    /// <summary>
    /// True for a top-level window with a caption or a sizing border that is no tool window: one a program may show as its window.
    /// A program also makes windows it never shows, and those have neither. Runs on the thread that watches window events.
    /// </summary>
    /// <param name="hwnd">A window that was just made.</param>
    public static bool MayBecomeTile(nint hwnd)
    {
        if (hwnd == 0 || ShellProperty.IsTagged(hwnd) || Native.GetAncestor(hwnd, Native.GaRoot) != hwnd)
        {
            return false;
        }

        var style = (long)Native.GetWindowLongPtr(hwnd, Native.GwlStyle);
        return (style & (Native.WsCaption | Native.WsThickFrame)) != 0
            && WindowAdmission.MayAdmit((long)Native.GetWindowLongPtr(hwnd, Native.GwlExStyle));
    }

    /// <inheritdoc />
    public bool TilesHold() => _placer.Holds();

    /// <inheritdoc />
    public void NoteFloated(IReadOnlyCollection<long> ids) => _placer.NoteFloated(ids);

    /// <inheritdoc />
    public AdmittedWindows World()
    {
        var world = _admission.Read(WindowSignals.Generation);

        // A census that found the same free windows hands out the same list, so the copy made of it still stands.
        if (!ReferenceEquals(world.Free, _freeFrom))
        {
            _free = Copy(world.Free);
            _freeFrom = world.Free;
        }

        return world;
    }

    /// <inheritdoc />
    public IReadOnlyList<TrackedWindow> Snapshot() => _admission.Read(WindowSignals.Generation).Tiles;

    /// <inheritdoc />
    public IReadOnlyList<long> PictureInPicture() => _admission.Read(WindowSignals.Generation).PictureInPicture;

    /// <inheritdoc />
    public IReadOnlyList<TrackedWindow> Free()
    {
        var free = _admission.Read(WindowSignals.Generation).Free;
        var labeled = new TrackedWindow[free.Count];
        for (var i = 0; i < free.Count; i++)
        {
            var id = free[i];
            labeled[i] = new TrackedWindow(id, Caption(id));
        }

        return labeled;
    }

    /// <inheritdoc />
    public string Caption(long id)
    {
        if (id == 0)
        {
            return "";
        }

        Span<char> buffer = stackalloc char[512];
        int length;
        unsafe
        {
            fixed (char* chars = buffer)
            {
                length = Native.GetWindowText((nint)id, chars, buffer.Length);
            }
        }

        return length > 0 ? new string(buffer[..length]) : "";
    }

    /// <summary>
    /// A top-level window moved or resized. Runs on the thread that watches window motion.
    /// A window that changed its styles as it moved, such as an app going fullscreen itself, makes the shared census stale.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    public void NoteMoved(nint hwnd) =>
        _admission.NoteMoved(hwnd, Native.GetWindowLongPtr(hwnd, Native.GwlStyle), Native.GetWindowLongPtr(hwnd, Native.GwlExStyle));

    /// <summary>
    /// False when showing, hiding, or cloaking <paramref name="hwnd"/> changes neither the tiling nor the bar:
    /// a tool window or one that never takes the keyboard, such as a tooltip, a menu, or a popup, that the last census did not admit.
    /// Runs on the thread that watches window events.
    /// </summary>
    /// <param name="hwnd">A top-level window that still exists.</param>
    public bool MayCount(nint hwnd) =>
        WindowAdmission.MayAdmit((long)Native.GetWindowLongPtr(hwnd, Native.GwlExStyle)) || _admission.Admitted(hwnd);

    /// <summary>
    /// A program made <paramref name="hwnd"/> and has not shown it: it is kept clear, and fades in when it is shown (<see cref="WindowFade"/>).
    /// Runs on the thread that watches window events.
    /// </summary>
    /// <param name="hwnd">A window that may become a tile (<see cref="MayBecomeTile"/>).</param>
    public void FadeMade(nint hwnd) => _fade.Made(hwnd);

    /// <summary>A top-level window was shown or uncloaked. One that waited clear starts fading in. Runs on the thread that watches window events.</summary>
    /// <param name="hwnd">The window.</param>
    public void FadeShown(nint hwnd) => _fade.Shown(hwnd);

    /// <summary>Ends every fade and makes every window solid. A desktop that stops or is replaced calls this.</summary>
    public void EndFades() => _fade.Dispose();

    /// <summary>A window was destroyed. True when a census ever saw it, or when there is none to ask.</summary>
    /// <param name="hwnd">The window that is gone.</param>
    public bool Departed(nint hwnd)
    {
        // Windows hands the handle to a later window, which this desktop did not hide.
        HiddenLedger.Windows.Forget(hwnd);
        return _admission.Departed(hwnd);
    }

    /// <summary>A top-level window changed its title. Runs on the thread that watches window motion.</summary>
    /// <param name="hwnd">The window.</param>
    public void NoteRenamed(nint hwnd) => _admission.NoteRenamed(hwnd);

    /// <summary>Changes when a top-level window is shown, hidden, destroyed, cloaked, or uncloaked.</summary>
    public int WindowGeneration => WindowSignals.Generation;

    /// <inheritdoc />
    public void Apply(IReadOnlyList<Placement> placements) => _placer.Apply(placements, FreeToLift());

    private IReadOnlyList<long> FreeToLift() => _free ?? Copy(_admission.Read(WindowSignals.Generation).Free);

    private static long[] Copy(IReadOnlyList<long> ids)
    {
        var copy = new long[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            copy[i] = ids[i];
        }

        return copy;
    }

    /// <inheritdoc />
    public IReadOnlyList<long> HideAll(IReadOnlyList<long> ids)
    {
        List<long>? stuck = null;
        var hid = false;
        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];

            // Every arrange asks again for every scratchpad window. One that is already put away is left alone: a hide asks Explorer.
            // A handle that names no window has nothing to hide.
            if (id == 0 || HiddenLedger.Hides((nint)id) || !Native.IsWindow((nint)id) || ShellProperty.IsTagged((nint)id))
            {
                continue;
            }

            if (HiddenLedger.Windows.Hide(id) is null)
            {
                (stuck ??= []).Add(id);
            }
            else
            {
                hid = true;
            }
        }

        // Windows reports each cloak a moment later, on another thread. The shell reads the windows again right after a switch,
        // and that census must not be the one taken before it.
        if (hid)
        {
            WindowSignals.Bump();
        }

        return stuck is null ? [] : stuck;
    }

    /// <inheritdoc />
    public void ShowAll(IReadOnlyList<long> ids)
    {
        var shown = false;
        for (var i = 0; i < ids.Count; i++)
        {
            shown |= ShowOne(ids[i]);
        }

        if (shown)
        {
            WindowSignals.Bump();
        }
    }

    /// <inheritdoc />
    public void ShowAllAndWait(IReadOnlyList<long> ids)
    {
        var shown = false;
        for (var i = 0; i < ids.Count; i++)
        {
            shown |= ShowOne(ids[i]);
        }

        // One wait covers every window shown above: they reach the screen in the same frame,
        // and the desktop's thread stands still for one frame, not one per window.
        if (shown)
        {
            WindowSignals.Bump();
            _ = Native.DwmFlush();
        }
    }

    /// <summary>
    /// Shows one window Winoma hid, the way it was hidden: one on this desktop's ledger, or one under Winoma's kind of cloak that
    /// no ledger knows (<see cref="WindowHide.Cloaked"/>). The caller waits for the frame.
    /// </summary>
    /// <returns>True when the window was asked to show. False when Winoma is not hiding it, so no frame is owed.</returns>
    private static bool ShowOne(long id)
    {
        // Asked for every scratchpad window on every arrange while the scratchpad is shown. A window that is not hidden costs one lookup
        // and one read of its cloak.
        if (id == 0 || (HiddenLedger.Windows.Way(id) is null && !HiddenLedger.Windows.Cloaked(id)))
        {
            return false;
        }

        // A window Explorer did not uncloak stays on the ledger, so the next arrange asks again.
        HiddenLedger.Windows.Show(id);
        return true;
    }

    /// <inheritdoc />
    public long? ForegroundLeft() => ForegroundTrail.Left(Native.GetForegroundWindow());

    /// <inheritdoc />
    public void RecordHidden(IReadOnlyCollection<long> ids) => HiddenStore.Write(ids);

    /// <inheritdoc />
    public IReadOnlyList<long> LeftHidden() => HiddenStore.TakeLeft();

    /// <inheritdoc />
    public long? Foreground()
    {
        var hwnd = Native.GetForegroundWindow();
        return hwnd == 0 ? null : hwnd;
    }

    /// <inheritdoc />
    public void PlaceNow(IReadOnlyList<Placement> placements) => _placer.PlaceNow(placements, FreeToLift());

    /// <inheritdoc />
    public IReadOnlyList<WindowMinimum> Minimums(IReadOnlyCollection<long> ids) => _placer.Minimums(ids);

    /// <inheritdoc />
    public IReadOnlyList<WindowMinimum> Overflows(IReadOnlyList<Placement> placements) =>
        _placer.Overflows(placements);

    /// <inheritdoc />
    public void Close(long id)
    {
        var hwnd = (nint)id;
        if (WindowExecutor.Close(id, hwnd != 0 && IsElevated(hwnd)) is ElevatedAct act)
        {
            ElevatedBroker.Close((nint)act.Id);
            return;
        }

        // It fades out first, and is asked to close when it is clear. With animation effects off it is asked at once.
        if (!_fade.Close(hwnd, () => Native.PostMessage(hwnd, Native.WmClose, 0, 0)))
        {
            Native.PostMessage(hwnd, Native.WmClose, 0, 0);
        }
    }

    /// <inheritdoc />
    public void Focus(long id)
    {
        var hwnd = (nint)id;
        ShowHidden(id);
        Native.ShowWindow(hwnd, Native.SwRestore);
        // The pointer stays where the person left it. A keyboard move never shows it.
        Apply(hwnd, FocusKind.Workspace, KeyboardHook.SuperIsHeld);
    }

    /// <summary>
    /// Shows a window Winoma hid before it takes the keyboard, on this desktop's ledger or not. A cloaked window keeps the keyboard it is given,
    /// and a Group member is given it a moment before the placement that shows it.
    /// </summary>
    private static void ShowHidden(long id) => HiddenLedger.Windows.Show(id);

    /// <inheritdoc />
    public void ResetFocusRefusal() => _focus.Reset();

    /// <summary>
    /// Asks Windows to give one of Winoma's own windows the keyboard, as a Workspace focus does (ADR 0020), and keeps no record of a refusal.
    /// <see cref="Activate"/> backs off for a second after Windows says no, so a window that asks again every few moments asks through this.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <returns>True when the window has the keyboard.</returns>
    public bool Seek(long id) => Perform((nint)id, FocusPlan.For(FocusKind.Workspace, KeyboardHook.SuperIsHeld));

    /// <inheritdoc />
    public bool Activate(long id)
    {
        var hwnd = (nint)id;
        if (hwnd == 0)
        {
            return false;
        }

        ShowHidden(id);
        if (Native.IsIconic(hwnd))
        {
            Native.ShowWindow(hwnd, Native.SwRestore);
        }

        var foreground = (long)Native.GetForegroundWindow();
        var now = Environment.TickCount64;
        var verdict = _focus.Decide(new FocusFacts(
            id,
            foreground,
            IsElevated(hwnd),
            ElevatedBroker.Running,
            KeyboardHook.SuperIsHeld,
            now));
        switch (verdict)
        {
            case FocusVerdict.AlreadyInFront:
                return true;
            case FocusVerdict.BackOff:
                return false;
            case FocusVerdict.HoldHandoff:
                return true;
            case FocusVerdict.HandOff:
                ElevatedBroker.Focus(hwnd);
                return true;
            case FocusVerdict.Attempt attempt:
                if (attempt.Kind == FocusKind.Helper)
                {
                    ElevatedBroker.Ensure();
                }

                if (Apply(hwnd, attempt.Kind, attempt.SuperHeld))
                {
                    _focus.Succeeded(id);
                    return true;
                }

                _focus.Failed(id, foreground, now);
                return false;
            default:
                return false;
        }
    }

    /// <summary>True when the window's process is elevated. A normal process cannot post a close to it.</summary>
    internal static bool IsElevated(nint hwnd)
    {
        _ = Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return false;
        }

        var process = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, pid);
        if (process == 0)
        {
            return Marshal.GetLastWin32Error() == Native.ErrorAccessDenied;
        }

        try
        {
            if (!Native.OpenProcessToken(process, Native.TokenQuery, out var token))
            {
                return Marshal.GetLastWin32Error() == Native.ErrorAccessDenied;
            }

            try
            {
                if (!Native.GetTokenInformation(token, Native.TokenElevation, out var elevation, sizeof(int), out _))
                {
                    return false;
                }

                return elevation != 0;
            }
            finally
            {
                Native.CloseHandle(token);
            }
        }
        finally
        {
            Native.CloseHandle(process);
        }
    }

    /// <inheritdoc />
    public bool ForegroundCloaked() => Native.ShellCloaked(Native.GetForegroundWindow());

    /// <inheritdoc />
    public void ReleaseFocus()
    {
        var current = Native.GetForegroundWindow();
        var hidden = current != 0 && !Native.IsWindowVisible(current);
        if (!hidden && !Native.ShellCloaked(current))
        {
            return;
        }

        FocusShell(current);
    }

    /// <inheritdoc />
    public void ReleaseFocus(long leaving)
    {
        var current = Native.GetForegroundWindow();
        if (current != 0 && current == (nint)leaving)
        {
            FocusShell(current);
        }
    }

    /// <summary>Gives the keyboard to the desktop's own window, which draws nothing a key changes.</summary>
    private static void FocusShell(nint current)
    {
        var shell = Native.FindWindow("Progman", null);
        if (shell == 0)
        {
            shell = Native.GetShellWindow();
        }

        if (shell != 0 && shell != current)
        {
            var superHeld = KeyboardHook.SuperIsHeld;
            Apply(shell, FocusKind.Workspace, superHeld);
        }
    }

    /// <inheritdoc />
    public long? WindowUnderPointer()
    {
        if (!Native.GetCursorPos(out var point))
        {
            return null;
        }

        return _placer.TileAt(point.X, point.Y);
    }

    /// <summary>The top-level window drawn at the pointer, above any tile there. Null when there is none.</summary>
    public long? DrawnAtPointer()
    {
        if (!Native.GetCursorPos(out var point))
        {
            return null;
        }

        var hit = Native.WindowFromPoint(point);
        if (hit == 0)
        {
            return null;
        }

        var root = Native.GetAncestor(hit, Native.GaRoot);
        return root == 0 ? hit : root;
    }

    /// <summary>Focuses a window from the elevated helper. The helper plan never joins thread input.</summary>
    internal static void FocusFromHelper(nint hwnd) => Apply(hwnd, FocusKind.Helper, superHeld: false);

    /// <summary>
    /// Runs a focus plan.
    /// A later attempt runs only after the previous one left another window in front, and the foreground lock is cleared before that later attempt.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <param name="attempts">The plan from <see cref="FocusPlan"/>.</param>
    /// <returns>True when <paramref name="hwnd"/> is in front.</returns>
    internal static bool Perform(nint hwnd, IReadOnlyList<FocusAttempt> attempts)
    {
        if (hwnd == 0 || Native.GetForegroundWindow() == hwnd)
        {
            return hwnd != 0;
        }

        for (var i = 0; i < attempts.Count; i++)
        {
            if (i == 0)
            {
                MoveForeground(hwnd, attempts[i].Join);
            }
            else
            {
                Repeat(hwnd, attempts[i].Join);
            }

            if (Native.GetForegroundWindow() == hwnd)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Apply(nint hwnd, FocusKind kind, bool superHeld)
    {
        if (Perform(hwnd, FocusPlan.For(kind, superHeld)))
        {
            return true;
        }

        if (hwnd != 0 && Native.GetForegroundWindow() != hwnd)
        {
            Log.Warning("Workspace", "could not focus window " + hwnd);
        }

        return false;
    }

    /// <summary>
    /// Repeats one attempt after clearing the foreground lock.
    /// Windows refuses <c>SetForegroundWindow</c> from a program the person is not typing into until that lock is cleared,
    /// and a Workspace switch asks while the window it leaves still has the keyboard.
    /// </summary>
    private static void Repeat(nint hwnd, bool join)
    {
        var previous = 0;
        var cleared = Native.SystemParametersInfo(Native.SpiGetForegroundLockTimeout, 0, ref previous, 0)
            && Native.SystemParametersInfoFlag(Native.SpiSetForegroundLockTimeout, 0, 0, 0);
        try
        {
            MoveForeground(hwnd, join);
        }
        finally
        {
            if (cleared)
            {
                Native.SystemParametersInfoFlag(Native.SpiSetForegroundLockTimeout, 0, previous, 0);
            }
        }
    }

    private static void MoveForeground(nint hwnd, bool join)
    {
        if (!join)
        {
            Front(hwnd, joined: false);
            return;
        }

        Steal(hwnd);
    }

    private static void Steal(nint hwnd)
    {
        var current = Native.GetForegroundWindow();
        var thread = Native.GetCurrentThreadId();
        var from = current == 0 ? 0u : Native.GetWindowThreadProcessId(current, out _);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var pumping = current != 0
            && Native.SendMessageTimeout(current, Native.WmNull, 0, 0, Native.SmtoAbortIfHung, 50, out _) != 0;
        started = Waited("the probe of window " + current, hwnd, started);
        if (!pumping)
        {
            Front(hwnd, joined: false);
            return;
        }

        var attachFrom = from != 0 && from != thread && Native.AttachThreadInput(from, thread, true);
        Waited("joining window " + current, hwnd, started);
        try
        {
            Front(hwnd, attachFrom);
        }
        finally
        {
            if (attachFrom)
            {
                started = System.Diagnostics.Stopwatch.GetTimestamp();
                Native.AttachThreadInput(from, thread, false);
                Waited("leaving window " + current, hwnd, started);
            }
        }
    }

    private static void Front(nint hwnd, bool joined)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        Native.BringWindowToTop(hwnd);
        started = Waited(joined ? "BringWindowToTop, joined" : "BringWindowToTop", hwnd, started);
        Native.SetForegroundWindow(hwnd);
        Waited(joined ? "SetForegroundWindow, joined" : "SetForegroundWindow", hwnd, started);
    }

    /// <summary>
    /// Logs one call of a focus that kept this thread waiting <see cref="SlowFocusMs"/> or longer. The desktop does nothing else in that time.
    /// </summary>
    /// <returns>The time now, for the next call.</returns>
    private static long Waited(string call, nint hwnd, long started)
    {
        var elapsed = (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (elapsed >= SlowFocusMs)
        {
            Log.Warning("Workspace", "focusing window " + hwnd + " waited " + elapsed + " ms on " + call);
        }

        return System.Diagnostics.Stopwatch.GetTimestamp();
    }

    private const long SlowFocusMs = 50;

    /// <inheritdoc />
    public bool ApplyCaption(long id, TileRect screen)
    {
        var hwnd = (nint)id;
        var style = (long)Native.GetWindowLongPtr(hwnd, Native.GwlStyle);
        var next = CaptionStyle.WithoutButtons(style, (style & Native.WsPopup) != 0);

        // The style is one read and is already stripped on every arrange but a window's first. Whether the window fills
        // the monitor is several more, so it is asked only of a window that would change.
        if (next == style || ScreenFill.This(hwnd, screen))
        {
            return false;
        }

        // The style change waits on the window's own thread. One that has stopped answering keeps its buttons, and the next arrange asks again.
        if (Native.IsHungAppWindow(hwnd))
        {
            return false;
        }

        CaptionReturn.Note(hwnd, style, next);
        Native.SetWindowLongPtr(hwnd, Native.GwlStyle, (nint)next);
        _admission.Forget();
        Native.SetWindowPos(hwnd, 0, 0, 0, 0, 0, Native.SwpNoZOrder | Native.SwpNoActivate | Native.SwpFrameChanged | 0x0001 | 0x0002);
        // The client changed inside the same rect. The content child may still have the old size.
        WindowPlacer.Relayout(hwnd);
        _placer.ForgetAlignment(id);
        return true;
    }

    /// <inheritdoc />
    public void MarkWorkspace(long id, int mark) => WorkspaceProperty.Write((nint)id, mark);

    /// <inheritdoc />
    public void MarkTiling(long id, int mark) => TilingProperty.Write((nint)id, mark);

    /// <inheritdoc />
    public IReadOnlyDictionary<long, int> TilingMarks() => TilingProperty.Read();

    /// <inheritdoc />
    public IReadOnlyDictionary<long, int> WorkspaceMarks() => WorkspaceProperty.Read();

    /// <summary>Shows the Windows taskbar from a process that has no gateway, such as the desktop watcher after the desktop is gone.</summary>
    public static void ShowTaskbarWithoutDesktop() => TaskbarGuard.ShowAll();

    /// <summary>
    /// Gives windows their caption buttons and resize frames back from a process that has no gateway. The bits are read off
    /// the windows, where the desktop that took them wrote them (ADR 0074).
    /// </summary>
    public static void RestoreCaptionsWithoutDesktop() => CaptionReturn.Restore();

    /// <summary>
    /// Installs the shell's window chrome. Hides the Windows taskbar when <paramref name="taskbar"/> is true,
    /// and shows it when <paramref name="taskbar"/> is false. Captions, notifications, and the fullscreen key are installed either way.
    /// </summary>
    /// <param name="taskbar">True to hide the Windows taskbar and keep it hidden.</param>
    public void HideTaskbar(bool taskbar = true)
    {
        if (taskbar)
        {
            _taskbar.Suppress();
        }
        else
        {
            _taskbar.Restore();
        }

        _notifications.Start();
        _captions.CarryPressed = _placer.ClaimCarry;
        _captions.CarryReleased = _placer.ReleaseCarry;
        _captions.WheelTurned = TurnWheel;
        _captions.Install();
        Volatile.Write(ref _fullscreenKey, _fullscreenKey ?? new FullscreenKey(_placer));
    }

    /// <summary>Gives the tiled windows their caption buttons, resize frames, borders, and corners back. A desktop that stops calls this.</summary>
    public void RestoreCaptions()
    {
        CaptionReturn.Restore();
        _placer.RestoreBorders();
    }

    /// <summary>Shows the Windows taskbar again.</summary>
    public void ShowTaskbar()
    {
        Interlocked.Exchange(ref _fullscreenKey, null)?.Dispose();
        ReleaseCovers();
        _captions.Stop();
        _notifications.Stop();
        _taskbar.Restore();
    }
}
