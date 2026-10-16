using System.Security.AccessControl;
using System.Security.Principal;
using Winland.Plugins;
using Winland.Plugins.Manager;

namespace Winland.Tests;

public class WindowsPluginDirectoryProtectorTests
{
    [Fact]
    public void ProtectionIsOwnedByAdministratorsAndPassesThePolicy()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var security = WindowsPluginDirectoryProtector.Protection();
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
        var grants = new List<PrincipalAccess>();
        foreach (FileSystemAccessRule rule in rules)
            grants.Add(new PrincipalAccess(rule.IdentityReference.Value, WindowsAccessReader.CanWrite(rule.FileSystemRights), AppliesToTarget: true));

        var owner = security.GetOwner(typeof(SecurityIdentifier))?.Value;

        Assert.Equal(AdministratorOnly.AdministratorsSid, owner);
        Assert.True(security.AreAccessRulesProtected);
        Assert.Null(AdministratorOnly.NonAdministrator(new AccessList(owner, grants)));
    }

    // Making Administrators the owner needs an elevated token, so this runs only elevated. The folders
    // above a temporary folder belong to the user, so the guard would refuse one there; this one is
    // made in ProgramData, as the plugin directory is.
    [Fact]
    public void ProtectedDirectoryPassesTheLocationGuard()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using (var current = WindowsIdentity.GetCurrent())
        {
            if (!new WindowsPrincipal(current).IsInRole(WindowsBuiltInRole.Administrator))
                return;
        }

        var parent = AdministratorFolder.CreateNew(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "winland-protect-");
        var directory = Path.Combine(parent, "plugins");
        Directory.CreateDirectory(directory);
        try
        {
            new WindowsPluginDirectoryProtector().Protect(parent);
            new WindowsPluginDirectoryProtector().Protect(directory);

            var denial = new WindowsPluginLocationGuard().Deny(directory, Path.Combine(directory, "approved.json"));
            Assert.True(denial is null, denial);
            var rules = new DirectoryInfo(directory).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in rules)
                Assert.False(rule.IsInherited);

            Assert.Equal(AdministratorOnly.AdministratorsSid, new DirectoryInfo(directory).GetAccessControl().GetOwner(typeof(SecurityIdentifier))?.Value);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    // A junction swapped in after a check by path would otherwise have its target's access list rewritten.
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void AJunctionIsRefusedAndItsTargetKeepsItsAccessList()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), "winland-protect-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "plugins");
        Directory.CreateDirectory(target);
        try
        {
            Junctions.Create(link, target);
            var before = new DirectoryInfo(target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access);

            var refused = Assert.Throws<InvalidOperationException>(() => new WindowsPluginDirectoryProtector().Protect(link));

            Assert.Contains("reparse point", refused.Message, StringComparison.Ordinal);
            var after = new DirectoryInfo(target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access);
            Assert.Equal(before, after);
        }
        finally
        {
            Junctions.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }
}
