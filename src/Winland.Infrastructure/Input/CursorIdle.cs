namespace Winland.Infrastructure;

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}

public interface ICursorSurface
{
    void Warp(Point position);

    void Hide();

    void Show();
}

/// <summary>
/// Hides the cursor after it has been still, on a key press, and on touch until the mouse moves. Tests advance
/// <see cref="IClock"/>.
/// </summary>
public sealed class CursorIdle
{
    private readonly IClock _clock;
    private readonly Lock _gate = new();
    private ICursorSurface _cursor;
    private DateTimeOffset _lastMove;
    private TimeSpan _timeout;
    private bool _hideOnKey;
    private bool _touchHidden;
    private bool _idleHidden;
    private bool _keyHidden;
    private bool _hidden;

    public CursorIdle(ICursorSurface cursor, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(clock);
        _cursor = cursor;
        _clock = clock;
        _lastMove = clock.Now;
    }

    public void Use(ICursorSurface cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        lock (_gate)
            _cursor = cursor;
    }

    public void Configure(CursorBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);
        lock (_gate)
        {
            _hideOnKey = behavior.HideOnKeyPress;
            _timeout = behavior.InactiveTimeoutSeconds > 0
                ? TimeSpan.FromSeconds(behavior.InactiveTimeoutSeconds)
                : TimeSpan.Zero;
            if (!_hideOnKey)
                _keyHidden = false;
            if (_timeout == TimeSpan.Zero)
                _idleHidden = false;
            Publish();
        }
    }

    /// <summary>Moves the cursor. A hidden cursor stays hidden: only the mouse shows it again.</summary>
    public void Warp(Point position)
    {
        lock (_gate)
            _cursor.Warp(position);
    }

    public void Moved()
    {
        lock (_gate)
        {
            _lastMove = _clock.Now;
            _idleHidden = false;
            _keyHidden = false;
            _touchHidden = false;
            Publish();
        }
    }

    /// <summary>Touch pressed down: the cursor hides until the mouse moves.</summary>
    public void Touched()
    {
        lock (_gate)
        {
            _touchHidden = true;
            Publish();
        }
    }

    public void KeyPressed()
    {
        lock (_gate)
        {
            if (!_hideOnKey)
                return;

            _keyHidden = true;
            Publish();
        }
    }

    public void Tick()
    {
        lock (_gate)
        {
            if (_timeout <= TimeSpan.Zero)
                return;

            if (_clock.Now - _lastMove > _timeout)
                _idleHidden = true;
            Publish();
        }
    }

    public void Reveal()
    {
        lock (_gate)
        {
            _idleHidden = false;
            _keyHidden = false;
            _touchHidden = false;
            Publish();
        }
    }

    private void Publish()
    {
        var hide = _idleHidden || _keyHidden || _touchHidden;
        if (hide == _hidden)
            return;

        _hidden = hide;
        if (hide)
            _cursor.Hide();
        else
            _cursor.Show();
    }
}

public sealed class CursorIdleTimer : IDisposable
{
    private readonly CursorIdle _idle;
    private readonly Timer _timer;
    private int _disposed;

    public CursorIdleTimer(CursorIdle idle)
    {
        ArgumentNullException.ThrowIfNull(idle);
        _idle = idle;
        _timer = new Timer(OnTick, null, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
    }

    public void Dispose()
    {
        Volatile.Write(ref _disposed, 1);
        _timer.Dispose();
    }

    private void OnTick(object? state)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        _idle.Tick();
    }
}
