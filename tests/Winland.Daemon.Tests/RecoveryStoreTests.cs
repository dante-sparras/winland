using Winland;

namespace Winland.Daemon.Tests;

public sealed class RecoveryStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winland-state-" + Guid.NewGuid().ToString("N"));

    public RecoveryStoreTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ASavedSnapshotIsReadBackAndDeleted()
    {
        var path = Path.Combine(_root, "Winland", "state.json");
        var store = new RecoveryStore(path, _ => null);
        store.Arm();

        store.Save(Snapshot(7));
        var read = store.Read();
        store.Delete();

        Assert.NotNull(read);
        Assert.Equal(7, Assert.Single(read.Clients).Window.Value);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void ASecondSaveReplacesTheFirst()
    {
        var path = Path.Combine(_root, "state.json");
        var store = new RecoveryStore(path);
        store.Arm();

        store.Save(Snapshot(1));
        store.Save(Snapshot(2));

        Assert.Equal(2, Assert.Single(new RecoveryStore(path).Read()!.Clients).Window.Value);
        Assert.Equal(["state.json"], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public void AFolderTheCheckRefusesIsNeitherWrittenNorRead()
    {
        var path = Path.Combine(_root, "state.json");
        File.WriteAllText(path, Snapshot(3).ToJson());
        var store = new RecoveryStore(path, _ => "only administrators can empty it");
        store.Arm();

        store.Save(Snapshot(4));
        var read = store.Read();
        store.Delete();

        Assert.Null(read);
        Assert.Equal(3, Assert.Single(new RecoveryStore(path).Read()!.Clients).Window.Value);
    }

    // A junction on the way is followed, and the folder it leads to is what is checked.
    [Fact]
    public void AJunctionedFolderIsCheckedWhereItLeads()
    {
        var target = Path.Combine(_root, "target");
        var link = Path.Combine(_root, "Winland");
        Directory.CreateDirectory(target);
        Junction(link, target);
        var checkedFolders = new List<string>();
        var store = new RecoveryStore(Path.Combine(link, "state.json"), folder =>
        {
            checkedFolders.Add(WindowsFileHandles.FinalPath(folder));
            return "refused";
        });
        store.Arm();

        store.Save(Snapshot(5));

        Assert.Equal(Path.GetFileName(target), Path.GetFileName(Assert.Single(checkedFolders)));
        Assert.Empty(Directory.GetFiles(target));
    }

    // A link at the file's own name is someone else's way into another folder. It is never read
    // through, and deleting the file deletes only the link.
    [Fact]
    public void ALinkAtTheFilesNameIsNotFollowed()
    {
        var elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "kept.txt"), "kept");
        var folder = Path.Combine(_root, "Winland");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "state.json");
        Junction(path, elsewhere);
        Junction(path + ".tmp", elsewhere);
        var store = new RecoveryStore(path);

        var read = store.Read();
        store.Arm();
        store.Save(Snapshot(6));
        var saved = new RecoveryStore(path).Read();
        store.Delete();

        Assert.Null(read);
        Assert.Equal(6, Assert.Single(saved!.Clients).Window.Value);
        Assert.False(Directory.Exists(path));
        Assert.False(File.Exists(path));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(elsewhere, "kept.txt")));
    }

    // Nothing is recorded until the Clients have been adopted, and nothing after the cleanup has run.
    [Fact]
    public void OnlyAnArmedOpenStoreWrites()
    {
        var path = Path.Combine(_root, "state.json");
        var store = new RecoveryStore(path);

        store.Save(Snapshot(1));
        var beforeArm = File.Exists(path);
        store.Arm();
        store.Delete();
        store.Arm();
        store.Save(Snapshot(2));

        Assert.False(beforeArm);
        Assert.False(File.Exists(path));
    }

    // The same snapshot is not written again, so a file someone else changed meanwhile is left alone.
    [Fact]
    public void AnUnchangedSnapshotIsNotWrittenAgain()
    {
        var path = Path.Combine(_root, "state.json");
        var store = new RecoveryStore(path);
        store.Arm();

        store.Save(Snapshot(1));
        File.WriteAllText(path, Snapshot(9).ToJson());
        store.Save(Snapshot(1));

        Assert.Equal(9, Assert.Single(store.Read()!.Clients).Window.Value);
    }

    [Fact]
    public void AMissingOrDamagedFileReadsAsNothing()
    {
        var path = Path.Combine(_root, "state.json");
        var store = new RecoveryStore(path);

        var missing = store.Read();
        File.WriteAllText(path, "{ not json");
        var damaged = store.Read();

        Assert.Null(missing);
        Assert.Null(damaged);
    }

    // A file where the folder should be is never used, and saving there does not throw.
    [Fact]
    public void AFileInPlaceOfTheFolderIsNotUsed()
    {
        var blocker = Path.Combine(_root, "Winland");
        File.WriteAllText(blocker, "not a folder");
        var store = new RecoveryStore(Path.Combine(blocker, "state.json"));
        store.Arm();

        store.Save(Snapshot(1));
        var read = store.Read();
        store.Delete();

        Assert.Null(read);
        Assert.Equal("not a folder", File.ReadAllText(blocker));
    }

    [Fact]
    public void APathWithoutAFolderIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new RecoveryStore(" "));
        Assert.Throws<ArgumentException>(() => new RecoveryStore(@"C:\"));
    }

    public void Dispose()
    {
        foreach (var directory in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories).Reverse())
        {
            if (new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint))
                Directory.Delete(directory);
        }

        Directory.Delete(_root, recursive: true);
    }

    private static RecoverySnapshot Snapshot(long window) =>
        new([new RecoveredClient(new WindowId(window), WorkspaceId.Initial, MonitorId.Primary, false, "app", "App", 1)]);

    private static void Junction(string link, string target)
    {
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target })
            start.ArgumentList.Add(argument);

        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
