namespace Winland.Domain;

/// <summary>Who may join a Group. Each path that puts a Client into a Group asks this one rule first.</summary>
internal static class GroupAdmission
{
    /// <summary>
    /// True when a Client with the rule flags <paramref name="joiner"/> may join a Group. A lock on all Groups, a
    /// Group that denies windows, <c>deny</c>, <c>barred</c>, a dialog, a floating Client into a tiled Group and a
    /// locked Group of the Client's own all refuse. A locked target refuses too, unless the Client has <c>invade</c>.
    /// </summary>
    public static bool May(
        GroupRuleFlags joiner,
        bool groupsLocked,
        bool targetLocked,
        bool targetDenied,
        bool dialog,
        bool floatingIntoTiled,
        bool ownLocked) =>
        !groupsLocked
        && (joiner.HasFlag(GroupRuleFlags.Invade) || !targetLocked)
        && !targetDenied
        && !joiner.HasFlag(GroupRuleFlags.Deny)
        && !joiner.HasFlag(GroupRuleFlags.Barred)
        && !dialog
        && !floatingIntoTiled
        && !ownLocked;
}
