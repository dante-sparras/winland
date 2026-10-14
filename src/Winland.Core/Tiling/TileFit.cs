namespace Winland.Core.Tiling;

/// <summary>
/// Places a session's tiles until each window keeps a size the tile can hold.
/// A window is asked its minimum when it joins, when the area changes, and after it kept a larger size than its tile, not on every place.
/// </summary>
public static class TileFit
{
    /// <summary>
    /// Reads the minimums <paramref name="windows"/> report, then places until nothing kept a larger size than its tile.
    /// </summary>
    /// <param name="session">The workspace's tiles.</param>
    /// <param name="windows">Reads minimums and puts tiles on the glass.</param>
    /// <param name="area">The rectangle the tiles share.</param>
    /// <param name="jump">True lands at once. False glides.</param>
    /// <returns>The session's tiles as they went onto the glass last.</returns>
    public static IReadOnlyList<Placement> Place(TilingSession session, ITilePlacement windows, TileRect area, bool jump) =>
        Place(session, windows, area, area, jump);

    /// <summary>
    /// Reads the minimums <paramref name="windows"/> report for windows not yet asked in this area, then places until nothing kept a larger size than its tile.
    /// The fullscreen window is not taught the monitor's size as its minimum, and a <paramref name="held"/> window keeps the minimum it had.
    /// </summary>
    /// <param name="session">The workspace's tiles.</param>
    /// <param name="windows">Reads minimums and puts tiles on the glass.</param>
    /// <param name="area">The rectangle the tiles share.</param>
    /// <param name="cover">The monitor the fullscreen window covers.</param>
    /// <param name="jump">True lands at once. False glides.</param>
    /// <param name="also">Windows drawn with the tiles, such as a sticky window or an open scratchpad.</param>
    /// <param name="held">
    /// Windows fullscreen in their own app. Such an app reports a smaller minimum than its windowed frame,
    /// and learning it would move every tile beside it.
    /// </param>
    /// <param name="hiding">Runs before each pass goes onto the glass, once the session knows which windows that pass hides.</param>
    /// <returns>
    /// The session's tiles as they went onto the glass last, without <paramref name="also"/>.
    /// While <see cref="TilingSession.Arrange(TileRect, TileRect)"/> answers this same list, the glass shows what the session holds.
    /// </returns>
    public static IReadOnlyList<Placement> Place(TilingSession session, ITilePlacement windows, TileRect area, TileRect cover, bool jump, IReadOnlyList<Placement>? also = null, IReadOnlySet<long>? held = null, Action? hiding = null)
    {
        var ask = session.MinimumsToAsk(area, held);
        var answers = ask.Count == 0 ? Array.Empty<WindowMinimum>() : windows.Minimums(ask);
        foreach (var minimum in answers)
        {
            if (minimum.Id == session.Fullscreen || held?.Contains(minimum.Id) == true)
            {
                continue;
            }

            session.NoteAsked(minimum.Id, area);
            var width = Math.Min(minimum.Width, area.Width);
            var height = Math.Min(minimum.Height, area.Height);
            if (session.ReportMinimum(minimum.Id, width, height))
            {
                Log.Debug("Tiling", "window " + minimum.Id + " reports minimum " + width + "x" + height);
            }
        }

        // A window that did not answer is asked on the next place, since a window that just opened is often busy.
        // Asking waits on its program, so one that stays silent is given up on after a few places.
        foreach (var id in ask)
        {
            var answered = false;
            foreach (var minimum in answers)
            {
                answered |= minimum.Id == id;
            }

            if (!answered)
            {
                session.NoteUnanswered(id, area);
            }
        }

        HashSet<long>? filled = null;
        IReadOnlyList<Placement> tiles = [];
        for (var pass = 0; pass < 4; pass++)
        {
            tiles = session.Arrange(area, cover);
            session.NotePlaced(tiles);
            hiding?.Invoke();
            var placed = Also(tiles, also);
            if (jump)
            {
                windows.PlaceNow(placed);
            }
            else
            {
                windows.Apply(placed);
            }

            // Only this session's own tiles are fitted. A window drawn with them has no tile here to grow, and with a Workspace in front
            // on each monitor the tiles of the others are named in every placement: asking them again would read each window once per monitor.
            var again = false;
            foreach (var size in windows.Overflows(tiles))
            {
                if (size.Id == session.Fullscreen || held?.Contains(size.Id) == true)
                {
                    continue;
                }

                var width = Math.Min(size.Width, area.Width);
                var height = Math.Min(size.Height, area.Height);
                // A window still as large as the whole workspace has usually not taken its new tile yet.
                // Learning that size on the first look makes a split unable to put anything beside it.
                var fills = (width >= area.Width && width > 0) || (height >= area.Height && height > 0);
                if (fills && (filled ??= []).Add(size.Id))
                {
                    again = true;
                    continue;
                }

                session.AskAgain(size.Id);
                if (session.NoteMinimum(size.Id, width, height))
                {
                    Log.Debug("Tiling", "window " + size.Id + " kept " + width + "x" + height + " past its tile");
                    again = true;
                }
            }

            if (!again)
            {
                break;
            }
        }

        return tiles;
    }

    private static IReadOnlyList<Placement> Also(IReadOnlyList<Placement> placed, IReadOnlyList<Placement>? also)
    {
        if (also is null || also.Count == 0)
        {
            return placed;
        }

        var combined = new List<Placement>(placed.Count + also.Count);
        combined.AddRange(placed);
        combined.AddRange(also);
        return combined;
    }
}
