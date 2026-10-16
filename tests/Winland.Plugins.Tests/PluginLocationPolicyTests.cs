using System.Security.AccessControl;
using Winland.Plugins;

namespace Winland.Tests;

[Collection("Winland.Plugins")]
public class PluginLocationPolicyTests
{
    private const string CurrentUser = "S-1-5-21-9";

    [Fact]
    public void NonAdminWriteOnTheDirectoryRefusesTheLoad()
    {
        var denial = PluginLocationPolicy.Deny(
            @"C:\ProgramData\Winland\plugins",
            Owned(
                new PrincipalAccess(AdministratorOnly.SystemSid, CanWrite: true, AppliesToTarget: true),
                new PrincipalAccess(AdministratorOnly.AdministratorsSid, CanWrite: true, AppliesToTarget: true),
                new PrincipalAccess("S-1-5-32-545", CanWrite: true, AppliesToTarget: true)),
            @"C:\ProgramData\Winland\plugins\approved.json",
            approvals: null);

        Assert.NotNull(denial);
        Assert.Contains("non-administrator", denial, StringComparison.Ordinal);
        Assert.Contains("S-1-5-32-545", denial, StringComparison.Ordinal);
        Assert.Contains(@"C:\ProgramData\Winland\plugins", denial, StringComparison.Ordinal);
    }

    [Fact]
    public void NonAdminWriteOnTheApprovalsFileRefusesTheLoad()
    {
        var denial = PluginLocationPolicy.Deny(
            @"C:\ProgramData\Winland\plugins",
            Owned(new PrincipalAccess(AdministratorOnly.AdministratorsSid, CanWrite: true, AppliesToTarget: true)),
            @"C:\ProgramData\Winland\plugins\approved.json",
            Owned(new PrincipalAccess("S-1-5-32-545", CanWrite: true, AppliesToTarget: true)));

        Assert.NotNull(denial);
        Assert.Contains("approved.json", denial, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemAdministratorsAndTrustedInstallerMayWrite()
    {
        var denial = PluginLocationPolicy.Deny(
            @"C:\ProgramData\Winland\plugins",
            Owned(
                new PrincipalAccess(AdministratorOnly.SystemSid, CanWrite: true, AppliesToTarget: true),
                new PrincipalAccess(AdministratorOnly.AdministratorsSid, CanWrite: true, AppliesToTarget: true),
                new PrincipalAccess(AdministratorOnly.TrustedInstallerSid, CanWrite: true, AppliesToTarget: true),
                new PrincipalAccess("S-1-3-0", CanWrite: true, AppliesToTarget: false),
                new PrincipalAccess("S-1-5-32-545", CanWrite: false, AppliesToTarget: true)),
            @"C:\ProgramData\Winland\plugins\approved.json",
            approvals: null);

        Assert.Null(denial);
    }

    // Under UAC every process the user runs without elevation has the user's SID,
    // so a grant to that SID is a grant to any program the user runs.
    [Fact]
    public void TheCurrentUserIsANonAdministratorEvenWhenItIsAnAdministrator()
    {
        var denial = PluginLocationPolicy.Deny(
            @"C:\ProgramData\Winland\plugins",
            Owned(
                new PrincipalAccess(AdministratorOnly.AdministratorsSid, CanWrite: true, AppliesToTarget: true),
                new PrincipalAccess(CurrentUser, CanWrite: true, AppliesToTarget: true)),
            @"C:\ProgramData\Winland\plugins\approved.json",
            approvals: null);

        Assert.NotNull(denial);
        Assert.Contains(CurrentUser, denial, StringComparison.Ordinal);
    }

    // An owner may always rewrite the access list, so it can grant itself write.
    [Fact]
    public void ANonAdministratorOwnerRefusesTheLoad()
    {
        var directory = new AccessList(
            CurrentUser,
            [new PrincipalAccess(AdministratorOnly.AdministratorsSid, CanWrite: true, AppliesToTarget: true)]);
        var approvals = new AccessList(
            CurrentUser,
            [new PrincipalAccess(AdministratorOnly.AdministratorsSid, CanWrite: true, AppliesToTarget: true)]);

        var onDirectory = PluginLocationPolicy.Deny(@"C:\p", directory, @"C:\p\approved.json", null);
        var onApprovals = PluginLocationPolicy.Deny(@"C:\p", Owned(), @"C:\p\approved.json", approvals);
        var unknown = PluginLocationPolicy.Deny(@"C:\p", new AccessList(null, []), @"C:\p\approved.json", null);

        Assert.NotNull(onDirectory);
        Assert.Contains(CurrentUser, onDirectory, StringComparison.Ordinal);
        Assert.NotNull(onApprovals);
        Assert.Contains("approved.json", onApprovals, StringComparison.Ordinal);
        Assert.NotNull(unknown);
    }

    [Theory]
    [InlineData(AdministratorOnly.SystemSid, true)]
    [InlineData(AdministratorOnly.AdministratorsSid, true)]
    [InlineData(AdministratorOnly.TrustedInstallerSid, true)]
    [InlineData("s-1-5-32-544", true)]
    [InlineData(CurrentUser, false)]
    [InlineData("S-1-5-32-545", false)]
    [InlineData("S-1-1-0", false)]
    [InlineData("S-1-5-11", false)]
    public void OnlyThreePrincipalsCountAsAdministrators(string sid, bool allowed)
    {
        Assert.Equal(allowed, AdministratorOnly.IsAllowed(sid));
    }

    [Fact]
    public void PathsComeFromTheOverrideThenTheVariableThenProgramData()
    {
        Assert.Equal(
            @"D:\plugins",
            PluginPaths.DefaultDirectory(@"D:\plugins", @"E:\ignored", @"C:\ProgramData"));
        Assert.Equal(
            @"E:\from-env",
            PluginPaths.DefaultDirectory(null, @"E:\from-env", @"C:\ProgramData"));
        Assert.Equal(
            Path.Combine(@"C:\ProgramData", "Winland", "plugins"),
            PluginPaths.DefaultDirectory(null, " ", @"C:\ProgramData"));
        Assert.Equal(
            Path.Combine(@"D:\plugins", "approved.json"),
            PluginPaths.DefaultApprovals(null, null, @"D:\plugins"));
    }

    // FileSystemRights values: read and execute, read, read permissions, full control, modify,
    // write, create files, append, delete, change permissions, take ownership, then generic rights.
    [Theory]
    [InlineData(0x1200A9, false)]
    [InlineData(0x120089, false)]
    [InlineData(0x20000, false)]
    [InlineData(0x1F01FF, true)]
    [InlineData(0x1301BF, true)]
    [InlineData(0x116, true)]
    [InlineData(0x2, true)]
    [InlineData(0x4, true)]
    [InlineData(0x10000, true)]
    [InlineData(0x40000, true)]
    [InlineData(0x80000, true)]
    [InlineData(0x40000000, true)]
    [InlineData(0x10000000, true)]
    [InlineData(unchecked((int)0x80000000), false)]
    public void OnlyWriteBitsCountAsWrite(int rights, bool write)
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.Equal(write, WindowsAccessReader.CanWrite((FileSystemRights)rights));
    }

    private static AccessList Owned(params PrincipalAccess[] grants) =>
        new(AdministratorOnly.AdministratorsSid, grants);
}
