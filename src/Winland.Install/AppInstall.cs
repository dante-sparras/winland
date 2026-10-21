namespace Winland.Install;

/// <summary>The directories one install reads and writes. They come from the process's special folders.</summary>
public sealed record InstallLayout(
    string MachineDirectory,
    string UserDirectory,
    string CommonPrograms,
    string UserPrograms,
    string ConfigDirectory,
    string StateDirectory);

/// <summary>
/// Copies Winland into place, registers how it starts at logon, and removes that again.
/// The elevated task only ever starts the copy under Program Files.
/// </summary>
public sealed class AppInstall
{
    private readonly InstallLayout layout;
    private readonly ITaskCatalog tasks;
    private readonly IFileTree files;
    private readonly IRegistry registry;
    private readonly IStartMenu shortcuts;
    private readonly IDaemonSession session;
    private readonly IDesktopRestore desktop;

    public AppInstall(
        InstallLayout layout,
        ITaskCatalog tasks,
        IFileTree files,
        IRegistry registry,
        IStartMenu shortcuts,
        IDaemonSession session,
        IDesktopRestore? desktop = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(session);
        this.layout = layout;
        this.tasks = tasks;
        this.files = files;
        this.registry = registry;
        this.shortcuts = shortcuts;
        this.session = session;
        this.desktop = desktop ?? NoDesktopRestore.Instance;
    }

    public bool MachinePresent() =>
        files.DirectoryExists(layout.MachineDirectory)
        || tasks.Contains(LogonTaskDefinition.TaskName)
        || registry.GetString(RegistryRoot.Machine, InstallLocations.UninstallKey, "DisplayName") is not null;

    public void InstallMachine(string executable, string account, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var plan = Plan(executable, layout.MachineDirectory);
        session.Stop();
        Apply(plan);
        var installed = plan.InstalledExecutable;
        // The task starts this copy, never the folder install was run from. That folder is one the user can write.
        new LogonInstall(tasks).Install(installed, account);
        Place(installed, layout.CommonPrograms, arguments: "");
        RegisterUninstall(RegistryRoot.Machine, installed, layout.MachineDirectory, version);
        SetPath(RegistryRoot.Machine, InstallLocations.MachinePathKey, layout.MachineDirectory);
        RemoveUser();
        session.Start(installed, []);
    }

    public void InstallForUser(string executable, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var plan = Plan(executable, layout.UserDirectory);
        session.Stop();
        Apply(plan);
        var installed = plan.InstalledExecutable;
        // No scheduled task. The Run key starts this copy unelevated, and a start from the folder never asks for UAC.
        var command = Quote(installed) + " " + ManualStartElevation.OptOutArgument;
        registry.SetString(RegistryRoot.User, InstallLocations.RunKey, InstallLocations.RunValue, command, expandable: false);
        Place(installed, layout.UserPrograms, ManualStartElevation.OptOutArgument);
        RegisterUninstall(RegistryRoot.User, installed, layout.UserDirectory, version);
        SetPath(RegistryRoot.User, InstallLocations.UserPathKey, layout.UserDirectory);
        session.Start(installed, [ManualStartElevation.OptOutArgument]);
    }

    /// <summary>
    /// Stops the running Winland, copies <paramref name="sourceExecutable"/> into <paramref name="destination"/>
    /// by the same copy install uses, and starts that copy. The recorded version is updated. The config, the
    /// logon task and the other install are left as they are.
    /// </summary>
    public void ReplaceInstalled(string sourceExecutable, string destination, bool perUser, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var plan = Plan(sourceExecutable, destination);
        session.Stop();
        Apply(plan);
        var installed = plan.InstalledExecutable;
        var root = perUser ? RegistryRoot.User : RegistryRoot.Machine;
        RegisterUninstall(root, installed, destination, version);
        session.Start(installed, perUser ? [ManualStartElevation.OptOutArgument] : []);
    }

    /// <param name="machine">True when this process may change the machine install. A per-user uninstall leaves it.</param>
    public void Uninstall(bool purge, bool machine)
    {
        session.Stop();
        // The record lives in the folder --purge deletes, so put the settings back first.
        desktop.Restore();
        if (machine)
            RemoveMachine();

        RemoveUser();
        if (!purge)
            return;

        files.DeleteTree(layout.ConfigDirectory);
        files.DeleteTree(layout.StateDirectory);
    }

    private sealed class NoDesktopRestore : IDesktopRestore
    {
        public static readonly NoDesktopRestore Instance = new();

        public void Restore()
        {
        }
    }

    private sealed record CopyPlan(
        string DaemonDirectory,
        IReadOnlyList<ListedFile> DaemonFiles,
        string? ControlDirectory,
        IReadOnlyList<ListedFile> ControlFiles,
        string Destination,
        string InstalledExecutable);

    private CopyPlan Plan(string executable, string destination)
    {
        var (daemonDirectory, controlDirectory) = InstallPayload.Locate(executable, files.Exists);
        var daemonFiles = files.List(daemonDirectory);
        Require(daemonFiles, InstallPayload.DaemonFile, daemonDirectory);
        RefuseLinks(daemonDirectory, daemonFiles);

        IReadOnlyList<ListedFile> controlFiles = [];
        if (controlDirectory is null)
            Require(daemonFiles, InstallPayload.ControlFile, daemonDirectory);
        else
        {
            controlFiles = files.List(controlDirectory);
            Require(controlFiles, InstallPayload.ControlFile, controlDirectory);
            RefuseLinks(controlDirectory, controlFiles);
            RefuseDifferentCopies(daemonDirectory, daemonFiles, controlDirectory, controlFiles);
        }

        return new CopyPlan(
            daemonDirectory,
            daemonFiles,
            controlDirectory,
            controlFiles,
            destination,
            InstallLocations.Executable(destination));
    }

