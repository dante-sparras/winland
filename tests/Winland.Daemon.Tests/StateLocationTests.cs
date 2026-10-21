using Winland;

namespace Winland.Daemon.Tests;

public class StateLocationTests
{
    private const string User = "S-1-5-21-9";
    private const string Standard = @"C:\Users\me\.winland\state.json";

    [Fact]
    public void WithoutElevationTheVariableIsFollowedWithoutAsking()
    {
        var asked = new List<string>();

        var chosen = StateLocation.Choose(@"D:\session\state.json", Standard, elevated: false, folder => Ask(asked, folder, "no"));
        var unset = StateLocation.Choose(" ", Standard, elevated: true, folder => Ask(asked, folder, "no"));

        Assert.Equal((@"D:\session\state.json", (string?)null), chosen);
        Assert.Equal((Standard, (string?)null), unset);
        Assert.Empty(asked);
    }

    // Any program the user runs can set the user's environment. Elevated, the variable is followed
    // only to a folder the check passes, and otherwise the default is used and the reason logged.
    [Fact]
    public void ElevatedTheVariableIsFollowedOnlyToAFolderThatPasses()
    {
        var asked = new List<string>();

        var passed = StateLocation.Choose(@"D:\session\state.json", Standard, elevated: true, folder => Ask(asked, folder, null));
        var refused = StateLocation.Choose(@"C:\Windows\System32\drivers\etc\hosts", Standard, elevated: true, folder => Ask(asked, folder, "only administrators can write it"));

        Assert.Equal((@"D:\session\state.json", (string?)null), passed);
        Assert.Equal(Standard, refused.Path);
        Assert.NotNull(refused.Ignored);
        Assert.Contains(StateLocation.Variable, refused.Ignored, StringComparison.Ordinal);
        Assert.Contains("only administrators can write it", refused.Ignored, StringComparison.Ordinal);
        Assert.Equal([@"D:\session", @"C:\Windows\System32\drivers\etc"], asked);
    }

    [Fact]
    public void AFolderTheUserOwnsOrCanEmptyMayHoldTheStateFile()
    {
        var owned = new AccessList(User, []);
        var granted = new AccessList(
            AdministratorOnly.AdministratorsSid,
            [new PrincipalAccess(User, CanWrite: true, AppliesToTarget: true, CanRemove: true)]);

        Assert.Null(StateLocation.Refusal(@"C:\Users\me\.winland", owned, User));
        Assert.Null(StateLocation.Refusal(@"C:\Users\me\.winland", granted, User));
    }

    // Writing there as an administrator would replace and then delete a file the user could not touch.
    [Fact]
    public void AFolderOnlyAdministratorsCanEmptyIsRefused()
    {
        var system = new AccessList(
            AdministratorOnly.TrustedInstallerSid,
            [
                new PrincipalAccess(AdministratorOnly.AdministratorsSid, CanWrite: true, AppliesToTarget: true, CanRemove: true),
                new PrincipalAccess("S-1-5-32-545", CanWrite: false, AppliesToTarget: true),
            ]);
        var addOnly = new AccessList(
            AdministratorOnly.SystemSid,
            [
                new PrincipalAccess(User, CanWrite: true, AppliesToTarget: true),
                new PrincipalAccess(User, CanWrite: true, AppliesToTarget: false, CanRemove: true),
            ]);

        Assert.NotNull(StateLocation.Refusal(@"C:\Windows\System32\drivers\etc", system, User));
        Assert.NotNull(StateLocation.Refusal(@"C:\Shared", addOnly, User));
        Assert.NotNull(StateLocation.Refusal(@"C:\Unreadable", null, User));
    }

    // A drive root has no folder above it to check, so an elevated Winland does not follow it.
    [Fact]
    public void AVariableThatNamesNoFolderIsIgnoredElevated()
    {
        var asked = new List<string>();

        var chosen = StateLocation.Choose(@"C:\", Standard, elevated: true, folder => Ask(asked, folder, null));

        Assert.Equal(Standard, chosen.Path);
        Assert.NotNull(chosen.Ignored);
        Assert.Contains(@"'C:\' names no folder", chosen.Ignored, StringComparison.Ordinal);
        Assert.Empty(asked);
    }

    [Fact]
    public void ARefusalNamesTheFolder()
    {
        var other = new AccessList(AdministratorOnly.SystemSid, []);

        Assert.Contains(@"C:\Shared", StateLocation.Refusal(@"C:\Shared", other, User), StringComparison.Ordinal);
        Assert.Contains(@"C:\Unreadable", StateLocation.Refusal(@"C:\Unreadable", null, User), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingArgumentsAreRefused()
    {
        var owned = new AccessList(User, []);

        Assert.Throws<ArgumentException>(() => StateLocation.Choose(null, " ", elevated: false, _ => null));
        Assert.Throws<ArgumentNullException>(() => StateLocation.Choose(null, Standard, elevated: false, null!));
        Assert.Throws<ArgumentException>(() => StateLocation.Refusal(" ", owned, User));
        Assert.Throws<ArgumentException>(() => StateLocation.Refusal(@"C:\Shared", owned, " "));
    }

    private static string? Ask(List<string> asked, string folder, string? answer)
    {
        asked.Add(folder);
        return answer;
    }
}
