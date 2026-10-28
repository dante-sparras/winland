namespace Winland.Domain;

/// <summary>Which binds an edge matches: the key, the modifiers, the flags, and whether a bind consumes the edge.</summary>
public sealed partial class BindSession
{
    private List<Bind> Collect(BindTable table, string key, int scanCode, bool mouse, bool releasing)
    {
        var matches = new List<Bind>();
        foreach (var bind in table.Binds)
        {
            if (Include(table, bind, key, scanCode, mouse, releasing))
                matches.Add(bind);
        }

        return matches;
    }

    private bool Include(BindTable table, Bind bind, string key, int scanCode, bool mouse, bool releasing)
    {
        if (!Eligible(table, bind, key, scanCode, mouse, releasing))
            return false;

        if (releasing)
        {
            if (!bind.Flags.HasFlag(BindFlag.Release) && !SendsBothEdges(bind))
                return false;
            if (_shadowed.Contains(bind))
                return false;
        }

        return true;
    }

    // Mouse, modifiers, the key and the locked flag.
    private bool Eligible(BindTable table, Bind bind, string key, int scanCode, bool mouse, bool releasing)
    {
        if (IsMouseBind(bind) != mouse)
            return false;
        if (!ModifiersMatch(bind, key, mouse, releasing))
            return false;
        if (!KeyEquals(bind.Key, key, scanCode, table.Layout))
            return false;
        if (Inhibited && !bind.Flags.HasFlag(BindFlag.Locked))
            return false;

        return true;
    }

    // A mouse button's bind. A drag bound to a key, through the mouse dispatcher, is the key's.
    private static bool IsMouseBind(Bind bind) =>
        bind.Key.StartsWith("mouse:", StringComparison.OrdinalIgnoreCase);

    // A sendshortcut bind runs on the press and again on the release, so the window it
    // sends to sees both edges.
    private static bool SendsBothEdges(Bind bind) =>
        bind.Dispatcher.Equals("sendshortcut", StringComparison.OrdinalIgnoreCase);

    // A non-consuming bind never consumes. Any other bind does.
    private static bool Consumes(List<Bind> binds) =>
        binds.Exists(bind => !bind.Flags.HasFlag(BindFlag.NonConsuming));

    private void Catch(string key, bool mouse)
    {
        (mouse ? _caughtButtons : _caughtKeys).Add(key);
        foreach (var held in _downKeys)
        {
            if (ModifierKeys.ContainsKey(held))
                _caughtKeys.Add(held);
        }
    }

    private void ReleaseModifier(string key)
    {
        if (!ModifierKeys.TryGetValue(key, out var modifier))
            return;

        foreach (var pair in ModifierKeys)
        {
            if (pair.Value == modifier && _downKeys.Contains(pair.Key))
                return;
        }

        Held &= ~modifier;
    }

    // On release Winland compares the modifiers the key went down under, with the released key's own
    // modifier added, so "bindr = SUPER, Super_L" runs when Super is let go and "bindr = , Super_L" does not, and
    // "bindr = SUPER, K" runs when Super is let go before K.
    private bool ModifiersMatch(Bind bind, string key, bool mouse, bool releasing)
    {
        var mask = releasing && _pressedUnder.TryGetValue(key, out var under) ? under : MaskFor(key, mouse);
        if (releasing && !mouse && ModifierKeys.TryGetValue(key, out var own))
            mask |= own;

        return bind.Modifiers == mask;
    }

    // A modifier keysym is matched against the mask without that key folded in,
    // unless the other side of the same modifier is already held.
    private Modifier MaskFor(string key, bool mouse)
    {
        if (mouse || !ModifierKeys.TryGetValue(key, out var modifier))
            return Held;

        foreach (var pair in ModifierKeys)
        {
            if (pair.Value != modifier || pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;
            if (_downKeys.Contains(pair.Key))
                return Held;
        }

        return Held & ~modifier;
    }

    // A bound key matches either layout's name, or its scan code when written as code:N.
    private static bool KeyEquals(string bound, string key, int scanCode, KeyLayout layout)
    {
        if (bound.Length == 0)
            return false;
        if (bound.Equals(key, StringComparison.OrdinalIgnoreCase))
            return true;

        if (bound.Equals(layout.NameForCode(scanCode), StringComparison.OrdinalIgnoreCase))
            return true;

        return scanCode > 0 && bound.Equals($"code:{scanCode}", StringComparison.OrdinalIgnoreCase);
    }
}
