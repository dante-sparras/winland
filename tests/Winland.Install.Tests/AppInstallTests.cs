using Winland.Install;

namespace Winland.Install.Tests;

public class AppInstallTests
{
    private const string Source = @"C:\Users\me\Downloads\Winland\winland\winland.exe";

    private const string Machine = @"C:\Program Files\Winland";

    private const string User = @"C:\Users\me\AppData\Local\Programs\Winland";

    private const string CommonPrograms = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs";

    private const string UserPrograms = @"C:\Users\me\AppData\Roaming\Microsoft\Windows\Start Menu\Programs";

    private static readonly string Config = InstallLocations.Config(@"C:\Users\me");

    private static readonly string State = InstallLocations.State(@"C:\Users\me");

    private readonly MemoryFileTree files = new();
    private readonly MemoryTaskCatalog tasks = new();
    private readonly MemoryRegistry registry = new();
    private readonly MemoryStartMenu shortcuts = new();
    private readonly MemorySession session = new();
    private readonly AppInstall install;

    public AppInstallTests()
    {
        install = new AppInstall(Layout(), tasks, files, registry, shortcuts, session);
        files.Seed(Source, [1]);
        files.Seed(@"C:\Users\me\Downloads\Winland\winland\winland.dll", [2]);
        files.Seed(@"C:\Users\me\Downloads\Winland\winlandctl\winlandctl.exe", [3]);
        files.Seed(@"C:\Users\me\Downloads\Winland\winlandctl\winlandctl.dll", [4]);
        files.Seed(@"C:\Users\me\Downloads\Winland\winland\Winland.Core.dll", [5]);
        files.Seed(@"C:\Users\me\Downloads\Winland\winlandctl\Winland.Core.dll", [5]);
        files.Seed(Path.Combine(Config, "winland.conf"), [9]);
    }

    [Fact]
    public void MachineInstallCopiesBothProgramsAndPointsTheTaskAtProgramFiles()
    {
        files.Seed(Path.Combine(User, "winland.exe"), [8]);
        registry.SetString(RegistryRoot.User, InstallLocations.RunKey, InstallLocations.RunValue, "old", expandable: false);

        install.InstallMachine(Source, @"DESKTOP\dante", "0.1.0");

        Assert.Equal([1], files.Read(Path.Combine(Machine, "winland.exe")));
        Assert.Equal([2], files.Read(Path.Combine(Machine, "winland.dll")));
        Assert.Equal([3], files.Read(Path.Combine(Machine, "winlandctl.exe")));
        Assert.Equal([4], files.Read(Path.Combine(Machine, "winlandctl.dll")));
        Assert.Equal([5], files.Read(Path.Combine(Machine, "Winland.Core.dll")));
        Assert.Equal([1], files.Read(Source));

        var task = tasks.Only();
        Assert.Equal(Path.Combine(Machine, "winland.exe"), task.Executable);
        Assert.Equal(@"DESKTOP\dante", task.User);
        Assert.DoesNotContain(Source, task.Executable, StringComparison.OrdinalIgnoreCase);

        var shortcut = Assert.Single(shortcuts.Shortcuts);
        Assert.Equal(Path.Combine(CommonPrograms, "Winland.lnk"), shortcut.LinkPath);
        Assert.Equal(Path.Combine(Machine, "winland.exe"), shortcut.Target);
        Assert.Equal("", shortcut.Arguments);
        Assert.Equal(InstallLocations.AppUserModelId, shortcut.AppUserModelId);

        Assert.Equal("Winland", Text(RegistryRoot.Machine, "DisplayName"));
        Assert.Equal(
            "\"" + Path.Combine(Machine, "winland.exe") + "\" uninstall",
            Text(RegistryRoot.Machine, "UninstallString"));
        Assert.Equal(Machine, Text(RegistryRoot.Machine, "InstallLocation"));
        Assert.Equal("0.1.0", Text(RegistryRoot.Machine, "DisplayVersion"));
        Assert.Equal(1, Dword(RegistryRoot.Machine, "NoModify"));
        Assert.Equal(Machine, Text(RegistryRoot.Machine, InstallLocations.PathValue, InstallLocations.MachinePathKey));
        Assert.True(registry.Values[(RegistryRoot.Machine, InstallLocations.MachinePathKey, InstallLocations.PathValue)].Expandable);

        Assert.False(files.Exists(Path.Combine(User, "winland.exe")));
        Assert.Null(registry.GetString(RegistryRoot.User, InstallLocations.RunKey, InstallLocations.RunValue));
        Assert.Equal([9], files.Read(Path.Combine(Config, "winland.conf")));
        Assert.Equal(["stop", "start"], session.Operations);
        Assert.Equal(Path.Combine(Machine, "winland.exe"), session.Started);
        Assert.Empty(session.StartedArguments);
    }

