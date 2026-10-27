using System.Collections.Concurrent;
using System.Diagnostics;

namespace Winland.Infrastructure;

/// <summary>The window calls a <see cref="MotionPlayer"/> makes. Tests pass a recording stand-in.</summary>
internal interface IMotionTarget
{
    /// <summary>Whether the window has stopped answering, so an in-between frame would block.</summary>
    bool Hung(WindowId window);

    /// <summary>
    /// An in-between frame of a window that rests on <paramref name="content"/>. It is fast and does not correct the frame.
    /// </summary>
    void Move(WindowId window, Rectangle bounds, Rectangle content);

    /// <summary>Undoes whatever the in-between frames did beyond moving the window, for a motion that stops early.</summary>
    void Release(WindowId window);

    /// <summary>The Layout rectangle, applied the way every other placement is.</summary>
    void Place(WindowId window, Rectangle bounds);

    /// <summary>The Layout rectangle of a window that has stopped answering, sent without waiting for it.</summary>
    void PlaceAsync(WindowId window, Rectangle bounds);

    void Alpha(WindowId window, byte alpha);

    void Border(WindowId window, BorderColor color);

    void Hide(WindowId window);

    void Close(WindowId window);

    /// <summary>Whether the window is still open and shown.</summary>
    bool Shown(WindowId window);

    /// <summary>Waits for the next composed frame.</summary>
    void Pace();
}

/// <summary>
/// Steps the core's motions on a thread of its own, so the message loop never waits for a frame. Every
/// call that changes a window holds that window's lock, and a frame is dropped when the window was
/// changed after the frame was computed. That keeps the Layout rectangle the last thing a window is given.
/// </summary>
internal sealed class MotionPlayer : IDisposable
{
    internal static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(1.5);

    private readonly IMotionTarget _target;
    private readonly Func<double> _clock;
    private readonly Animator _animator = new();
    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<WindowId, Lock> _windows = new();
    private readonly Dictionary<WindowId, long> _stamps = [];
    private readonly Dictionary<WindowId, Closing> _closing = [];
    private readonly Dictionary<WindowId, double> _graces = [];
    private readonly AutoResetEvent _wake = new(false);
    private readonly bool _threaded;
    private Thread? _thread;
    private volatile bool _stopped;
    private int _epoch;

