using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Winland.Install;
using Winland.Plugins;
using Winland.Plugins.Manager;

namespace Winland;

/// <summary>Runs <c>winlandctl plugin install|update|remove|list --installed</c> and the elevated <c>plugin apply</c>.</summary>
static class PluginManagerCli
{
    public static int Run(ManagerCommand command, string? pipe)
    {
        if (command is ManagerCommand.Invalid invalid)
        {
            Console.Error.WriteLine(invalid.Message);
            return 2;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("The plugin manager runs only on Windows.");
            return 1;
        }

        if (command is ManagerCommand.Apply apply)
            return Apply(apply.Request);

        var manager = new PluginManager(
            Paths(),
            new SystemProcessRunner(),
            new WindowsElevator(),
            new PipePlugins(pipe),
            Console.Out,
            Console.Error);
        return command switch
        {
            ManagerCommand.Install install => manager.Install(install.Url, install.Revision),
            ManagerCommand.Update update => manager.Update(update.Force),
            ManagerCommand.Remove remove => manager.Remove(remove.Identifier),
            ManagerCommand.List => manager.List(),
            _ => 2,
        };
    }

    [SupportedOSPlatform("windows")]
    private static int Apply(ApplyRequest request)
    {
        ApplyResult result;
        try
        {
            result = new PluginApply(new WindowsPluginLocationGuard(), new WindowsPluginDirectoryProtector()).Run(request);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            result = new ApplyResult(false, [exception.Message]);
        }

        foreach (var line in result.Lines)
            Console.WriteLine(line);

        if (request.Report is not null)
        {
            try
            {
                File.WriteAllLines(request.Report, result.Lines);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(exception.Message);
            }
        }

        return result.Ok ? 0 : 1;
    }

    private static PluginManagerPaths Paths()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var directory = Path.GetFullPath(PluginPaths.DefaultDirectory(
            null,
            Environment.GetEnvironmentVariable(PluginPaths.DirectoryVariable),
            programData));
        var approvals = Path.GetFullPath(PluginPaths.DefaultApprovals(
            null,
            Environment.GetEnvironmentVariable(PluginPaths.ApprovalsVariable),
            directory));
        var staging = Environment.GetEnvironmentVariable(PluginManagerPaths.StagingVariable);
        if (string.IsNullOrWhiteSpace(staging))
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            staging = Path.Combine(local, "Winland", "plugin-staging");
        }

        var api = Path.Combine(AppContext.BaseDirectory, "Winland.PluginApi.dll");
        return new PluginManagerPaths(Path.GetFullPath(staging), directory, approvals, api);
    }

    [SupportedOSPlatform("windows")]
    private sealed class WindowsElevator : IElevator
    {
        public bool IsElevated
        {
            get
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public ElevatedRun Run(IReadOnlyList<string> arguments)
        {
            var path = Environment.ProcessPath;
            if (path is null)
                return new ElevatedRun(false, 1, "The executable path is not available.");

            // Run through the dotnet host, the entry assembly comes first.
            var all = arguments;
            if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                all = [Path.Combine(AppContext.BaseDirectory, "winlandctl.dll"), .. arguments];

            var spec = RelaunchPlan.Create(
                path,
                all,
                AppContext.BaseDirectory,
                RuntimeEnvironment.GetRuntimeDirectory(),
                Environment.GetEnvironmentVariable("DOTNET_ROOT"),
                pipe: null,
                state: null,
                Environment.GetEnvironmentVariable("ComSpec"));
            var start = new ProcessStartInfo
            {
                FileName = spec.FileName,
                Arguments = spec.Arguments,
                WorkingDirectory = spec.WorkingDirectory,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            try
            {
                using var process = Process.Start(start);
                if (process is null)
                    return new ElevatedRun(false, 1, "The elevated step did not start.");

                process.WaitForExit();
                return new ElevatedRun(true, process.ExitCode, null);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223) // ERROR_CANCELLED
            {
                return new ElevatedRun(false, 1, "The approval was cancelled.");
            }
            catch (Win32Exception exception)
            {
                return new ElevatedRun(false, 1, exception.Message);
            }
        }
    }

    private sealed class PipePlugins(string? pipe) : IRunningPlugins
    {
        public bool Unload(string name) => Send("plugin unload " + name);

        public bool Load(string path) => Send("plugin load " + path);

        private bool Send(string line)
        {
            try
            {
                return CommandClient.Transact(line, pipe) == "ok";
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException)
            {
                return false;
            }
        }
    }
}
