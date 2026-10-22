namespace Winland.Domain;

/// <summary>Which binds an edge matches: the key, the modifiers, the submap, the flags, and whether a bind consumes the edge.</summary>
public static partial class BindMatcher
{
    // The anchor is where the released key went down. Only a release reads it.
    private static List<Bind> Collect(BindTable table, BindSession session, string key, int scanCode, bool mouse, bool releasing, string submap, Point anchor = default)
    {
        var matches = new List<Bind>();
        foreach (var bind in table.Active(submap))
        {
            if (IsCatchAll(bind) || !Include(bind, table, session, key, scanCode, mouse, releasing, anchor, catchAll: false))
                continue;

            matches.Add(bind);
            if (DispatchesNow(bind, releasing) && IsSubmapDispatcher(bind))
                break;
        }

        if (matches.Count == 0 && submap == session.Submap)
        {
            foreach (var bind in table.Active(submap))
            {
                if (!IsCatchAll(bind) || !Include(bind, table, session, key, scanCode, mouse, releasing, anchor, catchAll: true))
                    continue;

                matches.Add(bind);
                if (DispatchesNow(bind, releasing) && IsSubmapDispatcher(bind))
                    break;
            }
        }

        return matches;
    }

    private static bool Include(Bind bind, BindTable table, BindSession session, string key, int scanCode, bool mouse, bool releasing, Point anchor, bool catchAll)
    {
        if (!Eligible(bind, session, key, scanCode, mouse, releasing, anyKey: catchAll))
            return false;

        if (releasing)
        {
            if (!bind.Flags.HasFlag(BindFlag.Release) && !SendsBothEdges(bind))
                return false;
            if (!mouse && Listed(session.Suppressed, key, bind))
                return false;
            if (session.Shadowed.Contains(bind))
                return false;
            if (!mouse && bind.Flags.HasFlag(BindFlag.Release) && Parts(bind).Length > 1 && !Listed(session.Armed, key, bind))
                return false;
            if (!WithinGesture(table, session, bind, anchor))
                return false;
        }

        return true;
    }

    // Mouse, modifiers, the key unless any key will do, the locked flag, and the gesture flags.
    private static bool Eligible(Bind bind, BindSession session, string key, int scanCode, bool mouse, bool releasing, bool anyKey)
    {
        if (IsMouseBind(bind) != mouse)
            return false;
        // A release chord armed on this key matched the modifiers held when the key went down.
        var armed = releasing && !mouse && Listed(session.Armed, key, bind);
        if (!bind.Flags.HasFlag(BindFlag.IgnoreModifiers) && !armed && !ModifiersMatch(bind, session, key, mouse, releasing))
            return false;
        if (!anyKey && !KeyMatches(bind, key, scanCode))
            return false;
        if (!MatchesDevice(bind, session))
            return false;
        if (session.Inhibited && !bind.Flags.HasFlag(BindFlag.Locked))
            return false;
        if (!releasing && bind.Flags.HasFlag(BindFlag.Release) && bind.Flags.HasFlag(BindFlag.LongPress))
            return false;

        return true;
    }

    // A mouse button's bind. A drag bound to a key, through the mouse dispatcher, is the key's.
    private static bool IsMouseBind(Bind bind) =>
        bind.Key.StartsWith("mouse:", StringComparison.OrdinalIgnoreCase);

    private static bool IsCatchAll(Bind bind) =>
        bind.Key.Equals("catchall", StringComparison.Ordinal);

    private static bool IsSubmapDispatcher(Bind bind) =>
        bind.Dispatcher.Equals("submap", StringComparison.OrdinalIgnoreCase);

    // A sendshortcut bind runs on the press and again on the release, so the window it
    // sends to sees both edges.
    private static bool SendsBothEdges(Bind bind) =>
        !bind.Flags.HasFlag(BindFlag.LongPress)
        && bind.Dispatcher.Equals("sendshortcut", StringComparison.OrdinalIgnoreCase);

    private static bool DispatchesNow(Bind bind, bool releasing)
    {
        var release = bind.Flags.HasFlag(BindFlag.Release);
        var longPress = bind.Flags.HasFlag(BindFlag.LongPress);
        if (releasing)
            return release;

        return !release && !longPress;
    }

    private static bool WithinGesture(BindTable table, BindSession session, Bind bind, Point anchor)
    {
        var click = bind.Flags.HasFlag(BindFlag.Click);
        var drag = bind.Flags.HasFlag(BindFlag.Drag);
        if (!click && !drag)
            return true;

        var past = PastThreshold(table, session, anchor);
        if (click && past)
            return false;
        if (drag && !past)
            return false;

        return true;
    }