    internal MotionPlayer(IMotionTarget target, Func<double>? clock = null, bool threaded = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        _clock = clock ?? (() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
        _threaded = threaded;
    }

    /// <summary>The lock to hold while changing <paramref name="window"/> outside the player.</summary>
    internal Lock For(WindowId window) => _windows.GetOrAdd(window, _ => new Lock());

    internal bool Running(WindowId window)
    {
        lock (_gate)
            return _animator.Running(window) || _closing.ContainsKey(window);
    }

    /// <summary>
    /// Starts a motion and shows its first frame. The caller holds <see cref="For"/>. Returns false while the
    /// player lands its motions, and the caller then applies the outcome without one.
    /// </summary>
    internal bool Begin(Motion motion)
    {
        ArgumentNullException.ThrowIfNull(motion);
        if (_stopped)
            return false;

        Frame? first;
        lock (_gate)
        {
            var now = _clock();
            Touch(motion.Window);
            _closing.Remove(motion.Window);
            if (motion.End == MotionEnd.Close && motion.CloseGraceMilliseconds is int grace)
                _graces[motion.Window] = grace;
            else
                _graces.Remove(motion.Window);
            _animator.Start(motion, now);
            first = _animator.Peek(motion.Window, now);
        }

        // A motion that no longer moves the window, such as a fade that replaced a move to another rectangle, leaves
        // the window where the caller puts it, so whatever the moving frames did to it is undone now.
        if (first?.Bounds is null)
            _target.Release(motion.Window);

        if (first is not null)
            Show(first);

        Wake();
        return true;
    }

    /// <summary>
    /// Asks a window to close without a motion. It goes transparent at once, as a closing motion leaves it, so nothing
    /// it draws while it closes shows, and it is put back on <paramref name="rest"/> at <paramref name="alpha"/> if it
    /// stays open. The caller holds <see cref="For"/>.
    /// </summary>
    internal void Close(WindowId window, Rectangle rest, byte alpha)
    {
        if (_stopped)
        {
            _target.Close(window);
            return;
        }

        lock (_gate)
        {
            Touch(window);
            _animator.Forget(window);
            _graces.Remove(window);
            _closing[window] = new Closing(_clock() + CloseGrace.TotalMilliseconds, rest, alpha, Stamp(window));
        }

        _target.Alpha(window, 0);
        _target.Close(window);
        Wake();
    }

    /// <summary>
    /// Takes a placement that came without a motion. Returns true when a running motion keeps the window.
    /// Otherwise the caller places it while it still holds <see cref="For"/>.
    /// </summary>
    internal bool Settle(WindowId window, Rectangle bounds)
    {
        lock (_gate)
        {
            if (_animator.Settle(window, bounds))
                return true;

            Touch(window);
        }

        _target.Release(window);
        return false;
    }

    internal PaintHold Paint(WindowId window, byte alpha, BorderColor border)
    {
        // A window asked to close stays transparent while it goes.
        lock (_gate)
        {
            var hold = _animator.Paint(window, alpha, border, _clock());
            return _closing.ContainsKey(window) ? hold with { Alpha = true } : hold;
        }
    }

    /// <summary>Stops any motion on the window. The caller holds <see cref="For"/> and puts the window in place.</summary>
    internal void Halt(WindowId window)
    {
        lock (_gate)
        {
            Touch(window);
            _animator.Forget(window);
            _closing.Remove(window);
        }

        _target.Release(window);
    }

    /// <summary>
    /// Forgets a window that is no longer a Client. It waits out a frame already being applied to the
    /// window, so whatever the caller does to the window next comes after that frame.
    /// </summary>
    internal void Drop(WindowId window)
    {
        // The window's lock stays, so a caller that still holds it keeps the same one.
        lock (For(window))
            Halt(window);
    }

    /// <summary>Applies one frame of every motion. Returns whether any motion is still running.</summary>
    internal bool Tick()
    {
        List<(Frame Frame, long Stamp)> frames;
        List<(WindowId Window, Closing Closing)> due = [];
        lock (_gate)
        {
            var now = _clock();
            var stepped = _animator.Step(now);
            frames = new List<(Frame, long)>(stepped.Count);
            foreach (var frame in stepped)
                frames.Add((frame, Stamp(frame.Window)));

            foreach (var (window, closing) in _closing)
            {
                if (closing.Due <= now)
                    due.Add((window, closing));
            }

            foreach (var (window, _) in due)
                _closing.Remove(window);
        }

        // The frames in between go first, so a window coming to rest, which waits for its program, does not hold up
        // the others' frames for this vblank.
        foreach (var (frame, stamp) in frames.OrderBy(pair => pair.Item1.Done))
        {
            // A window that another thread is changing, as a new motion starts, skips this frame so it does not hold up
            // the others. Its last frame waits, so it always comes to rest.
            var window = For(frame.Window);
            if (frame.Done)
                window.Enter();
            else if (!window.TryEnter())
                continue;

            try
            {
                lock (_gate)
                {
                    if (Stamp(frame.Window) != stamp)
                        continue;
                }

                if (frame.Done)
                    Rest(frame);
                else if (!_target.Hung(frame.Window))
                    Show(frame);
            }
            finally
            {
                window.Exit();
            }
        }

        foreach (var (window, closing) in due)
        {
            lock (For(window))
            {
                lock (_gate)
                {
                    if (Stamp(window) != closing.Stamp)
                        continue;
                }

                // The window refused to close, so it goes back where its Layout put it.
                if (_target.Shown(window))
                {
                    Place(window, closing.Rest);
                    _target.Alpha(window, closing.Alpha);
                }
            }
        }

        lock (_gate)
            return _animator.Count > 0 || _closing.Count > 0;
    }

    /// <summary>Lands every motion at once. The thread stops, and the next motion starts it again.</summary>
    internal void Land()
    {
        _stopped = true;
        _wake.Set();
        var thread = _thread;
        if (thread is not null && thread != Thread.CurrentThread)
            thread.Join(TimeSpan.FromSeconds(2));

        IReadOnlyList<Frame> frames;
        List<(WindowId Window, Closing Closing)> closing;
        lock (_gate)
        {
            frames = _animator.Land();
            closing = [.. _closing.Select(pair => (pair.Key, pair.Value))];
            _closing.Clear();
        }

        // A window that was leaving stays shown here; revealing hidden Clients on exit shows it anyway.
        foreach (var frame in frames)
        {
            lock (For(frame.Window))
            {
                if (frame.End == MotionEnd.Close)
                {
                    Rest(frame);
                    continue;
                }

                if (frame.Border is BorderColor border)
                    _target.Border(frame.Window, border);
                Place(frame.Window, frame.Rest);
                _target.Alpha(frame.Window, frame.RestAlpha);
            }
        }

        foreach (var (window, pending) in closing)
        {
            lock (For(window))
            {
                if (_target.Shown(window))
                {
                    Place(window, pending.Rest);
                    _target.Alpha(window, pending.Alpha);
                }
            }
        }

        lock (_gate)
        {
            _epoch++;
            _thread = null;
            _stopped = false;
        }
    }

    public void Dispose()
    {
        Land();
        _wake.Dispose();
    }

    private void Show(Frame frame)
    {
        if (frame.Bounds is Rectangle bounds)
            _target.Move(frame.Window, bounds, frame.Content ?? frame.Goal ?? bounds);
        if (frame.Alpha is byte alpha)
            _target.Alpha(frame.Window, alpha);
        if (frame.Border is BorderColor border)
            _target.Border(frame.Window, border);
    }

    // A window that has stopped answering is sent its rectangle without waiting, so it does not hold up the others.
    private void Place(WindowId window, Rectangle bounds)
    {
        if (_target.Hung(window))
            _target.PlaceAsync(window, bounds);
        else
            _target.Place(window, bounds);
    }

    private void Rest(Frame frame)
    {
        if (frame.Border is BorderColor border)
            _target.Border(frame.Window, border);

        switch (frame.End)
        {
            case MotionEnd.Hide:
                _target.Hide(frame.Window);
                Place(frame.Window, frame.Rest);
                _target.Alpha(frame.Window, frame.RestAlpha);
                return;
            case MotionEnd.Close:
                if (frame.Alpha is byte faded)
                    _target.Alpha(frame.Window, faded);
                _target.Close(frame.Window);
                if (_stopped)
                    return;

                lock (_gate)
                {
                    var grace = _graces.Remove(frame.Window, out var own) ? own : CloseGrace.TotalMilliseconds;
                    _closing[frame.Window] = new Closing(_clock() + grace, frame.Rest, frame.RestAlpha, Stamp(frame.Window));
                }
                return;
            default:
                if (frame.Bounds is Rectangle bounds)
                    Place(frame.Window, bounds);
                else
                    _target.Release(frame.Window);
                if (frame.Alpha is byte alpha)
                    _target.Alpha(frame.Window, alpha);
                return;
        }
    }

    private void Touch(WindowId window) => _stamps[window] = Stamp(window) + 1;

    private long Stamp(WindowId window) => _stamps.GetValueOrDefault(window);

    private void Wake()
    {
        if (!_threaded)
            return;

        lock (_gate)
        {
            if (_thread is null && !_stopped)
            {
                var epoch = _epoch;
                _thread = new Thread(() => Run(epoch)) { IsBackground = true, Name = "Winland motions" };
                _thread.Start();
            }
        }

        _wake.Set();
    }

    private void Run(int epoch)
    {
        while (!_stopped && Volatile.Read(ref _epoch) == epoch)
        {
            bool more;
            try
            {
                more = Tick();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Console.Error.WriteLine(exception.Message);
                more = false;
            }

            if (_stopped || Volatile.Read(ref _epoch) != epoch)
                return;

            // A motion that wakes the thread showed its first frame already, so the next waits for the next vblank.
            if (!more)
                _wake.WaitOne();
            _target.Pace();
        }
    }

    private readonly record struct Closing(double Due, Rectangle Rest, byte Alpha, long Stamp);
}