    [Fact]
    public void MachineInstallKeepsTheRestOfTheMachinePath()
    {
        registry.SetString(
            RegistryRoot.Machine,
            InstallLocations.MachinePathKey,
            InstallLocations.PathValue,
            @"%SystemRoot%\system32;C:\Tools",
            expandable: true);

        install.InstallMachine(Source, @"DESKTOP\dante", "0.1.0");

        Assert.Equal(@"%SystemRoot%\system32;C:\Tools;" + Machine, Text(RegistryRoot.Machine, InstallLocations.PathValue, InstallLocations.MachinePathKey));
    }

    [Fact]
    public void UserInstallUsesTheRunKeyAndNeverTheTask()
    {
        install.InstallForUser(Source, "0.1.0");

        Assert.Equal([1], files.Read(Path.Combine(User, "winland.exe")));
        Assert.Equal([3], files.Read(Path.Combine(User, "winlandctl.exe")));
        Assert.False(tasks.Contains("Winland"));
        Assert.Empty(tasks.Operations);
        Assert.Equal(
            "\"" + Path.Combine(User, "winland.exe") + "\" --no-elevate",
            registry.GetString(RegistryRoot.User, InstallLocations.RunKey, InstallLocations.RunValue));

        var shortcut = Assert.Single(shortcuts.Shortcuts);
        Assert.Equal(Path.Combine(UserPrograms, "Winland.lnk"), shortcut.LinkPath);
        Assert.Equal("--no-elevate", shortcut.Arguments);
        Assert.Equal(InstallLocations.AppUserModelId, shortcut.AppUserModelId);
        Assert.Equal(
            "\"" + Path.Combine(User, "winland.exe") + "\" uninstall",
            Text(RegistryRoot.User, "UninstallString"));
        Assert.Equal(User, Text(RegistryRoot.User, InstallLocations.PathValue, InstallLocations.UserPathKey));
        Assert.Null(registry.GetString(RegistryRoot.Machine, InstallLocations.UninstallKey, "DisplayName"));
        Assert.Equal(["stop", "start"], session.Operations);
        Assert.Equal(["--no-elevate"], session.StartedArguments);
        Assert.Equal([9], files.Read(Path.Combine(Config, "winland.conf")));
    }

    [Fact]
    public void ALinkInTheCopiedFolderIsRefusedBeforeWinlandIsStopped()
    {
        files.Seed(@"C:\Users\me\Downloads\Winland\winland\swapped.exe", [1], reparsePoint: true);

        var error = Assert.Throws<InvalidOperationException>(() => install.InstallMachine(Source, @"DESKTOP\dante", "0.1.0"));

        Assert.Contains("it is a link", error.Message, StringComparison.Ordinal);
        Assert.Empty(session.Operations);
        Assert.False(tasks.Contains("Winland"));
        Assert.False(files.DirectoryExists(Machine));
    }

    [Fact]
    public void DifferentCopiesOfTheSameFileAreRefused()
    {
        files.Seed(@"C:\Users\me\Downloads\Winland\winlandctl\Winland.Core.dll", [6]);

        var error = Assert.Throws<InvalidOperationException>(() => install.InstallForUser(Source, "0.1.0"));

        Assert.Contains("Winland.Core.dll", error.Message, StringComparison.Ordinal);
        Assert.Contains("differ", error.Message, StringComparison.Ordinal);
        Assert.Empty(session.Operations);
    }

    [Fact]
    public void MissingWinlandctlIsRefused()
    {
        var tree = new MemoryFileTree();
        tree.Seed(Source, [1]);
        var session = new MemorySession();
        var install = new AppInstall(Layout(), new MemoryTaskCatalog(), tree, new MemoryRegistry(), new MemoryStartMenu(), session);

        var error = Assert.Throws<InvalidOperationException>(() => install.InstallMachine(Source, @"DESKTOP\dante", "0.1.0"));

        Assert.Contains("winlandctl", error.Message, StringComparison.Ordinal);
        Assert.Empty(session.Operations);
    }

