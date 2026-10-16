using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Winland.Plugins;

namespace Winland.Tests;

[SupportedOSPlatform("windows")]
public class WindowsFileHandlesTests
{
    // Administrators can be made the owner only elevated, so this sets the current user as the owner.
    [Fact]
    public void SecurityWrittenThroughAHandleReplacesTheInheritedList()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), "winland-handle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User!;
            var security = new DirectorySecurity();
            security.SetOwner(user);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));

            using (var handle = WindowsFileHandles.Open(directory, WindowsFileHandles.ReadControl | WindowsFileHandles.WriteDac | WindowsFileHandles.WriteOwner, FileShare.Read, followReparse: false))
            {
                WindowsFileHandles.WriteSecurity(handle, security.GetSecurityDescriptorBinaryForm());
                Assert.True(WindowsAccessReader.TryRead(handle, out var access));
                Assert.Equal(user.Value, access.Owner);
                Assert.Equal(user.Value, Assert.Single(access.Grants).Sid);
            }

            var written = new DirectoryInfo(directory).GetAccessControl();
            Assert.True(written.AreAccessRulesProtected);
            var rule = Assert.Single(written.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>());
            Assert.False(rule.IsInherited);
            Assert.Equal(user.Value, rule.IdentityReference.Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AJunctionOpenedWithoutFollowingShowsItselfAndItsOwnPath()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), "winland-handle-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(target);
        try
        {
            Junctions.Create(link, target);

            using (var itself = WindowsFileHandles.Open(link, WindowsFileHandles.ReadAttributes, FileShare.ReadWrite, followReparse: false))
            using (var followed = WindowsFileHandles.Open(link, WindowsFileHandles.ReadAttributes, FileShare.ReadWrite, followReparse: true))
            {
                Assert.True(WindowsFileHandles.Attributes(itself).HasFlag(FileAttributes.ReparsePoint));
                Assert.False(WindowsFileHandles.Attributes(followed).HasFlag(FileAttributes.ReparsePoint));
                Assert.Equal(
                    Path.GetFileName(target),
                    Path.GetFileName(WindowsFileHandles.FinalPath(followed)));
            }
        }
        finally
        {
            Junctions.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }
}
