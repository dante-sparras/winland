namespace Winland.Domain;

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
