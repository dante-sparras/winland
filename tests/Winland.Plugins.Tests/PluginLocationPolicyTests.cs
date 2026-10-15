using System.Security.AccessControl;
using Winland.Plugins;

namespace Winland.Tests;

[Collection("Winland.Plugins")]
public class PluginLocationPolicyTests
{
    [Fact]
    public void NonAdminWriteOnTheDirectoryRefusesTheLoad()
    {
        var denial = PluginLocationPolicy.Deny(
            @"C:\ProgramData\Winland\plugins",
            [
                new PrincipalAccess(PluginLocationPolicy.SystemSid, CanWrite: true, AppliesToTarget: true),
                new PrincipalAccess(PluginLocationPolicy.AdministratorsSid, CanWrite: true, AppliesToTarget: true),
                new PrincipalAccess("S-1-5-32-545", CanWrite: true, AppliesToTarget: true),
            ],
            @"C:\ProgramData\Winland\plugins\approved.json",
            approvals: null,
            currentUserSid: "S-1-5-21-1",
            currentUserIsAdmin: false);

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
            [new PrincipalAccess(PluginLocationPolicy.AdministratorsSid, CanWrite: true, AppliesToTarget: true)],
            @"C:\ProgramData\Winland\plugins\approved.json",
            [new PrincipalAccess("S-1-5-32-545", CanWrite: true, AppliesToTarget: true)],
            currentUserSid: null,
            currentUserIsAdmin: false);

        Assert.NotNull(denial);
        Assert.Contains("approved.json", denial, StringComparison.Ordinal);
    }

    [Fact]
    public void AdministratorAndSystemWriteIsAllowed()
    {
        var denial = PluginLocationPolicy.Deny(
            @"C:\ProgramData\Winland\plugins",
            [
                new PrincipalAccess(PluginLocationPolicy.SystemSid, CanWrite: true, AppliesToTarget: true),
                new PrincipalAccess(PluginLocationPolicy.AdministratorsSid, CanWrite: true, AppliesToTarget: true),
                new PrincipalAccess("S-1-5-21-9", CanWrite: true, AppliesToTarget: true),
                new PrincipalAccess("S-1-3-0", CanWrite: true, AppliesToTarget: false),
                new PrincipalAccess("S-1-5-32-545", CanWrite: false, AppliesToTarget: true),
            ],
            @"C:\ProgramData\Winland\plugins\approved.json",
            approvals: null,
            currentUserSid: "S-1-5-21-9",
            currentUserIsAdmin: true);

        Assert.Null(denial);
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

        Assert.Equal(write, WindowsPluginLocationGuard.CanWrite((FileSystemRights)rights));
    }
}