    [Fact]
    public void AFolderThatAlreadyHoldsBothExecutablesIsCopiedOnce()
    {
        var tree = new MemoryFileTree();
        var flat = @"C:\Users\me\Downloads\flat";
        tree.Seed(Path.Combine(flat, "winland.exe"), [1]);
        tree.Seed(Path.Combine(flat, "winlandctl.exe"), [3]);
        var install = new AppInstall(Layout(), tasks, tree, registry, shortcuts, session);

        install.InstallForUser(Path.Combine(flat, "winland.exe"), "0.1.0");

        Assert.Equal([1], tree.Read(Path.Combine(User, "winland.exe")));
        Assert.Equal([3], tree.Read(Path.Combine(User, "winlandctl.exe")));
    }

    [Fact]
    public void ReplaceInstalledCopiesIntoTheRunningFolderAndKeepsTheConfig()
    {
        files.Seed(Path.Combine(Machine, "winland.exe"), [8]);
        files.Seed(Path.Combine(User, "winland.exe"), [7]);
        files.Seed(Path.Combine(User, "winlandctl.exe"), [6]);
        tasks.Seed(LogonTaskDefinition.Build(Path.Combine(Machine, "winland.exe"), @"DESKTOP\dante"));
        registry.SetString(RegistryRoot.Machine, InstallLocations.MachinePathKey, InstallLocations.PathValue, Machine, expandable: true);
        registry.SetString(RegistryRoot.Machine, InstallLocations.UninstallKey, "DisplayVersion", "0.1.0", expandable: false);

        install.ReplaceInstalled(Source, Machine, perUser: false, "0.2.0");

        Assert.Equal([1], files.Read(Path.Combine(Machine, "winland.exe")));
        Assert.Equal([2], files.Read(Path.Combine(Machine, "winland.dll")));
        Assert.Equal([3], files.Read(Path.Combine(Machine, "winlandctl.exe")));
        Assert.Equal([7], files.Read(Path.Combine(User, "winland.exe")));
        Assert.Equal([9], files.Read(Path.Combine(Config, "winland.conf")));
        Assert.Equal(["stop", "start"], session.Operations);
        Assert.Equal(Path.Combine(Machine, "winland.exe"), session.Started);
        Assert.Empty(session.StartedArguments);
        Assert.Equal("0.2.0", Text(RegistryRoot.Machine, "DisplayVersion"));
        Assert.Empty(tasks.Operations);
        Assert.Equal(Path.Combine(Machine, "winland.exe"), tasks.Only().Executable);
        Assert.Equal(Machine, Text(RegistryRoot.Machine, InstallLocations.PathValue, InstallLocations.MachinePathKey));
    }

    [Fact]
    public void ReplaceInstalledForTheUserStartsWithoutElevation()
    {
        install.ReplaceInstalled(Source, User, perUser: true, "0.2.0");

        Assert.Equal([1], files.Read(Path.Combine(User, "winland.exe")));
        Assert.Equal([3], files.Read(Path.Combine(User, "winlandctl.exe")));
        Assert.Equal(["--no-elevate"], session.StartedArguments);
        Assert.Equal("0.2.0", Text(RegistryRoot.User, "DisplayVersion"));
        Assert.Null(registry.GetString(RegistryRoot.Machine, InstallLocations.UninstallKey, "DisplayVersion"));
        Assert.Equal([9], files.Read(Path.Combine(Config, "winland.conf")));
        Assert.Empty(tasks.Operations);
    }

    [Fact]
    public void ReplaceInstalledRefusesAMissingControlBeforeStopping()
    {
        var tree = new MemoryFileTree();
        tree.Seed(Source, [1]);
        var waited = new MemorySession();
        var replacing = new AppInstall(Layout(), new MemoryTaskCatalog(), tree, new MemoryRegistry(), new MemoryStartMenu(), waited);

        var error = Assert.Throws<InvalidOperationException>(() => replacing.ReplaceInstalled(Source, Machine, perUser: false, "0.2.0"));

        Assert.Contains("winlandctl", error.Message, StringComparison.Ordinal);
        Assert.Empty(waited.Operations);
    }

    [Fact]
    public void InstallingAgainFromTheInstalledCopyRefreshesTheTask()
    {
        install.InstallMachine(Source, @"DESKTOP\dante", "0.1.0");
        var again = new MemorySession();
        var refresh = new AppInstall(Layout(), tasks, files, registry, shortcuts, again);

        refresh.InstallMachine(Path.Combine(Machine, "winland.exe"), @"DESKTOP\dante", "0.1.0");

        Assert.Equal(Path.Combine(Machine, "winland.exe"), tasks.Only().Executable);
        Assert.Equal("replace", tasks.Operations[^1]);
        Assert.Equal([1], files.Read(Path.Combine(Machine, "winland.exe")));
        Assert.Equal(["stop", "start"], again.Operations);
    }

