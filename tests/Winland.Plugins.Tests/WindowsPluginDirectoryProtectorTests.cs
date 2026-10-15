using System.Security.AccessControl;
using System.Security.Principal;
using Winland.Plugins;
using Winland.Plugins.Manager;

namespace Winland.Tests;

public class WindowsPluginDirectoryProtectorTests
{
    [Fact]
    public void ProtectedDirectoryPassesTheLocationGuard()
    {
        if (!OperatingSystem.IsWindows())
            return;

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
            // The owner may still rewrite the access list, which lets a non-elevated test clean up.
            var open = new DirectorySecurity();
            using var identity = WindowsIdentity.GetCurrent();
            open.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(open);
            Directory.Delete(directory, recursive: true);
        }
    }
}
