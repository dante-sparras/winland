namespace Winland.Windows;

/// <summary>
/// Applies cursor, input, relayed key, idle and display mirror requests from an outcome. Until <see cref="InstallSystem"/>
/// the cursor and the system input are stand-ins, no key is sent, no idle state is set and no display is mirrored, so
/// tests that apply an outcome do not change the machine.
/// </summary>
public static class SessionEffects
{
    private static readonly CursorIdle Idle = new(NullCursor.Instance, new SystemClock());
    private static readonly WarpBack Back = new();

    // cursor:hotspot_padding and the monitors it keeps the cursor inside, in physical pixels. The mouse hook reads both.
    private static volatile int _padding;
    private static volatile Rectangle[] _monitors = [];
    private static SystemInputSession? _session;
    private static KeyboardLayoutSwitcher? _layouts;
    private static CursorIdleTimer? _timer;
    private static IKeyRelaySender? _relay;
    private static IdleKeeper? _idle;
    private static DisplayMirrorSession? _mirrors;
    private static int _installed;

    public static InputOriginals? Originals => _session?.Originals;

    /// <summary>The displays Winland made mirror another, to put back.</summary>
    public static IReadOnlyList<DisplayMirrorOriginal> MirrorOriginals => _mirrors?.Originals ?? [];

    /// <summary>The first layout <c>kb_layout</c> names, once loaded, or 0.</summary>
    public static nint FirstLayout => _layouts?.First ?? 0;

    public static void InstallSystem()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        Idle.Use(new Win32Cursor());
        _session = new SystemInputSession(new Win32SystemInput());
        _layouts = new KeyboardLayoutSwitcher(new Win32KeyboardLayouts());
        _timer = new CursorIdleTimer(Idle);
        _relay = new Win32KeyRelaySender();
        _idle = new IdleKeeper();
        _mirrors = new DisplayMirrorSession(new Win32DisplayTopology());
    }

    public static void Apply(Outcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var monitors = new List<Rectangle>(outcome.State.Monitors.Count);
        var scale = 1.0;
        foreach (var monitor in outcome.State.Monitors)
        {
            if (!monitor.Enabled || monitor.Width <= 0 || monitor.Height <= 0)
                continue;

            monitors.Add(new Rectangle(monitor.X, monitor.Y, monitor.Width, monitor.Height));
            scale = Math.Max(scale, monitor.Scale);
        }

        if (monitors.Count > 0)
            _monitors = [.. monitors];

        CursorShape.Show(outcome.ResizeCursor);

        if (outcome.Cursor is CursorBehavior behavior)
        {
            _padding = (int)Math.Round(behavior.HotspotPadding * scale);
            Idle.Configure(behavior);
            Back.Configure(behavior.WarpBackAfterNonMouseInput);
            if (Volatile.Read(ref _installed) != 0)
                Magnifier.Configure(behavior.Zoom);
        }

        if (outcome.Warp is WarpTo warp)
        {
            Idle.Warp(warp.Position);
            Back.NonMouse();
            Magnifier.Moved(warp.Position);
        }

        _layouts?.Apply(outcome.KeyboardLayout);
        if (outcome.InputSettings is InputSettingsRequest settings)
            _session?.Apply(settings);
        else
            _session?.Revert();

        Relay(outcome);
        _idle?.Set(IdleInhibit.Wanted(outcome.State));
        if (outcome.DisplayMirrors is { } mirrors)
            _mirrors?.Apply(mirrors);
    }

    /// <summary>
    /// Carries out only the outcome's one-off requests: its cursor warp and its relayed keys. An older outcome
    /// applied after a newer one keeps these and nothing else here; its warp is gone when a newer one warped.
    /// </summary>
    public static void ApplyOneOff(Outcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Warp is WarpTo warp)
        {
            Idle.Warp(warp.Position);
            Back.NonMouse();
            Magnifier.Moved(warp.Position);
        }

        Relay(outcome);
    }

    private static void Relay(Outcome outcome)
    {
        if (_relay is not IKeyRelaySender relay)
            return;

        foreach (var key in outcome.Relays)
            relay.Send(key);
    }

    public static void NoteMove(Point position, PointerSource source = PointerSource.Mouse)
    {
        if (source is PointerSource.Touch or PointerSource.Pen)
            Idle.Touched(pen: source == PointerSource.Pen);
        else
            Idle.Moved();

        Magnifier.Moved(position);
    }

    /// <summary>A touch or a pen pressed down.</summary>
    public static void NoteTouch(bool pen) => Idle.Touched(pen);

    public static void NoteKey() => Idle.KeyPressed();

    /// <summary>Something other than the mouse moved the cursor: touch, a pen, or a move another program injected.</summary>
    public static void NoteNonMouse() => Back.NonMouse();

    /// <summary>Where the cursor goes instead of <paramref name="point"/>, to keep <c>cursor:hotspot_padding</c> from the monitors' edges.</summary>
    public static Point Padded(Point point) => _padding == 0 ? point : HotspotPadding.ClosestValid(point, _monitors, _padding);

    /// <summary>The mouse moves the cursor. Returns where it goes instead, with <c>cursor:warp_back_after_non_mouse_input</c>.</summary>
    public static Point? MouseMoved(Point from, Point to) => Back.Mouse(from, to);

    /// <summary>Loads the user's own cursors again, after a run that may have left a drag's pointer shape behind.</summary>
    public static void RestoreCursors() => CursorShape.Restore();

    public static void Restore()
    {
        _session?.Restore();
        _mirrors?.Restore();
        _layouts?.Restore();
        Magnifier.Stop();
        CursorShape.Reset();
        Idle.Reveal();
        _timer?.Dispose();
        _timer = null;
        _idle?.Dispose();
        _idle = null;
    }

    public static void RestoreSaved(InputOriginals saved) => _session?.RestoreSaved(saved);

    /// <summary>Puts back the mirrors a run that did not stop cleanly left.</summary>
    public static void RestoreSaved(IReadOnlyList<DisplayMirrorOriginal> mirrors) => _mirrors?.RestoreSaved(mirrors);

    private sealed class NullCursor : ICursorSurface
    {
        public static readonly NullCursor Instance = new();

        public void Warp(Point position)
        {
        }

        public void Hide()
        {
        }

        public void Show()
        {
        }
    }
}
