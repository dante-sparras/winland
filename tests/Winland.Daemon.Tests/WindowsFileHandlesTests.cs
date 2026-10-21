using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Winland;

namespace Winland.Daemon.Tests;

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

    // The final path is the plain path, without the \\?\ prefix the system reports it with.
    [Fact]
    public void TheFinalPathOfAFileIsItsPlainPath()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "winland-final-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "");
        try
        {
            using var handle = WindowsFileHandles.Open(path, WindowsFileHandles.ReadAttributes, FileShare.ReadWrite, followReparse: false);

            Assert.Equal(path, WindowsFileHandles.FinalPath(handle), ignoreCase: true);
            Assert.False(WindowsFileHandles.Attributes(handle).HasFlag(FileAttributes.Directory));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A new file is opened for reading, writing and deletion, and an existing one is not replaced.
    [Fact]
    public void ANewFileCanBeWrittenReadAndDeletedButNotMadeTwice()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(Path.GetTempPath(), "winland-new-" + Guid.NewGuid().ToString("N") + ".txt");
        byte[] read;
        using (var handle = WindowsFileHandles.CreateNewFile(path, FileShare.None))
        {
            RandomAccess.Write(handle, "state"u8, 0);
            read = new byte[5];
            RandomAccess.Read(handle, read, 0);
            Assert.Throws<IOException>(() => WindowsFileHandles.CreateNewFile(path, FileShare.ReadWrite));
            WindowsFileHandles.MarkForDeletion(handle);
        }

        Assert.Equal("state"u8.ToArray(), read);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(@"sub\state.json")]
    [InlineData("sub/state.json")]
    [InlineData("C:state.json")]
    public void ARenameToAnythingButOneNameIsRefused(string name)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), "winland-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var folder = WindowsFileHandles.Open(directory, WindowsFileHandles.ReadAttributes, FileShare.ReadWrite | FileShare.Delete, followReparse: true);
            using var file = WindowsFileHandles.CreateNewFile(Path.Combine(directory, "state.tmp"), FileShare.None);

            Assert.Throws<ArgumentException>(() => WindowsFileHandles.Rename(file, folder, name));
            WindowsFileHandles.Rename(file, folder, "state.json");
            Assert.Equal(Path.Combine(directory, "state.json"), WindowsFileHandles.FinalPath(file), ignoreCase: true);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MissingArgumentsAreRefused()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.Throws<ArgumentException>(() => WindowsFileHandles.Open(" ", WindowsFileHandles.ReadAttributes, FileShare.Read, followReparse: false));
        Assert.Throws<ArgumentException>(() => WindowsFileHandles.CreateNewFile(" ", FileShare.Read));
        Assert.Throws<ArgumentNullException>(() => WindowsFileHandles.Attributes(null!));
        Assert.Throws<ArgumentNullException>(() => WindowsFileHandles.FinalPath(null!));
        Assert.Throws<ArgumentNullException>(() => WindowsFileHandles.ReadSecurity(null!));
        Assert.Throws<ArgumentNullException>(() => WindowsFileHandles.WriteSecurity(null!, []));
        Assert.Throws<ArgumentNullException>(() => WindowsFileHandles.MarkForDeletion(null!));
        Assert.Throws<ArgumentNullException>(() => WindowsFileHandles.Rename(null!, null!, "a"));
        Assert.Throws<FileNotFoundException>(() => WindowsFileHandles.Open(Path.Combine(Path.GetTempPath(), "winland-missing-" + Guid.NewGuid().ToString("N")), WindowsFileHandles.ReadAttributes, FileShare.Read, followReparse: false));
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
