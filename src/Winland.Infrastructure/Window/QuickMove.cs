
namespace Winland.Infrastructure;

/// <summary>
/// Moves a window without waiting for it, for a frame of a motion and a step of a drag. The window's own
/// thread handles the request when it can, so a window that repaints slowly does not hold up the others.
/// A window still busy with the last request is skipped, so requests never pile up behind it. A request
/// that keeps the size only moves the window, which spares the window laying itself out again.
/// It also remembers where Winland recently put each window, so a move the window reports back that lands
/// on one of those places is known to be Winland's own and not one the application made itself.
/// </summary>
internal sealed class QuickMove
{
    // A window that answers nothing for this many frames in a row is sent the newest frame anyway.
    internal const int MaxSkips = 3;

    // A request the window has not taken in this long is no longer waited for or counted as Winland's.
    internal const long Patience = 500;

    // How many recent places are remembered per window. A motion sends one a frame.
    private const int Remembered = 8;

    private readonly Lock _gate = new();
    private readonly Dictionary<WindowId, Pending> _pending = [];
    private readonly Dictionary<WindowId, Queue<Placed>> _recent = [];
    private readonly Func<long> _clock;

    internal QuickMove(Func<long>? clock = null) => _clock = clock ?? (() => Environment.TickCount64);

    internal readonly record struct Decision(bool Send, bool KeepSize);

    /// <summary>
    /// Whether to send <paramref name="target"/>, given where the window is now and the request it was
    /// last sent without waiting. The window has taken a request once it stands where that request put it.
    /// </summary>
    internal static Decision Decide(Rectangle target, Rectangle current, Rectangle? requested, int skipped)
    {
        if (requested is { } last && !SamePlace(current, last) && skipped < MaxSkips)
            return new Decision(false, false);

        if (target == current)
            return new Decision(false, false);

        return new Decision(true, target.Width == current.Width && target.Height == current.Height);
    }

    /// <summary>Asks for <paramref name="target"/>, an outer window rectangle. Returns whether a request was sent.</summary>
    internal bool Request(WindowId window, Rectangle target, Rectangle current, Action<Rectangle, bool> send)
    {
        ArgumentNullException.ThrowIfNull(send);
        Decision decision;
        lock (_gate)
        {
            var pending = Waiting(window);
            decision = Decide(target, current, pending?.Requested, pending?.Skipped ?? 0);
            if (!decision.Send)
            {
                if (pending is { } waiting && target != current)
                    _pending[window] = waiting with { Skipped = waiting.Skipped + 1 };
                return false;
            }

            _pending[window] = new Pending(target, 0, _clock());
        }

        send(target, decision.KeepSize);
        return true;
    }

    /// <summary>
    /// Notes an outer rectangle Winland asked the window for. <paramref name="waiting"/> is true for a request
    /// sent without waiting, which the window may not have taken yet; a direct placement has been taken.
    /// </summary>
    internal void Note(WindowId window, Rectangle placed, bool waiting)
    {
        lock (_gate)
        {
            var now = _clock();
            if (waiting)
                _pending[window] = new Pending(placed, 0, now);
            else
                _pending.Remove(window);

            if (!_recent.TryGetValue(window, out var recent))
                _recent[window] = recent = new Queue<Placed>(Remembered);
            if (recent.Count == Remembered)
                recent.Dequeue();
            recent.Enqueue(new Placed(placed.X, placed.Y, now));
        }
    }

    /// <summary>
    /// Whether a request sent without waiting may still be on its way to the window. Its thread handles it after
    /// a direct placement, so the caller sends the placement again the same way, to land behind it.
    /// </summary>
    internal bool Trails(WindowId window, Rectangle current)
    {
        lock (_gate)
            return Waiting(window) is { } pending && !SamePlace(current, pending.Requested);
    }

    /// <summary>
    /// Whether the window stands where Winland recently put it, so the move it reports is Winland's own.
    /// A move the application made itself lands somewhere else.
    /// </summary>
    internal bool Ours(WindowId window, Rectangle current)
    {
        lock (_gate)
        {
            if (!_recent.TryGetValue(window, out var recent))
                return false;

            var now = _clock();
            foreach (var placed in recent)
            {
                if (now - placed.Since <= Patience && placed.X == current.X && placed.Y == current.Y)
                    return true;
            }

            return false;
        }
    }

    internal void Forget(WindowId window)
    {
        lock (_gate)
        {
            _pending.Remove(window);
            _recent.Remove(window);
        }
    }

    private Pending? Waiting(WindowId window)
    {
        if (!_pending.TryGetValue(window, out var pending))
            return null;

        if (_clock() - pending.Since <= Patience)
            return pending;

        _pending.Remove(window);
        return null;
    }

    // A window has taken a request once it has the request's size as well as its position. A tile that
    // grows in place keeps its origin on every frame, so the origin alone would count a frame as taken early.
    private static bool SamePlace(Rectangle a, Rectangle b) => a == b;

    private readonly record struct Pending(Rectangle Requested, int Skipped, long Since);

    private readonly record struct Placed(int X, int Y, long Since);
}