    private void Apply(CopyPlan plan)
    {
        if (plan.ControlDirectory is not null && !SameDirectory(plan.ControlDirectory, plan.Destination))
        {
            files.PrepareDirectory(plan.Destination);
            CopyEach(plan.ControlDirectory, plan.ControlFiles, plan.Destination);
        }

        if (!SameDirectory(plan.DaemonDirectory, plan.Destination))
        {
            files.PrepareDirectory(plan.Destination);
            CopyEach(plan.DaemonDirectory, plan.DaemonFiles, plan.Destination);
        }
    }

    private void CopyEach(string sourceDirectory, IReadOnlyList<ListedFile> listed, string destination)
    {
        foreach (var file in listed)
            files.Copy(Path.Combine(sourceDirectory, file.RelativePath), Path.Combine(destination, file.RelativePath));
    }

    private void RefuseDifferentCopies(
        string daemonDirectory,
        IReadOnlyList<ListedFile> daemonFiles,
        string controlDirectory,
        IReadOnlyList<ListedFile> controlFiles)
    {
        var daemonNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in daemonFiles)
            daemonNames[file.RelativePath] = Path.Combine(daemonDirectory, file.RelativePath);

        foreach (var file in controlFiles)
        {
            if (!daemonNames.TryGetValue(file.RelativePath, out var daemonPath))
                continue;

            var controlPath = Path.Combine(controlDirectory, file.RelativePath);
            if (!files.SameContent(daemonPath, controlPath))
            {
                throw new InvalidOperationException(
                    $"Cannot install '{file.RelativePath}': winland and winlandctl both contain it and they differ.");
            }
        }
    }

    private static void Require(IReadOnlyList<ListedFile> files, string name, string directory)
    {
        if (files.Any(file => file.RelativePath.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return;

        throw new InvalidOperationException($"'{name}' was not found in '{directory}'.");
    }

    private static void RefuseLinks(string directory, IReadOnlyList<ListedFile> files)
    {
        var link = files.FirstOrDefault(file => file.ReparsePoint);
        if (link is null)
            return;

        throw new InvalidOperationException(
            $"Refusing to install '{Path.Combine(directory, link.RelativePath)}': it is a link.");
    }

    private void Place(string executable, string programsDirectory, string arguments)
    {
        shortcuts.Place(new Shortcut(
            InstallLocations.Shortcut(programsDirectory),
            executable,
            arguments,
            InstallLocations.AppUserModelId));
    }

    private void RegisterUninstall(RegistryRoot root, string executable, string directory, string version)
    {
        registry.SetString(root, InstallLocations.UninstallKey, "DisplayName", "Winland", expandable: false);
        registry.SetString(root, InstallLocations.UninstallKey, "UninstallString", Quote(executable) + " uninstall", expandable: false);
        registry.SetString(root, InstallLocations.UninstallKey, "InstallLocation", directory, expandable: false);
        registry.SetString(root, InstallLocations.UninstallKey, "DisplayVersion", version, expandable: false);
        registry.SetDword(root, InstallLocations.UninstallKey, "NoModify", 1);
        registry.SetDword(root, InstallLocations.UninstallKey, "NoRepair", 1);
    }

    private void SetPath(RegistryRoot root, string key, string folder)
    {
        var current = registry.GetString(root, key, InstallLocations.PathValue);
        if (PathList.Add(current, folder) is string updated)
            registry.SetString(root, key, InstallLocations.PathValue, updated, expandable: true);
    }

    private void RemoveMachine()
    {
        new LogonInstall(tasks).Uninstall();
        shortcuts.Remove(InstallLocations.Shortcut(layout.CommonPrograms));
        registry.DeleteKey(RegistryRoot.Machine, InstallLocations.UninstallKey);
        ClearPath(RegistryRoot.Machine, InstallLocations.MachinePathKey, layout.MachineDirectory);
        files.DeleteTree(layout.MachineDirectory);
    }

    private void RemoveUser()
    {
        registry.DeleteValue(RegistryRoot.User, InstallLocations.RunKey, InstallLocations.RunValue);
        shortcuts.Remove(InstallLocations.Shortcut(layout.UserPrograms));
        registry.DeleteKey(RegistryRoot.User, InstallLocations.UninstallKey);
        ClearPath(RegistryRoot.User, InstallLocations.UserPathKey, layout.UserDirectory);
        files.DeleteTree(layout.UserDirectory);
    }

    private void ClearPath(RegistryRoot root, string key, string folder)
    {
        var current = registry.GetString(root, key, InstallLocations.PathValue);
        if (PathList.Remove(current, folder) is string updated)
            registry.SetString(root, key, InstallLocations.PathValue, updated, expandable: true);
    }

    private static bool SameDirectory(string left, string right) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left))
            .Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    private static string Quote(string value)
    {
        if (value.Contains('"'))
            throw new ArgumentException($"Cannot quote '{value}': it contains a quote.", nameof(value));

        return "\"" + value + "\"";
    }
}