    [Fact]
    public void UninstallRemovesTheInstallAndKeepsTheConfig()
    {
        install.InstallMachine(Source, @"DESKTOP\dante", "0.1.0");
        files.Seed(Path.Combine(State, "state.json"), [7]);
        files.Seed(Path.Combine(State, "winland.log"), [8]);

        install.Uninstall(purge: false, machine: true);

        Assert.False(files.DirectoryExists(Machine));
        Assert.False(tasks.Contains("Winland"));
        Assert.Empty(shortcuts.Shortcuts);
        Assert.Null(registry.GetString(RegistryRoot.Machine, InstallLocations.UninstallKey, "DisplayName"));
        Assert.Equal("", Text(RegistryRoot.Machine, InstallLocations.PathValue, InstallLocations.MachinePathKey));
        Assert.Equal([9], files.Read(Path.Combine(Config, "winland.conf")));
        Assert.Equal([7], files.Read(Path.Combine(State, "state.json")));
        Assert.Equal([8], files.Read(Path.Combine(State, "winland.log")));
    }

    [Fact]
    public void PurgeRemovesTheConfigTheLogsAndTheState()
    {
        install.InstallForUser(Source, "0.1.0");
        files.Seed(Path.Combine(State, "state.json"), [7]);
        files.Seed(Path.Combine(State, "winland.log"), [8]);

        install.Uninstall(purge: true, machine: false);

        Assert.False(files.DirectoryExists(User));
        Assert.False(files.DirectoryExists(Config));
        Assert.False(files.DirectoryExists(State));
        Assert.False(files.Exists(Path.Combine(Config, "winland.conf")));
        Assert.False(files.Exists(Path.Combine(State, "state.json")));
        Assert.False(files.Exists(Path.Combine(State, "winland.log")));
        Assert.Null(registry.GetString(RegistryRoot.User, InstallLocations.RunKey, InstallLocations.RunValue));
    }

    [Fact]
    public void APerUserUninstallLeavesTheMachineInstall()
    {
        install.InstallMachine(Source, @"DESKTOP\dante", "0.1.0");

        install.Uninstall(purge: false, machine: false);

        Assert.True(files.Exists(Path.Combine(Machine, "winland.exe")));
        Assert.True(tasks.Contains("Winland"));
        Assert.Equal(Machine, Text(RegistryRoot.Machine, InstallLocations.PathValue, InstallLocations.MachinePathKey));
    }

    [Fact]
    public void UninstallRestoresRecordedSettingsBeforePurgeDeletesTheFolder()
    {
        var folderStillThere = false;
        var watching = new WatchingRestore(() => folderStillThere = files.Exists(Path.Combine(Config, "winland.conf")));
        var watched = new AppInstall(Layout(), tasks, files, registry, shortcuts, session, watching);

        watched.Uninstall(purge: true, machine: false);

        Assert.True(folderStillThere);
        Assert.False(files.Exists(Path.Combine(Config, "winland.conf")));
    }

    [Fact]
    public void UninstallRemovesOnlyTheInstallFolderFromThePath()
    {
        registry.SetString(
            RegistryRoot.User,
            InstallLocations.UserPathKey,
            InstallLocations.PathValue,
            User + @"\;C:\Tools",
            expandable: true);
        files.Seed(Path.Combine(User, "winland.exe"), [1]);
        files.Seed(Path.Combine(User, "winlandctl.exe"), [3]);

        install.Uninstall(purge: false, machine: false);

        Assert.Equal(@"C:\Tools", Text(RegistryRoot.User, InstallLocations.PathValue, InstallLocations.UserPathKey));
    }

    private sealed class WatchingRestore(Action onRestore) : IDesktopRestore
    {
        public void Restore() => onRestore();
    }

    private string? Text(RegistryRoot root, string name, string? key = null) =>
        registry.GetString(root, key ?? InstallLocations.UninstallKey, name);

    private int Dword(RegistryRoot root, string name) =>
        registry.Values[(root, InstallLocations.UninstallKey, name)].Dword ?? throw new InvalidOperationException(name);

    private static InstallLayout Layout() => new(Machine, User, CommonPrograms, UserPrograms, Config, State);
}
