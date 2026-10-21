namespace Winland;

/// <summary>The options of a <c>group</c> window rule, as flags.</summary>
[Flags]
public enum GroupRuleFlags
{
    None = 0,

    /// <summary>Open as a new Group, or join the focused one.</summary>
    Set = 1 << 0,
    SetAlways = 1 << 1,

    /// <summary>Do not join the focused Group automatically.</summary>
    Barred = 1 << 2,

    /// <summary>Lock the Group this window makes.</summary>
    Lock = 1 << 3,
    LockAlways = 1 << 4,

    /// <summary>Join the focused Group even when it is locked.</summary>
    Invade = 1 << 5,
    Override = 1 << 6,

    /// <summary>Never join a Group, and stop others from joining the one this window makes.</summary>
    Deny = 1 << 7,
}

internal static class GroupRules
{
    /// <summary>
    /// Reads the space-separated options of a <c>group</c> rule the way Winland does.
    /// An empty value is the manual's shorthand for <c>set</c>.
    /// </summary>
    public static GroupRuleFlags Parse(string? text)
    {
        var value = text?.Trim() ?? "";
        if (value.Length == 0)
            return GroupRuleFlags.Set;

        var flags = GroupRuleFlags.None;
        var previous = "";
        foreach (var option in value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (option == "group")
            {
                previous = option;
                continue;
            }

            switch (option)
            {
                case "set":
                    flags |= GroupRuleFlags.Set;
                    break;
                case "new":
                    flags |= GroupRuleFlags.Set | GroupRuleFlags.Barred;
                    break;
                case "lock":
                    flags |= GroupRuleFlags.Lock;
                    break;
                case "invade":
                    flags |= GroupRuleFlags.Invade;
                    break;
                case "barred":
                    flags |= GroupRuleFlags.Barred;
                    break;
                case "deny":
                    flags |= GroupRuleFlags.Deny;
                    break;
                case "override":
                    flags = GroupRuleFlags.Override;
                    break;
                case "unset":
                    return GroupRuleFlags.Override;
                case "always" when previous is "set" or "group":
                    flags |= GroupRuleFlags.SetAlways;
                    break;
                case "always" when previous == "lock":
                    flags |= GroupRuleFlags.LockAlways;
                    break;
            }

            previous = option;
        }

        return flags;
    }

    /// <summary>True when the option text holds only words the rule knows.</summary>
    public static bool IsValid(string? text)
    {
        foreach (var option in (text ?? "").Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (option is not ("group" or "set" or "new" or "lock" or "invade" or "barred" or "deny" or "override" or "unset" or "always"))
                return false;
        }

        return true;
    }
}
