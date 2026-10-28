using System.Security.AccessControl;
using System.Security.Principal;

namespace Winland.Daemon.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class AdministratorFolderTests
{
    // Elevated, the new folder is owned by Administrators and passes.
    [ElevatedFact(true)]
    public void AnElevatedRunGetsAProtectedFolder()
    {
        InParent(parent =>
        {
            var folder = AdministratorFolder.CreateNew(parent, "task-");

            Assert.StartsWith(Path.Combine(parent, "task-"), folder, StringComparison.OrdinalIgnoreCase);
            Assert.Null(WindowsAccessReader.FirstNonAdministrator(folder));
        });
    }

    // Not elevated, the folder is owned by the user, which the check refuses rather than handing back an unprotected one.
    [ElevatedFact(false)]
    public void AnUnelevatedRunIsRefused()
    {
        InParent(parent =>
        {
            var refused = Assert.Throws<InvalidOperationException>(() => AdministratorFolder.CreateNew(parent, "task-"));

            Assert.Contains("non-administrator", refused.Message, StringComparison.Ordinal);
        });
    }

    private static void InParent(Action<string> test)
    {
        var parent = Path.Combine(Path.GetTempPath(), "winland-admin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            test(parent);
        }
        finally
        {
            foreach (var folder in Directory.GetDirectories(parent))
                Open(folder);

            Directory.Delete(parent, recursive: true);
        }
    }

    // Runs only when the test process's elevation is the one the test needs, and says so when it skips.
    private sealed class ElevatedFactAttribute : FactAttribute
    {
        public ElevatedFactAttribute(bool elevated)
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) != elevated)
                Skip = elevated ? "Needs an elevated test run." : "Needs a test run without elevation.";
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
