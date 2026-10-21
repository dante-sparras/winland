using System.Diagnostics;

namespace Winland.Daemon.Tests;

public class InstallFilesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "winland-install-" + Guid.NewGuid().ToString("N"));
    private readonly WindowsInstallFiles files = new();

    public InstallFilesTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
            files.DeleteTree(root);
    }

    [Fact]
    public void ALinkIsListedAndNotFollowed()
    {
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "target");
        var linked = Path.Combine(source, "linked");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "winland.exe"), "daemon");
        File.WriteAllText(Path.Combine(target, "secret.txt"), "secret");
        Junction(linked, target);

        var listed = files.List(source);

        Assert.Contains(listed, file => file.RelativePath == "winland.exe" && !file.ReparsePoint);
        Assert.Contains(listed, file => file.RelativePath == "linked" && file.ReparsePoint);
        Assert.DoesNotContain(listed, file => file.RelativePath.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void CopyRefusesALink()
    {
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "winland.exe"), "daemon");
        Junction(source, target);

        var error = Assert.Throws<InvalidOperationException>(() => files.Copy(source, Path.Combine(root, "copied.exe")));

        Assert.Contains("it is a link", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "copied.exe")));
    }

    [Fact]
    public void DeleteRemovesALinkAndLeavesItsTarget()
    {
        var tree = Path.Combine(root, "tree");
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(tree);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(tree, "winland.exe"), "daemon");
        File.WriteAllText(Path.Combine(target, "secret.txt"), "secret");
        Junction(Path.Combine(tree, "linked"), target);

        files.DeleteTree(tree);

        Assert.False(Directory.Exists(tree));
        Assert.Equal("secret", File.ReadAllText(Path.Combine(target, "secret.txt")));
    }

    [Fact]
    public void CopyWritesTheFileItself()
    {
        var source = Path.Combine(root, "winland.exe");
        var destination = Path.Combine(root, "out", "winland.exe");
        File.WriteAllText(source, "daemon");

        files.Copy(source, destination);

        Assert.Equal("daemon", File.ReadAllText(destination));
    }

    [Fact]
    public void CopyReplacesAnExistingFile()
    {
        var source = Path.Combine(root, "new.exe");
        var destination = Path.Combine(root, "old.exe");
        File.WriteAllText(source, "new");
        File.WriteAllText(destination, "old");

        files.Copy(source, destination);

        Assert.Equal("new", File.ReadAllText(destination));
        Assert.False(File.Exists(destination + ".winland-old"));
    }

    [Fact]
    public void CopyRefusesALockedFileOutsideTheRunningFolder()
    {
        var source = Path.Combine(root, "new.exe");
        var destination = Path.Combine(root, "locked.exe");
        File.WriteAllText(source, "new");
        File.WriteAllText(destination, "old");
        using var locked = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.None);

        var error = Record.Exception(() => files.Copy(source, destination));

        Assert.True(error is IOException or UnauthorizedAccessException);
        locked.Dispose();
        Assert.Equal("old", File.ReadAllText(destination));
        Assert.False(File.Exists(destination + ".winland-old"));
    }

    private static void Junction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
