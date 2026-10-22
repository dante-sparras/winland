namespace Winland.Domain;

/// <summary>Binds on several keys: matching the held keys in order, holding back a bind a longer chord could extend, and suppressing the shorter binds a chord covers.</summary>
public static partial class BindMatcher
{
    private enum KeyMatch
    {
        None,
        Partial,
        Full,
    }

    private readonly record struct ChordMatches(List<Bind> Immediate, int HeldBack, bool Claims);

    // A key press as Winland resolves it. A bind on several keys matches the held keys in the order they went
    // down: full once its last key completes them, partial while the press extends a prefix of it. A single key is full
    // when pressed and partial while it is held under another key. A full bind that a longer partial chord could still
    // extend waits for its key's release, and one a longer full chord covers does not run. A partial chord consumes the
    // press unless it is non-consuming or a release bind. A release chord completed in order runs on its last key's release.
    private static ChordMatches MatchChords(BindTable table, BindSession session, string key, int scanCode)
    {
        var full = new List<Bind>();
        var partial = new List<Bind>();
        var completed = new List<Bind>();
        foreach (var bind in table.Active(session.Submap))
        {
            if (IsCatchAll(bind) || !Eligible(bind, session, key, scanCode, mouse: false, releasing: false, anyKey: true))
                continue;

            var match = MatchKeys(bind, session, key, scanCode);
            if (match == KeyMatch.None)
                continue;

            if (bind.Flags.HasFlag(BindFlag.Release) && Parts(bind).Length > 1)
            {
                if (match == KeyMatch.Full)
                {
                    Add(BindSession.Of(session.Armed, key), bind);
                    completed.Add(bind);
                }

                partial.Add(bind);
                continue;
            }

            if (match == KeyMatch.Partial)
            {
                partial.Add(bind);
                continue;
            }

            full.Add(bind);
            if (DispatchesNow(bind, releasing: false) && IsSubmapDispatcher(bind))
                break;
        }

        var immediate = new List<Bind>();
        var heldBack = 0;
        foreach (var bind in full)
        {
            // A release or long-press bind on one key keeps its own timing.
            if (bind.Flags.HasFlag(BindFlag.Release) || bind.Flags.HasFlag(BindFlag.LongPress))
            {
                immediate.Add(bind);
                continue;
            }

            if (full.Exists(other => !ReferenceEquals(other, bind) && SubChord(bind, other, session)))
                continue;

            if (partial.Exists(other => SubChord(bind, other, session)))
            {
                Add(BindSession.Of(session.HeldBack, key), bind);
                completed.Add(bind);
                heldBack++;
                continue;
            }

            immediate.Add(bind);
            completed.Add(bind);
        }

        // A chord that ran, waits for its key's release, or is armed for it holds off the shorter binds inside it.
        foreach (var bind in completed)
        {
            if (Parts(bind).Length > 1)
                Suppress(table, session, bind);
        }

        var claims = partial.Exists(bind => !bind.Flags.HasFlag(BindFlag.NonConsuming) && !bind.Flags.HasFlag(BindFlag.Release) && !bind.Flags.HasFlag(BindFlag.AutoConsuming));
        if (full.Count == 0 && partial.Count == 0)
        {
            foreach (var bind in table.Active(session.Submap))
            {
                if (!IsCatchAll(bind) || !Include(bind, table, session, key, scanCode, mouse: false, releasing: false, anchor: default, catchAll: true))
                    continue;

                immediate.Add(bind);
                if (DispatchesNow(bind, releasing: false) && IsSubmapDispatcher(bind))
                    break;
            }
        }

        return new ChordMatches(immediate, heldBack, claims);
    }

    private static KeyMatch MatchKeys(Bind bind, BindSession session, string key, int scanCode)
    {
        var parts = Parts(bind);
        if (parts.Length == 1)
        {
            if (KeyEquals(parts[0], key, scanCode))
                return KeyMatch.Full;

            foreach (var held in session.Order)
            {
                if (KeyEquals(parts[0], held.Key, held.ScanCode))
                    return KeyMatch.Partial;
            }

            return KeyMatch.None;
        }

        // Only the press that advances the prefix matches.
        var matched = 0;
        var triggered = false;
        foreach (var held in session.Order)
        {
            if (matched == parts.Length)
                break;
            if (!KeyEquals(parts[matched], held.Key, held.ScanCode))
                continue;

            matched++;
            triggered = held.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        if (!triggered)
            return KeyMatch.None;

        return matched == parts.Length && matched == session.Order.Count ? KeyMatch.Full : KeyMatch.Partial;
    }

    // True when every key of the shorter bind is held and is also a key of the longer one.
    private static bool SubChord(Bind shorter, Bind longer, BindSession session)
    {
        var mine = Parts(shorter);
        var theirs = Parts(longer);
        if (mine.Length >= theirs.Length)
            return false;

        var usedHeld = new bool[session.Order.Count];
        var usedTheirs = new bool[theirs.Length];
        foreach (var part in mine)
        {
            var matched = false;
            for (var h = 0; h < session.Order.Count && !matched; h++)
            {
                var (held, code) = session.Order[h];
                if (usedHeld[h] || !KeyEquals(part, held, code))
                    continue;

                for (var o = 0; o < theirs.Length; o++)
                {
                    if (usedTheirs[o] || !KeyEquals(theirs[o], held, code))
                        continue;

                    usedHeld[h] = true;
                    usedTheirs[o] = true;
                    matched = true;
                    break;
                }
            }

            if (!matched)
                return false;
        }

        return true;
    }

    // A completed chord stops the shorter binds inside it from running on the release of its keys.
    private static void Suppress(BindTable table, BindSession session, Bind completed)
    {
        var shorter = new List<Bind>();
        foreach (var bind in table.Active(session.Submap))
        {
            if (!ReferenceEquals(bind, completed) && SubChord(bind, completed, session))
                shorter.Add(bind);
        }

        foreach (var (held, code) in session.Order)
        {
            if (!Contains(completed, held, code))
                continue;

            foreach (var bind in shorter)
            {
                if (!Contains(bind, held, code))
                    continue;

                if (session.HeldBack.TryGetValue(held, out var back))
                    back.RemoveAll(listed => ReferenceEquals(listed, bind));
                Add(BindSession.Of(session.Suppressed, held), bind);
            }
        }
    }

    private static void Add(List<Bind> binds, Bind bind)
    {
        if (!binds.Exists(listed => ReferenceEquals(listed, bind)))
            binds.Add(bind);
    }

    private static bool KeyEquals(string bound, string key, int scanCode)
    {
        if (bound.Length == 0)
            return false;
        if (bound.Equals(key, StringComparison.OrdinalIgnoreCase))
            return true;

        return scanCode > 0 && bound.Equals($"code:{scanCode}", StringComparison.OrdinalIgnoreCase);
    }
}
