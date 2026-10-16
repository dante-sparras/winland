using System.Security.AccessControl;
using System.Security.Principal;
using Winland;

namespace Winland.Daemon.Tests;

public sealed class FileConfigSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winland-source-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _locked = [];

    public FileConfigSourceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void APatternInTheConfigFolderDoesNotLookIntoSubfolders()
    {
        Write("a.conf");
        Write(@"sub\b.conf");
        Lock("locked");

        var matches = new FileConfigSource(_root).Match("*.conf");

        Assert.Equal([Path.Combine(_root, "a.conf")], matches);
    }

    [Fact]
    public void AnUnreadableFolderDoesNotHideTheOtherMatches()
    {
        Write(@"one\x.conf");
        Write(@"two\y.conf");
        Write(@"two\deep\z.conf");
        Lock("locked");

        var matches = new FileConfigSource(_root).Match(@"*\*.conf");

        Assert.Equal([Path.Combine(_root, "one", "x.conf"), Path.Combine(_root, "two", "y.conf")], matches);
    }

    [Theory]
    [InlineData(@"C:\*.conf", @"C:\", 0)]
    [InlineData(@"C:\conf\*.conf", @"C:\conf", 0)]
    [InlineData(@"C:\conf\*\*.conf", @"C:\conf", 1)]
    [InlineData(@"C:\conf\**\x\*.conf", @"C:\conf", 2)]
    [InlineData(@"C:\conf\a?\*.conf", @"C:\conf", 1)]
    [InlineData(@"C:/conf/*/*.conf", @"C:\conf", 1)]
    public void TheSearchStartsAtTheLastFolderWithoutAWildcard(string pattern, string root, int depth)
    {
        var plan = FileConfigSource.Plan(pattern);

        Assert.Equal((root, depth), plan);
    }

    public void Dispose()
    {
        foreach (var folder in _locked)
            Unlock(folder);

        Directory.Delete(_root, recursive: true);
    }

    private void Write(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    // A folder the user cannot list, like one another account owns.
    private void Lock(string relative)
    {
        var folder = Path.Combine(_root, relative);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "hidden.conf"), "");
        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(Me(), FileSystemRights.ListDirectory, AccessControlType.Deny));
        info.SetAccessControl(security);
        _locked.Add(folder);
    }

    private static void Unlock(string folder)
    {
        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        security.RemoveAccessRule(new FileSystemAccessRule(Me(), FileSystemRights.ListDirectory, AccessControlType.Deny));
        info.SetAccessControl(security);
    }

    private static SecurityIdentifier Me()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!;
    }
}