    private static bool PastThreshold(BindTable table, BindSession session, Point anchor)
    {
        var dx = (long)session.Pointer.X - anchor.X;
        var dy = (long)session.Pointer.Y - anchor.Y;
        var limit = (long)table.DragThreshold * table.DragThreshold;
        return (dx * dx) + (dy * dy) > limit;
    }

    private readonly record struct Claim(bool Hard, bool Conditional);

    // A non-consuming bind never consumes. An auto-consuming bind consumes only when its dispatcher succeeds, so the
    // press is swallowed now and given back on failure. Any other bind consumes outright.
    private static Claim Classify(List<Bind> binds)
    {
        var hard = false;
        var conditional = false;
        foreach (var bind in binds)
        {
            if (bind.Flags.HasFlag(BindFlag.NonConsuming))
                continue;
            if (bind.Flags.HasFlag(BindFlag.AutoConsuming))
                conditional = true;
            else
                hard = true;
        }

        return new Claim(hard, conditional);
    }

    private static bool CanFail(List<Bind> binds)
    {
        foreach (var bind in binds)
        {
            if (bind.Dispatcher.Length > 0 && bind.Flags.HasFlag(BindFlag.AutoConsuming) && !bind.Flags.HasFlag(BindFlag.NonConsuming))
                return true;
        }

        return false;
    }

    private static void Catch(BindSession session, string key, bool mouse, List<string>? added)
    {
        NoteCaught(mouse ? session.CaughtButtons : session.CaughtKeys, key, added);
        foreach (var held in session.DownKeys)
        {
            if (ModifierKeys.ContainsKey(held))
                NoteCaught(session.CaughtKeys, held, added);
        }
    }

    private static void NoteCaught(HashSet<string> caught, string key, List<string>? added)
    {
        if (caught.Add(key) && added is not null)
            added.Add(key);
    }

    private static void ReleaseModifier(BindSession session, string key)
    {
        if (!ModifierKeys.TryGetValue(key, out var modifier))
            return;

        foreach (var pair in ModifierKeys)
        {
            if (pair.Value == modifier && session.DownKeys.Contains(pair.Key))
                return;
        }

        session.Held &= ~modifier;
    }

    // On release Winland compares the modifiers the key went down under, with the released key's own
    // modifier added, so "bindr = SUPER, Super_L" runs when Super is let go and "bindr = , Super_L" does not, and
    // "bindr = SUPER, K" runs when Super is let go before K.
    private static bool ModifiersMatch(Bind bind, BindSession session, string key, bool mouse, bool releasing)
    {
        if (!releasing && session.DeviceMatchMask is Modifier forced)
            return bind.Modifiers == forced;

        var mask = releasing && session.PressedUnder.TryGetValue(key, out var under) ? under : MaskFor(session, key, mouse);
        if (releasing && !mouse && ModifierKeys.TryGetValue(key, out var own))
            mask |= own;

        return bind.Modifiers == mask;
    }

    // A modifier keysym is matched against the mask without that key folded in,
    // unless the other side of the same modifier is already held.
    private static Modifier MaskFor(BindSession session, string key, bool mouse)
    {
        if (mouse || !ModifierKeys.TryGetValue(key, out var modifier))
            return session.Held;

        foreach (var pair in ModifierKeys)
        {
            if (pair.Value != modifier || pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;
            if (session.DownKeys.Contains(pair.Key))
                return session.Held;
        }

        return session.Held & ~modifier;
    }

    // A bind on several keys is triggered by its last key.
    private static bool KeyMatches(Bind bind, string key, int scanCode)
    {
        var parts = Parts(bind);
        return KeyEquals(parts[^1], key, scanCode);
    }

    private static string[] Parts(Bind bind) =>
        bind.Flags.HasFlag(BindFlag.MultiKey) ? bind.Key.Split('&', StringSplitOptions.TrimEntries) : [bind.Key];

    private static bool Contains(Bind bind, string key, int scanCode)
    {
        foreach (var part in Parts(bind))
        {
            if (KeyEquals(part, key, scanCode))
                return true;
        }

        return false;
    }

    private static bool Listed(Dictionary<string, List<Bind>> perKey, string key, Bind bind) =>
        perKey.TryGetValue(key, out var binds) && binds.Exists(listed => ReferenceEquals(listed, bind));
}
