using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Winland.Core;

namespace Winland.Daemon.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsAccessReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winland-access-" + Guid.NewGuid().ToString("N"));
    private string? _locked;

    public WindowsAccessReaderTests() => Directory.CreateDirectory(_root);

    // Every entry under the folder is read, hidden ones and those in subfolders too, the folder first.
    [Fact]
    public void TheTreeIsTheFolderAndEverythingInIt()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "sub", "Plugin.dll"), "");
        var hidden = Path.Combine(_root, "hidden.dll");
        File.WriteAllText(hidden, "");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var tree = WindowsAccessReader.Tree(_root);

        Assert.Equal(_root, tree[0].Path);
        Assert.Equal(
            [_root, hidden, Path.Combine(_root, "sub"), Path.Combine(_root, "sub", "Plugin.dll")],
            tree.Select(entry => entry.Path).Order(StringComparer.OrdinalIgnoreCase));
        Assert.All(tree, entry => Assert.NotNull(entry.Access));
    }

    // A folder that cannot be listed cannot be shown to be protected.
    [Fact]
    public void AFolderThatCannotBeListedReadsAsUnknown()
    {
        if (!OperatingSystem.IsWindows())
            return;

        _locked = Path.Combine(_root, "locked");
        Directory.CreateDirectory(_locked);
        File.WriteAllText(Path.Combine(_locked, "Plugin.dll"), "");
        var info = new DirectoryInfo(_locked);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(Me(), FileSystemRights.ListDirectory, AccessControlType.Deny));
        info.SetAccessControl(security);

        var tree = WindowsAccessReader.Tree(_root);

        Assert.Contains(tree, entry => entry.Path == _root && entry.Access is null);
    }

    [Fact]
    public void AnOpenFileAndAFolderCanBeRead()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(_root, "approved.json");
        File.WriteAllText(path, "[]");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.True(WindowsAccessReader.TryRead(stream, out var opened));
        Assert.True(WindowsAccessReader.TryRead(_root, out var folder, out var error));
        Assert.True(WindowsAccessReader.TryRead(path, out var file, out _));
        Assert.Null(error);
        Assert.False(string.IsNullOrEmpty(opened.Owner));
        Assert.Equal(file.Owner, opened.Owner);
        Assert.False(string.IsNullOrEmpty(folder.Owner));
        Assert.NotEmpty(opened.Grants);
    }

    [Fact]
    public void APathThatIsNotThereCannotBeRead()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var missing = Path.Combine(_root, "missing", "Plugin.dll");

        Assert.False(WindowsAccessReader.TryRead(missing, out _, out var error));
        Assert.Equal($"Could not verify that '{missing}' is protected from non-administrators.", error);
    }

    // The folders above a path, nearest first, up to the root.
    [Fact]
    public void TheAncestorsAreNearestFirst()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var file = Path.Combine(_root, "sub", "Plugin.dll");

        var ancestors = WindowsAccessReader.Ancestors(file).Select(entry => entry.Path).ToList();

        Assert.Equal(Path.Combine(_root, "sub"), ancestors[0]);
        Assert.Equal(_root, ancestors[1]);
        Assert.Equal(Path.GetPathRoot(_root), ancestors[^1]);
    }

    [Fact]
    public void MissingArgumentsAreRefused()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.Throws<ArgumentException>(() => WindowsAccessReader.TryRead(" ", out _, out _));
        Assert.Throws<ArgumentNullException>(() => WindowsAccessReader.TryRead((FileStream)null!, out _));
        Assert.Throws<ArgumentNullException>(() => WindowsAccessReader.TryRead((Microsoft.Win32.SafeHandles.SafeFileHandle)null!, out _));
        Assert.Throws<ArgumentException>(() => WindowsAccessReader.FirstNonAdministrator(" "));
        Assert.Throws<ArgumentException>(() => WindowsAccessReader.Tree(" "));
        Assert.Throws<ArgumentException>(() => WindowsAccessReader.FirstNonAdministratorAbove(" "));
        Assert.Throws<ArgumentException>(() => WindowsAccessReader.Ancestors(" "));
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows() && _locked is not null)
        {
            var info = new DirectoryInfo(_locked);
            var security = info.GetAccessControl();
            security.RemoveAccessRule(new FileSystemAccessRule(Me(), FileSystemRights.ListDirectory, AccessControlType.Deny));
            info.SetAccessControl(security);
        }

        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        Directory.Delete(_root, recursive: true);
    }

    private static SecurityIdentifier Me()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!;
    }
}
