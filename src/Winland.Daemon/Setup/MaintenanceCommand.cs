using System.Security.Principal;
using Winland.Install;

namespace Winland.Daemon;

/// <summary>The commands that install, update, uninstall and tweak Winland: <c>winland install</c> and the rest.</summary>
static class MaintenanceCommand
{
    public static int Run(string[] args, InstallCommand command)
    {
        // One approval for the machine install, and for uninstalling it. The per-user install never asks.
        // The relaunch carries the invoking user. An over-the-shoulder approval runs as the administrator
        // who approved it, which is not the account that logs on.
        var needsAdministrator = command is InstallCommand.Remove
            ? MachineInstalled()
            : command.RequiresAdministrator(machineInstallPresent: false);
        if (needsAdministrator && !Elevation.IsElevated())
        {
            var relay = command is InstallCommand.Machine { Account: null } machine ? machine.Relay(CurrentUser()) : args;
            var result = Elevation.Relaunch(relay);
            if (result == 0)
            {
                if (command is InstallCommand.Machine)
                    Maintenance().RemoveUserInstall();
                else if (command is InstallCommand.Remove remove)
                {
                    TaskbarControl.RecoverHidden();
                    Maintenance().Uninstall(remove.Purge, machine: false);
                }
            }

            return result;
        }

        return Perform(command);
    }

    private static int Perform(InstallCommand command)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The executable path is not available.");
        var app = Maintenance();
        switch (command)
        {
            case InstallCommand.UserCleanup:
                if (Elevation.IsElevated())
                    throw new InvalidOperationException("User cleanup must run from a normal terminal.");
                app.RemoveUserInstall();
                return 0;
            case InstallCommand.Machine machine:
                var account = string.IsNullOrWhiteSpace(machine.Account) ? CurrentUser() : machine.Account;
                var start = machine.Start && account.Equals(CurrentUser(), StringComparison.OrdinalIgnoreCase);
                app.InstallMachine(executable, account, BuildStamp.Version, machine.Logon, start);
                Console.WriteLine(Installed("Winland is installed", machine.Logon, start));
                if (machine.Start && !start)
                    Console.WriteLine("Start Winland from the signed-in user's Start menu.");
                return 0;
            case InstallCommand.ForUser user:
                app.InstallForUser(executable, BuildStamp.Version, user.Logon, user.Start);
                Console.WriteLine(Installed("Winland is installed for this user", user.Logon, user.Start));
                return 0;
            case InstallCommand.Remove remove:
                if (!Elevation.IsElevated())
                    TaskbarControl.RecoverHidden();
                app.Uninstall(remove.Purge, machine: Elevation.IsElevated());
                Console.WriteLine(remove.Purge
                    ? "Winland is uninstalled, and its config, logs and state are removed."
                    : "Winland is uninstalled.");
                return 0;
            case InstallCommand.Update update:
                return UpdateInstalled(update.Channel);
            case InstallCommand.Tweaks tweaks:
                RecordedSettings().TurnOff(tweaks.Touchpad);
                Console.WriteLine(tweaks.Touchpad
                    ? "Snap windows and the three- and four-finger touchpad swipes are off."
                    : "Snap windows is off.");
                return 0;
            default:
                throw new InvalidOperationException("Unexpected install command.");
        }
    }

    private static string Installed(string installed, bool logon, bool start) => (logon, start) switch
    {
        (true, true) => installed + ", running, and will start at logon.",
        (true, false) => installed + " and will start at logon.",
        (false, true) => installed + " and running.",
        (false, false) => installed + ".",
    };

    private static int UpdateInstalled(UpdateChannel? requested)
    {
        var saved = new FileSettingsText(UpdateChannelFile.PathFor(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        var channel = UpdateChannelFile.Choose(requested, Elevation.IsElevated() ? null : saved.Read());
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The executable path is not available.");
        var layout = Layout();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var result = new ReleaseUpdate(
            new GitHubReleaseFeed(http),
            new ZipReleasePackage(),
            Maintenance(),
            StageDirectory,
            RemoveStage).Run(
                BuildStamp.Version,
                executable,
                layout.MachineDirectory,
                layout.UserDirectory,
                Elevation.IsElevated(),
                channel,
                BuildStamp.Commit);

        switch (result)
        {
            case UpdateResult.NotInstalled:
                Console.Error.WriteLine("Winland is not installed. Run winland install.");
                return 1;
            case UpdateResult.Current:
                Remember();
                Console.WriteLine("Winland is up to date.");
                return 0;
            case UpdateResult.NeedsAdministrator:
                var resultCode = Elevation.Relaunch(["update", "--channel", channel == UpdateChannel.Dev ? "dev" : "stable"]);
                if (resultCode == 0)
                    Remember();
                return resultCode;
            case UpdateResult.Rejected:
                Console.Error.WriteLine("Refusing the update: the SHA-256 of Winland-win-x64.zip does not match SHA256SUMS. The release may still be uploading. Try again in a minute.");
                return 1;
            case UpdateResult.Updated updated:
                if (Elevation.IsElevated())
                    _ = SchtasksCatalog.UseLauncher(executable);
                Remember();
                Console.WriteLine("Winland is updated to " + updated.Version + ".");
                return 0;
            default:
                throw new InvalidOperationException("Unexpected update result.");
        }

        // A channel asked for is kept only once the update on it went through, so a failed one changes nothing.
        void Remember()
        {
            if (!Elevation.IsElevated() && requested is UpdateChannel chosen)
                saved.Write(UpdateChannelFile.Text(chosen));
        }
    }

    private static string StageDirectory()
    {
        // An elevated replace copies into Program Files. The unpacked zip stays in a folder only
        // administrators can write, so another program cannot swap it after the hash check.
        if (Elevation.IsElevated())
        {
            var windowsTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            return AdministratorFolder.CreateNew(windowsTemp, "winland-update-");
        }

        var folder = Path.Combine(Path.GetTempPath(), "winland-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void RemoveStage(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private static bool MachineInstalled() => Maintenance().MachinePresent();

    private static InstallLayout Layout() => new(
        InstallLocations.Machine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),
        InstallLocations.User(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        InstallLocations.Config(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        InstallLocations.State(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

    private static AppInstall Maintenance() => new(
        Layout(),
        new SchtasksCatalog(),
        new WindowsInstallFiles(),
        new WindowsInstallRegistry(),
        new ShellStartMenu(),
        new DaemonSession(),
        RecordedSettings());

    private static DesktopSettings RecordedSettings() => new(
        new WindowsDesktop(),
        new FileSettingsText(InstallLocations.SettingsFile(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))));

    private static string CurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.Name;
    }
}
