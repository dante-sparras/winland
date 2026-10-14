namespace Winland.Core;

/// <summary>
/// Which Bars there are for the monitors the Host lists (ADR 0080): one on each. Every Bar is the same, so which window
/// stands on which monitor matters only for what it costs to get there. A monitor is known by its rectangle (ADR 0079).
/// </summary>
public static class BarSet
{
    /// <summary>What becomes of the Bars when the monitors are <paramref name="screens"/>.</summary>
    /// <param name="bars">The monitor each Bar is on now, whole, in the order the Bars were made. The first is the one the desktop started with.</param>
    /// <param name="screens">The monitors the Host lists now, whole.</param>
    /// <returns>
    /// A Bar whose monitor is still listed stays on it. When there are more Bars than monitors, the newest are removed, so the first Bar always stays.
    /// A Bar that is left and has lost its monitor takes a monitor that has none: the one that covers the most of where it was,
    /// which is its own monitor at a new size, or else the first in the Host's order. Each monitor still without a Bar gets a new one.
    /// With no monitor listed, nothing changes: the Host has not listed them, and a desktop is never without a Bar.
    /// </returns>
    public static BarPlan Follow(IReadOnlyList<TileRect> bars, IReadOnlyList<TileRect> screens)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(screens);
        var kept = new TileRect?[bars.Count];
        if (screens.Count == 0)
        {
            for (var i = 0; i < kept.Length; i++)
            {
                kept[i] = bars[i];
            }

            return new BarPlan(kept, [], Changed: false);
        }

        var stay = Math.Min(bars.Count, screens.Count);
        var free = new List<TileRect>(screens);
        var moved = false;
        for (var i = 0; i < stay; i++)
        {
            if (free.Remove(bars[i]))
            {
                kept[i] = bars[i];
            }
        }

        for (var i = 0; i < stay; i++)
        {
            if (kept[i] is not null)
            {
                continue;
            }

            var best = 0;
            long most = 0;
            for (var at = 0; at < free.Count; at++)
            {
                var shared = Shared(bars[i], free[at]);
                if (shared > most)
                {
                    most = shared;
                    best = at;
                }
            }

            kept[i] = free[best];
            free.RemoveAt(best);
            moved = true;
        }

        return new BarPlan(kept, free, moved || free.Count > 0 || stay < bars.Count);
    }

    /// <summary>
    /// Which Bar is on <paramref name="monitor"/>, for a surface that opens on one monitor: a Panel on the monitor with the keyboard,
    /// or Windows' own notification window on the monitor Windows put it on.
    /// </summary>
    /// <param name="bars">The monitor each Bar is on, whole, in the order the Bars were made.</param>
    /// <param name="monitor">The monitor asked about, whole.</param>
    /// <returns>
    /// The index of the Bar whose monitor that is. A monitor no Bar names, such as one that just changed size, is the Bar's that covers
    /// the most of it. One no Bar lies on at all is the first Bar's, and so is any monitor before a Bar is made.
    /// </returns>
    public static int On(IReadOnlyList<TileRect> bars, TileRect monitor)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var best = 0;
        long most = 0;
        for (var i = 0; i < bars.Count; i++)
        {
            if (bars[i] == monitor)
            {
                return i;
            }

            var shared = Shared(bars[i], monitor);
            if (shared > most)
            {
                most = shared;
                best = i;
            }
        }

        return best;
    }

    private static long Shared(TileRect one, TileRect other)
    {
        var width = Math.Min((long)one.X + one.Width, (long)other.X + other.Width) - Math.Max(one.X, other.X);
        var height = Math.Min((long)one.Y + one.Height, (long)other.Y + other.Height) - Math.Max(one.Y, other.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }
}

/// <summary>What <see cref="BarSet.Follow"/> decided.</summary>
/// <param name="Kept">One entry for each Bar there was, in the same order: the monitor it is on from now, or null when the Bar is removed.</param>
/// <param name="Made">The monitors that get a new Bar, in the Host's order.</param>
/// <param name="Changed">False when every Bar stays where it is and none is made or removed.</param>
public sealed record BarPlan(IReadOnlyList<TileRect?> Kept, IReadOnlyList<TileRect> Made, bool Changed);
