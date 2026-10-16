namespace Winland.Windows;

/// <summary>
/// Moves a window without waiting for it, for a frame of a motion and a step of a drag. The window's own
/// thread handles the request when it can, so a window that repaints slowly does not hold up the others.
/// A window still busy with the last request is skipped, so requests never pile up behind it. A request
/// that keeps the size only moves the window, which spares the window laying itself out again.
/// It also remembers where Winland last put each window, so the move the window reports back is known
/// to be Winland's own and not one the user or the application made.
/// </summary>
internal sealed class QuickMove
{
    // A window that answers nothing for this many frames in a row is sent the newest frame anyway.
    internal const int MaxSkips = 3;

    // A request the window has not taken in this long is no longer waited for.
    internal const long Patience = 500;

    private readonly Lock _gate = new();
    private readonly Dictionary<WindowId, Pending> _pending = [];
    private readonly Func<long> _clock;

    internal QuickMove(Func<long>? clock = null) => _clock = clock ?? (() => Environment.TickCount64);

    internal readonly record struct Decision(bool Send, bool KeepSize);

    /// <summary>
    /// Whether to send <paramref name="target"/>, given where the window is now and the request it was
    /// last sent. The window has taken a request once it stands where that request put it.
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
            var pending = Live(window);
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

    /// <summary>Notes an outer rectangle Winland put the window on by a direct placement.</summary>
    internal void Note(WindowId window, Rectangle placed)
    {
        lock (_gate)
            _pending[window] = new Pending(placed, 0, _clock());
    }

    /// <summary>
    /// Whether a request may still be on its way to the window. Its thread handles a waiting request after a
    /// placement made directly, so the caller sends the placement again the same way, to land behind it.
    /// </summary>
    internal bool Trails(WindowId window, Rectangle current)
    {
        lock (_gate)
            return Live(window) is { } pending && !SamePlace(current, pending.Requested);
    }

    /// <summary>Whether the window's move is Winland's own: it is on its way to, or stands on, Winland's last request.</summary>
    internal bool Ours(WindowId window, Rectangle current)
    {
        lock (_gate)
        {
            if (Live(window) is not { } pending)
                return false;

            // Standing on the request is Winland's move. Elsewhere, the request is still on its way.
            return current == pending.Requested || !SamePlace(current, pending.Requested);
        }
    }

    internal void Forget(WindowId window)
    {
        lock (_gate)
            _pending.Remove(window);
    }

    private Pending? Live(WindowId window)
    {
        if (!_pending.TryGetValue(window, out var pending))
            return null;

        if (_clock() - pending.Since <= Patience)
            return pending;

        _pending.Remove(window);
        return null;
    }

    private static bool SamePlace(Rectangle a, Rectangle b) => a.X == b.X && a.Y == b.Y;

    private readonly record struct Pending(Rectangle Requested, int Skipped, long Since);
}
