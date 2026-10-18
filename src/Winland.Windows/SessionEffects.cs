namespace Winland.Windows;

/// <summary>
/// Applies cursor, input, relayed key and idle requests from an outcome. Until <see cref="InstallSystem"/>
/// the cursor and the system input are stand-ins, no key is sent and no idle state is set, so tests
/// that apply an outcome do not change the machine.
/// </summary>
public static class SessionEffects
{
    private static readonly CursorIdle Idle = new(NullCursor.Instance, new SystemClock());
    private static readonly WarpBack Back = new();
    private static SystemInputSession? _session;
    private static KeyboardLayoutSwitcher? _layouts;
    private static CursorIdleTimer? _timer;
    private static IKeyRelaySender? _relay;
    private static IdleKeeper? _idle;
    private static int _installed;

    public static InputOriginals? Originals => _session?.Originals;

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
    }

    public static void Apply(Outcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Cursor is CursorBehavior behavior)
        {
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

    /// <summary>The mouse moves the cursor. Returns where it goes instead, with <c>cursor:warp_back_after_non_mouse_input</c>.</summary>
    public static Point? MouseMoved(Point from, Point to) => Back.Mouse(from, to);

    public static void Restore()
    {
        _session?.Restore();
        _layouts?.Restore();
        Magnifier.Stop();
        Idle.Reveal();
        _timer?.Dispose();
        _timer = null;
        _idle?.Dispose();
        _idle = null;
    }

    public static void RestoreSaved(InputOriginals saved) => _session?.RestoreSaved(saved);

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
