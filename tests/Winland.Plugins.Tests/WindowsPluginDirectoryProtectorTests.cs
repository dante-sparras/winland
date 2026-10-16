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

    // Making Administrators the owner needs an elevated token, so this runs only elevated.
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

        var directory = Path.Combine(Path.GetTempPath(), "winland-protect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            new WindowsPluginDirectoryProtector().Protect(directory);

            var denial = new WindowsPluginLocationGuard().Deny(directory, Path.Combine(directory, "approved.json"));
            Assert.True(denial is null, denial);
            var rules = new DirectoryInfo(directory).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in rules)
                Assert.False(rule.IsInherited);
        }
        finally
        {
            var open = new DirectorySecurity();
            using var identity = WindowsIdentity.GetCurrent();
            open.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(open);
            Directory.Delete(directory, recursive: true);
        }
    }
}
