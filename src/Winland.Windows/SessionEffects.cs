namespace Winland.Windows;

/// <summary>
/// Applies cursor and input requests from an outcome. Until <see cref="InstallSystem"/> the cursor
/// and the system input are stand-ins, so tests that apply an outcome do not change the machine.
/// </summary>
public static class SessionEffects
{
    private static readonly CursorIdle Idle = new(NullCursor.Instance, new SystemClock());
    private static SystemInputSession? _session;
    private static CursorIdleTimer? _timer;
    private static int _installed;

    public static InputOriginals? Originals => _session?.Originals;

    public static void InstallSystem()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        Idle.Use(new Win32Cursor());
        _session = new SystemInputSession(new Win32SystemInput());
        _timer = new CursorIdleTimer(Idle);
    }

    public static void Apply(Outcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Cursor is CursorBehavior behavior)
            Idle.Configure(behavior);

        if (outcome.Warp is WarpTo warp)
            Idle.Warp(warp.Position);

        if (outcome.InputSettings is InputSettingsRequest settings)
            _session?.Apply(settings);
        else
            _session?.Revert();
    }

    public static void NoteMove() => Idle.Moved();

    public static void NoteKey() => Idle.KeyPressed();

    public static void Restore()
    {
        _session?.Restore();
        Idle.Reveal();
        _timer?.Dispose();
        _timer = null;
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
