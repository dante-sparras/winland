using Winland;
using Winland.Plugins;

namespace Winland.Daemon.Tests;

public class InstallLocationTests
{
    private const string User = "S-1-5-21-9";

    private static readonly AccessList Protected = new(
        AdministratorOnly.AdministratorsSid,
        [
            new PrincipalAccess(AdministratorOnly.SystemSid, CanWrite: true, AppliesToTarget: true, CanRemove: true),
            new PrincipalAccess(AdministratorOnly.AdministratorsSid, CanWrite: true, AppliesToTarget: true, CanRemove: true),
            new PrincipalAccess("S-1-5-32-545", CanWrite: false, AppliesToTarget: true),
        ]);

    // C:\ as Windows sets it up: authenticated users may make folders in it, and own what they make.
    private static readonly AccessList Drive = new(
        AdministratorOnly.TrustedInstallerSid,
        [
            new PrincipalAccess("S-1-5-11", CanWrite: true, AppliesToTarget: true),
            new PrincipalAccess("S-1-5-11", CanWrite: true, AppliesToTarget: false, CanRemove: true),
            new PrincipalAccess(AdministratorOnly.AdministratorsSid, CanWrite: true, AppliesToTarget: true, CanRemove: true),
        ]);

    [Fact]
    public void ProgramFilesPasses()
    {
        var refusal = InstallLocation.Refusal(
            [(@"C:\Program Files\Winland", Protected), (@"C:\Program Files\Winland\winland.exe", Protected)],
            [(@"C:\Program Files", Protected), (@"C:\", Drive)]);

        Assert.Null(refusal);
    }

    [Fact]
    public void AFileInTheFolderTheUserCanWriteIsRefused()
    {
        var refusal = InstallLocation.Refusal(
            [(@"C:\Program Files\Winland", Protected), (@"C:\Program Files\Winland\winland.dll", new AccessList(User, []))],
            [(@"C:\Program Files", Protected), (@"C:\", Drive)]);

        Assert.NotNull(refusal);
        Assert.Contains(@"C:\Program Files\Winland\winland.dll", refusal, StringComparison.Ordinal);
        Assert.Contains("can write", refusal, StringComparison.Ordinal);
    }

    // A protected folder inside one the user owns can be renamed away, and another put in its place.
    [Fact]
    public void AFolderAboveThatTheUserOwnsOrCanEmptyIsRefused()
    {
        var owned = InstallLocation.Refusal(
            [(@"C:\Tools\Winland", Protected)],
            [(@"C:\Tools", new AccessList(User, [])), (@"C:\", Drive)]);
        var emptied = InstallLocation.Refusal(
            [(@"C:\Tools\Winland", Protected)],
            [(@"C:\Tools", Protected with { Grants = [.. Protected.Grants, new PrincipalAccess(User, CanWrite: true, AppliesToTarget: true, CanRemove: true)] }), (@"C:\", Drive)]);
        var unreadable = InstallLocation.Refusal([(@"C:\Tools\Winland", Protected)], [(@"C:\Tools", null)]);

        Assert.NotNull(owned);
        Assert.Contains(@"'C:\Tools'", owned, StringComparison.Ordinal);
        Assert.Contains("rename or replace", owned, StringComparison.Ordinal);
        Assert.NotNull(emptied);
        Assert.Contains(User, emptied, StringComparison.Ordinal);
        Assert.NotNull(unreadable);
    }

    [Fact]
    public void NoTreeOrNoAncestorsIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => InstallLocation.Refusal(null!, []));
        Assert.Throws<ArgumentNullException>(() => InstallLocation.Refusal([], null!));
    }
}
