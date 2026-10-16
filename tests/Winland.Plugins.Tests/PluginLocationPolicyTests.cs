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
            approvals: null,
            ancestors: []);

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
            Owned(new PrincipalAccess("S-1-5-32-545", CanWrite: true, AppliesToTarget: true)),
            ancestors: []);

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
            approvals: null,
            ancestors: []);

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
            approvals: null,
            ancestors: []);

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

        var onDirectory = PluginLocationPolicy.Deny(@"C:\p", directory, @"C:\p\approved.json", null, []);
        var onApprovals = PluginLocationPolicy.Deny(@"C:\p", Owned(), @"C:\p\approved.json", approvals, []);
        var unknown = PluginLocationPolicy.Deny(@"C:\p", new AccessList(null, []), @"C:\p\approved.json", null, []);

        Assert.NotNull(onDirectory);
        Assert.Contains(CurrentUser, onDirectory, StringComparison.Ordinal);
        Assert.NotNull(onApprovals);
        Assert.Contains("approved.json", onApprovals, StringComparison.Ordinal);
        Assert.NotNull(unknown);
    }

    [Fact]
    public void ATreeIsProtectedOnlyWhenEveryEntryIs()
    {
        var protectedEntry = Owned(new PrincipalAccess(AdministratorOnly.AdministratorsSid, CanWrite: true, AppliesToTarget: true));
        var userFile = new AccessList(CurrentUser, []);

        var clean = AdministratorOnly.FirstNonAdministrator([(@"C:\w", protectedEntry), (@"C:\w\winland.exe", protectedEntry)]);
        var dirty = AdministratorOnly.FirstNonAdministrator([(@"C:\w", protectedEntry), (@"C:\w\winland.dll", userFile), (@"C:\w\x.dll", userFile)]);
        var unreadable = AdministratorOnly.FirstNonAdministrator([(@"C:\w", protectedEntry), (@"C:\w\locked.dll", null)]);

        Assert.Null(clean);
        Assert.Equal((@"C:\w\winland.dll", "owner " + CurrentUser), dirty);
        Assert.NotNull(unreadable);
        Assert.Equal(@"C:\w\locked.dll", unreadable.Value.Path);
    }

    [Fact]
    public void AFolderTheUserCreatedIsNotProtected()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), "winland-tree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "runtimes"));
        File.WriteAllText(Path.Combine(directory, "runtimes", "a.dll"), "");
        try
        {
            var found = WindowsAccessReader.FirstNonAdministrator(directory);

            Assert.NotNull(found);
            Assert.Equal(directory, found.Value.Path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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

    // Any program the user runs can set the user's environment, so the elevated host follows a
    // variable only to a location the guard passes.
    [Fact]
    public void VariablesAreIgnoredWhenTheGuardDeniesWhereTheyPoint()
    {
        var guard = new Guard("A non-administrator can write it.");

        var location = PluginPaths.Choose(@"E:\mine", @"E:\mine\list.json", @"C:\ProgramData", guard);

        Assert.Equal(Path.Combine(@"C:\ProgramData", "Winland", "plugins"), location.Directory);
        Assert.Equal(Path.Combine(@"C:\ProgramData", "Winland", "plugins", "approved.json"), location.Approvals);
        Assert.NotNull(location.Ignored);
        Assert.Contains(PluginPaths.DirectoryVariable, location.Ignored, StringComparison.Ordinal);
        Assert.Contains("A non-administrator can write it.", location.Ignored, StringComparison.Ordinal);
        Assert.Equal([(@"E:\mine", @"E:\mine\list.json")], guard.Asked);
    }

    [Fact]
    public void VariablesAreFollowedWhenTheGuardPassesWhereTheyPoint()
    {
        var guard = new Guard(null);

        var directory = PluginPaths.Choose(@"E:\mine", null, @"C:\ProgramData", guard);
        var approvals = PluginPaths.Choose(null, @"E:\list.json", @"C:\ProgramData", guard);

        Assert.Equal((@"E:\mine", Path.Combine(@"E:\mine", "approved.json"), (string?)null), (directory.Directory, directory.Approvals, directory.Ignored));
        Assert.Equal(Path.Combine(@"C:\ProgramData", "Winland", "plugins"), approvals.Directory);
        Assert.Equal(@"E:\list.json", approvals.Approvals);
        Assert.Null(approvals.Ignored);
    }

    [Fact]
    public void WithoutVariablesTheDefaultIsUsedWithoutAsking()
    {
        var guard = new Guard("unused");

        var location = PluginPaths.Choose(" ", null, @"C:\ProgramData", guard);

        Assert.Equal(Path.Combine(@"C:\ProgramData", "Winland", "plugins"), location.Directory);
        Assert.Null(location.Ignored);
        Assert.Empty(guard.Asked);
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

    // Delete, delete child, change permissions, take ownership and generic all remove or rename what a
    // folder holds. Creating files and folders, writing and generic write do not.
    [Theory]
    [InlineData(0x10000, true)]
    [InlineData(0x40, true)]
    [InlineData(0x40000, true)]
    [InlineData(0x80000, true)]
    [InlineData(0x10000000, true)]
    [InlineData(0x1F01FF, true)]
    [InlineData(0x1301BF, true)]
    [InlineData(0x2, false)]
    [InlineData(0x4, false)]
    [InlineData(0x116, false)]
    [InlineData(0x40000000, false)]
    [InlineData(0x1200A9, false)]
    public void OnlyRightsThatRemoveEntriesCountAsRemove(int rights, bool remove)
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.Equal(remove, WindowsAccessReader.CanRemove((FileSystemRights)rights));
    }

    // Whoever owns ProgramData\Winland, or may delete what it holds, can rename the protected plugins
    // folder away and put one of their own, with its own approvals, in its place.
    [Fact]
    public void AFolderAboveThatANonAdministratorCanEmptyRefusesTheLoad()
    {
        var owned = PluginLocationPolicy.Deny(
            @"C:\ProgramData\Winland\plugins",
            Owned(new PrincipalAccess(AdministratorOnly.AdministratorsSid, CanWrite: true, AppliesToTarget: true, CanRemove: true)),
            @"C:\ProgramData\Winland\plugins\approved.json",
            approvals: null,
            ancestors: [(@"C:\ProgramData\Winland", new AccessList(CurrentUser, [])), (@"C:\ProgramData", ProgramData())]);
        var emptied = PluginLocationPolicy.Deny(
            @"C:\ProgramData\Winland\plugins",
            Owned(),
            @"C:\ProgramData\Winland\plugins\approved.json",
            approvals: null,
            ancestors: [(@"C:\ProgramData\Winland", Owned(new PrincipalAccess("S-1-5-32-545", CanWrite: true, AppliesToTarget: true, CanRemove: true)))]);
        var unreadable = PluginLocationPolicy.Deny(
            @"C:\ProgramData\Winland\plugins",
            Owned(),
            @"C:\ProgramData\Winland\plugins\approved.json",
            approvals: null,
            ancestors: [(@"C:\ProgramData\Winland", null)]);

        Assert.NotNull(owned);
        Assert.Contains(@"'C:\ProgramData\Winland'", owned, StringComparison.Ordinal);
        Assert.Contains("owner " + CurrentUser, owned, StringComparison.Ordinal);
        Assert.Contains("rename or replace", owned, StringComparison.Ordinal);
        Assert.NotNull(emptied);
        Assert.Contains("S-1-5-32-545", emptied, StringComparison.Ordinal);
        Assert.NotNull(unreadable);
    }

    // C:\ and ProgramData let users create entries. A new name cannot take the place of one that is there.
    [Fact]
    public void FoldersAboveThatOnlyLetUsersAddEntriesPass()
    {
        var root = new AccessList(
            AdministratorOnly.TrustedInstallerSid,
            [
                new PrincipalAccess("S-1-5-11", CanWrite: true, AppliesToTarget: true),
                new PrincipalAccess("S-1-5-11", CanWrite: true, AppliesToTarget: false, CanRemove: true),
                new PrincipalAccess(AdministratorOnly.AdministratorsSid, CanWrite: true, AppliesToTarget: true, CanRemove: true),
            ]);

        var denial = PluginLocationPolicy.Deny(
            @"C:\ProgramData\Winland\plugins",
            Owned(),
            @"C:\ProgramData\Winland\plugins\approved.json",
            approvals: null,
            ancestors: [(@"C:\ProgramData\Winland", Owned()), (@"C:\ProgramData", ProgramData()), (@"C:\", root)]);

        Assert.Null(denial);
    }

    [Fact]
    public void AnApprovalsFileANonAdministratorCanWriteIsRefusedWhereverItWasOpened()
    {
        Assert.Null(PluginLocationPolicy.DenyApprovals(@"C:\p\approved.json", null));
        Assert.Null(PluginLocationPolicy.DenyApprovals(@"C:\p\approved.json", Owned()));

        var denial = PluginLocationPolicy.DenyApprovals(@"C:\p\approved.json", new AccessList(CurrentUser, []));

        Assert.NotNull(denial);
        Assert.Contains("approved.json", denial, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFoldersAboveSystem32AreProtectedAndTheOnesAboveTempAreNot()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), "winland-above-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Null(WindowsAccessReader.FirstNonAdministratorAbove(Path.Combine(Environment.SystemDirectory, "drivers")));
            Assert.NotNull(WindowsAccessReader.FirstNonAdministratorAbove(directory));
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    // The access lists are read through paths, so a junction would lead the check to another folder.
    [Fact]
    public void AJunctionedPluginDirectoryIsRefused()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), "winland-junction-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "plugins");
        Directory.CreateDirectory(target);
        try
        {
            Junctions.Create(link, target);

            var denial = new WindowsPluginLocationGuard().Deny(link, Path.Combine(link, "approved.json"));

            Assert.NotNull(denial);
            Assert.Contains("reparse point", denial, StringComparison.Ordinal);
        }
        finally
        {
            Junctions.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }

    private static AccessList Owned(params PrincipalAccess[] grants) =>
        new(AdministratorOnly.AdministratorsSid, grants);

    // ProgramData as Windows sets it up: users may create entries, and own what they create.
    private static AccessList ProgramData() =>
        new(
            AdministratorOnly.SystemSid,
            [
                new PrincipalAccess("S-1-3-0", CanWrite: true, AppliesToTarget: false, CanRemove: true),
                new PrincipalAccess(AdministratorOnly.SystemSid, CanWrite: true, AppliesToTarget: true, CanRemove: true),
                new PrincipalAccess(AdministratorOnly.AdministratorsSid, CanWrite: true, AppliesToTarget: true, CanRemove: true),
                new PrincipalAccess("S-1-5-32-545", CanWrite: false, AppliesToTarget: true),
                new PrincipalAccess("S-1-5-32-545", CanWrite: true, AppliesToTarget: true),
            ]);

    private sealed class Guard(string? denial) : IPluginLocationGuard
    {
        public List<(string Directory, string Approvals)> Asked { get; } = [];

        public string? Deny(string pluginDirectory, string approvalsFile)
        {
            Asked.Add((pluginDirectory, approvalsFile));
            return denial;
        }

        public string? DenyApprovals(FileStream approvals) => denial;
    }
}
