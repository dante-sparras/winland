using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Winland.Plugins.Manager;

public sealed record ProcessResult(int ExitCode, string Output);

/// <summary>Runs git and dotnet as the current user. Tests pass a fake.</summary>
public interface IProcessRunner
{
    ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory);
}

/// <summary>The outcome of asking Windows to run the elevated step, and the report it sent back.</summary>
public sealed record ElevatedRun(bool Started, int ExitCode, string? Error)
{
    public IReadOnlyList<string> Lines { get; init; } = [];
}

/// <summary>
/// Asks for one UAC confirmation and runs <c>winlandctl</c> elevated with the given arguments.
/// The elevator carries the report back itself (see <see cref="ApplyReport"/>).
/// </summary>
public interface IElevator
{
    bool IsElevated { get; }

    ElevatedRun Run(IReadOnlyList<string> arguments);
}

/// <summary>Unloads and loads plugins in a running Winland. Both return false when Winland is not running or refuses.</summary>
public interface IRunningPlugins
{
    bool Unload(string name);

    bool Load(string path);

    /// <summary>The names of the plugins loaded in the running Winland, or null when it cannot be asked.</summary>
    IReadOnlyList<string>? Loaded();

    /// <summary>Shows a notification in the running Winland, as the <c>notify</c> command does.</summary>
    void Notify(int icon, int milliseconds, string message);
}

public sealed class SystemProcessRunner : IProcessRunner
{
    public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        // cmd.exe reads its own command line, not argv, so a build step goes to it as written.
        if (fileName == PluginStaging.Shell && arguments.Count == 1)
        {
            start.Arguments = "/d /s /c \"" + arguments[0] + "\"";
        }
        else
        {
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);
        }

        // A build must never wait on a prompt the user cannot see.
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";

        var output = new StringBuilder();
        try
        {
            using var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, line) => Append(output, line.Data);
            process.ErrorDataReceived += (_, line) => Append(output, line.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            lock (output)
                return new ProcessResult(process.ExitCode, output.ToString());
        }
        catch (Win32Exception exception)
        {
            return new ProcessResult(-1, $"Could not start {fileName}: {exception.Message}");
        }
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null)
            return;

        lock (output)
            output.AppendLine(line);
    }
}
