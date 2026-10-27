namespace Winland.Infrastructure;

/// <summary>
/// The WindowManager decides outcomes under its lock, but they are applied after the lock is released, on the main
/// thread, the pipe threads and the group bar thread, so an older outcome can arrive after a newer one.
/// Each window's visibility, placement and decoration, and the focus, remember the sequence of the outcome
/// that last set them. An older outcome loses each part a newer one already decided, and keeps the rest.
/// Its one-time requests, such as a close, a launch or a kill, always run. A restack loses only the windows
/// a newer one restacked, and a cursor warp loses only to a newer warp.
/// </summary>
internal sealed class ApplyOrder
{
    private readonly Lock _gate = new();
    private readonly Dictionary<long, Ledger> _cores = [];

    /// <summary>The outcome to apply. <see cref="Current"/> is false when a newer one was already applied,
    /// so the state that every outcome carries whole, such as the group bars and the input settings, is skipped.</summary>
    internal readonly record struct Admitted(Outcome Outcome, bool Current);

    internal Admitted Admit(Outcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Order is not OutcomeOrder order)
            return new Admitted(outcome, true);

        lock (_gate)
        {
            if (!_cores.TryGetValue(order.WindowManager, out var ledger))
                _cores[order.WindowManager] = ledger = new Ledger();

            return ledger.Admit(outcome, order.Sequence);
        }
    }

    /// <summary>
    /// Whether an outcome newer than <paramref name="outcome"/> was admitted with a placement for
    /// <paramref name="window"/> after this one was, so this one's placement, about to be made, would land last and win.
    /// </summary>
    internal bool Superseded(Outcome outcome, WindowId window)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Order is not OutcomeOrder order)
            return false;

        lock (_gate)
            return _cores.TryGetValue(order.WindowManager, out var ledger) && ledger.PlacedAfter(window, order.Sequence);
    }

    private sealed class Ledger
    {
        internal bool PlacedAfter(WindowId window, long sequence) =>
            _placed.TryGetValue(window, out var placed) && placed > sequence;

        private readonly Dictionary<WindowId, long> _shown = [];
        private readonly Dictionary<WindowId, long> _placed = [];
        private readonly Dictionary<WindowId, long> _painted = [];
        private readonly Dictionary<WindowId, long> _stacked = [];
        private long _latest;
        private long _focus;
        private long _warp;

        internal Admitted Admit(Outcome outcome, long sequence)
        {
            var current = sequence > _latest;
            if (current)
                _latest = sequence;

            var windows = new Dictionary<ClientId, WindowId>(outcome.State.Clients.Count);
            foreach (var client in outcome.State.Clients)
                windows[client.Id] = client.Window;

            var dropped = false;
            var hide = Keep(outcome.Hide, _shown, sequence, ref dropped);
            var show = Keep(outcome.Show, _shown, sequence, ref dropped);
            var minimized = Keep(outcome.ShowMinimized, _shown, sequence, ref dropped);
            var resets = Keep(outcome.ResetDecorations, _painted, sequence, ref dropped);
            var placements = KeepFor(outcome.Placements, placement => placement.Client, windows, _placed, sequence, ref dropped);
            var decorations = KeepFor(outcome.Decorations, decoration => decoration.Client, windows, _painted, sequence, ref dropped);
            var motions = KeepMotions(outcome.Motions, hide, sequence, ref dropped);

            var focus = outcome.Focus;
            if (focus is not null)
            {
                if (sequence >= _focus)
                {
                    _focus = sequence;
                }
                else
                {
                    focus = null;
                    dropped = true;
                }
            }

            // Restacking and moving the cursor are one-off requests. Each loses only to a newer one of its kind.
            var zOrder = KeepFor(outcome.ZOrder, request => request.Window, _stacked, sequence, ref dropped);
            var warp = outcome.Warp;
            if (warp is not null)
            {
                if (sequence >= _warp)
                {
                    _warp = sequence;
                }
                else
                {
                    warp = null;
                    dropped = true;
                }
            }

            if (current)
                Prune(windows);

            if (!dropped)
                return new Admitted(outcome, current);

            return new Admitted(
                outcome with
                {
                    Hide = hide,
                    Show = show,
                    ShowMinimized = minimized,
                    ResetDecorations = resets,
                    Placements = placements,
                    Decorations = decorations,
                    Motions = motions,
                    Focus = focus,
                    ZOrder = zOrder,
                    Warp = warp,
                },
                current);
        }

        private static IReadOnlyList<WindowId> Keep(
            IReadOnlyList<WindowId> windows,
            Dictionary<WindowId, long> latest,
            long sequence,
            ref bool dropped)
        {
            if (windows.Count == 0)
                return windows;

            var kept = new List<WindowId>(windows.Count);
            foreach (var window in windows)
            {
                if (Claim(latest, window, sequence))
                    kept.Add(window);
            }

            dropped |= kept.Count != windows.Count;
            return kept.Count == windows.Count ? windows : kept;
        }

        private static IReadOnlyList<T> KeepFor<T>(
            IReadOnlyList<T> items,
            Func<T, WindowId> windowOf,
            Dictionary<WindowId, long> latest,
            long sequence,
            ref bool dropped)
        {
            if (items.Count == 0)
                return items;

            var kept = new List<T>(items.Count);
            foreach (var item in items)
            {
                if (Claim(latest, windowOf(item), sequence))
                    kept.Add(item);
            }

            dropped |= kept.Count != items.Count;
            return kept.Count == items.Count ? items : kept;
        }

        private static IReadOnlyList<T> KeepFor<T>(
            IReadOnlyList<T> items,
            Func<T, ClientId> clientOf,
            Dictionary<ClientId, WindowId> windows,
            Dictionary<WindowId, long> latest,
            long sequence,
            ref bool dropped)
        {
            if (items.Count == 0)
                return items;

            var kept = new List<T>(items.Count);
            foreach (var item in items)
            {
                // A part for a Client that is not in the outcome's state is skipped when it is applied anyway.
                if (!windows.TryGetValue(clientOf(item), out var window) || Claim(latest, window, sequence))
                    kept.Add(item);
            }

            dropped |= kept.Count != items.Count;
            return kept.Count == items.Count ? items : kept;
        }

        // A motion goes with the part it animates: a hide motion with the hide, any other with the placement.
        private IReadOnlyList<Motion> KeepMotions(IReadOnlyList<Motion> motions, IReadOnlyList<WindowId> hide, long sequence, ref bool dropped)
        {
            if (motions.Count == 0)
                return motions;

            var kept = new List<Motion>(motions.Count);
            foreach (var motion in motions)
            {
                var keep = motion.End switch
                {
                    MotionEnd.Close => true,
                    MotionEnd.Hide => hide.Contains(motion.Window),
                    _ => Claim(_placed, motion.Window, sequence),
                };
                if (keep)
                    kept.Add(motion);
            }

            dropped |= kept.Count != motions.Count;
            return kept.Count == motions.Count ? motions : kept;
        }

        // The same outcome may claim a window twice, for example in Show and ShowMinimized.
        private static bool Claim(Dictionary<WindowId, long> latest, WindowId window, long sequence)
        {
            if (latest.GetValueOrDefault(window) > sequence)
                return false;

            latest[window] = sequence;
            return true;
        }

        // A window that is no longer a Client has nothing left to order. The ledgers stay within twice the Clients.
        private void Prune(Dictionary<ClientId, WindowId> clients)
        {
            var limit = (2 * clients.Count) + 16;
            if (_shown.Count <= limit && _placed.Count <= limit && _painted.Count <= limit && _stacked.Count <= limit)
                return;

            var keep = new HashSet<WindowId>(clients.Values);
            Drop(_shown, keep);
            Drop(_placed, keep);
            Drop(_painted, keep);
            Drop(_stacked, keep);
        }

        private static void Drop(Dictionary<WindowId, long> latest, HashSet<WindowId> keep)
        {
            List<WindowId>? stale = null;
            foreach (var window in latest.Keys)
            {
                if (!keep.Contains(window))
                    (stale ??= []).Add(window);
            }

            if (stale is null)
                return;

            foreach (var window in stale)
                latest.Remove(window);
        }
    }
}
