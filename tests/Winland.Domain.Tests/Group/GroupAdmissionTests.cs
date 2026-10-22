namespace Winland.Domain.Tests;

public class GroupAdmissionTests
{
    [Fact]
    public void AnOrdinaryClientMayJoinAnOpenGroup()
    {
        Assert.True(May());
    }

    [Fact]
    public void LockedGroupsRefuseEveryone()
    {
        Assert.False(May(groupsLocked: true));
        Assert.False(May(groupsLocked: true, joiner: GroupRuleFlags.Invade));
    }

    [Fact]
    public void ALockedGroupRefusesUnlessTheClientInvades()
    {
        Assert.False(May(targetLocked: true));
        Assert.True(May(targetLocked: true, joiner: GroupRuleFlags.Invade));
    }

    [Fact]
    public void AGroupThatDeniesWindowsRefusesEvenAnInvader()
    {
        Assert.False(May(targetDenied: true));
        Assert.False(May(targetDenied: true, joiner: GroupRuleFlags.Invade));
    }

    [Theory]
    [InlineData(GroupRuleFlags.Deny)]
    [InlineData(GroupRuleFlags.Barred)]
    [InlineData(GroupRuleFlags.Set | GroupRuleFlags.Barred)]
    public void DenyAndBarredKeepTheClientOut(GroupRuleFlags joiner)
    {
        Assert.False(May(joiner: joiner));
    }

    [Fact]
    public void ADialogDoesNotJoin()
    {
        Assert.False(May(dialog: true));
    }

    [Fact]
    public void AFloatingClientDoesNotJoinATiledGroup()
    {
        Assert.False(May(floatingIntoTiled: true));
    }

    [Fact]
    public void AClientInALockedGroupDoesNotLeaveIt()
    {
        Assert.False(May(ownLocked: true));
        Assert.False(May(ownLocked: true, joiner: GroupRuleFlags.Invade));
    }

    private static bool May(
        GroupRuleFlags joiner = GroupRuleFlags.None,
        bool groupsLocked = false,
        bool targetLocked = false,
        bool targetDenied = false,
        bool dialog = false,
        bool floatingIntoTiled = false,
        bool ownLocked = false) =>
        GroupAdmission.May(joiner, groupsLocked, targetLocked, targetDenied, dialog, floatingIntoTiled, ownLocked);
}
