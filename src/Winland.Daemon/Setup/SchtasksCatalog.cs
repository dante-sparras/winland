using System.Diagnostics;
using System.Text;
using Winland.Install;

namespace Winland.Daemon;

sealed class SchtasksCatalog : ITaskCatalog
{
    public bool Contains(string taskName) =>
        Run(SchtasksArguments.Query(taskName)).ExitCode == 0;

    public void Create(LogonTask task) => Register(task, update: false);

    public void Replace(LogonTask task) => Register(task, update: true);

    public void Delete(string taskName)
    {
        var result = Run(SchtasksArguments.Delete(taskName));
        if (result.ExitCode != 0)
            throw Failed("Could not remove the logon task.", result);
    }

    // schtasks reads the definition from a file. In the user's %TEMP% any program the user runs
    // could swap that file before schtasks reads it, and the task runs at the highest privileges.
    // A new folder in the Windows temp folder that only administrators can write cannot be
    // touched: users may create entries there but not rename or delete another's.
    private static void Register(LogonTask task, bool update)
    {
        var windowsTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        var folder = AdministratorFolder.CreateNew(windowsTemp, "winland-");
        var path = Path.Combine(folder, "task.xml");
        try
        {
            File.WriteAllText(path, task.Xml, Encoding.Unicode);
            var arguments = update
                ? SchtasksArguments.Update(task.Name, path)
                : SchtasksArguments.Create(task.Name, path);
            var result = Run(arguments);
            if (result.ExitCode != 0)
                throw Failed("Could not register the logon task.", result);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static InvalidOperationException Failed(string fallback, SchtasksResult result)
    {
        var detail = string.Join(
            Environment.NewLine,
            new[] { result.Output, result.Error }.Where(text => !string.IsNullOrWhiteSpace(text))).Trim();
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"{fallback} schtasks exited with code {result.ExitCode}."
            : detail;
        return new InvalidOperationException(message);
    }

    private static SchtasksResult Run(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start schtasks.exe.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new SchtasksResult(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    private readonly record struct SchtasksResult(int ExitCode, string Output, string Error);
}
