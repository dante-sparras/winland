using System.Security.AccessControl;
using System.Security.Principal;
using Winland.Plugins;

namespace Winland.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class AdministratorFolderTests
{
    // Elevated, the new folder is owned by Administrators and passes. Not elevated, it is owned
    // by the user, which the check refuses rather than handing back an unprotected folder.
    [Fact]
    public void NewFolderIsProtectedOrRefused()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var parent = Path.Combine(Path.GetTempPath(), "winland-admin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            bool elevated;
            using (var identity = WindowsIdentity.GetCurrent())
                elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);

            if (elevated)
            {
                var folder = AdministratorFolder.CreateNew(parent, "task-");

                Assert.StartsWith(Path.Combine(parent, "task-"), folder, StringComparison.OrdinalIgnoreCase);
                Assert.Null(WindowsAccessReader.FirstNonAdministrator(folder));
            }
            else
            {
                var refused = Assert.Throws<InvalidOperationException>(() => AdministratorFolder.CreateNew(parent, "task-"));

                Assert.Contains("non-administrator", refused.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            foreach (var folder in Directory.GetDirectories(parent))
                Open(folder);

            Directory.Delete(parent, recursive: true);
        }
    }

    // The owner may still rewrite the access list, which lets a non-elevated test clean up.
    private static void Open(string folder)
    {
        var open = new DirectorySecurity();
        using var identity = WindowsIdentity.GetCurrent();
        open.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
        new DirectoryInfo(folder).SetAccessControl(open);
    }
}
