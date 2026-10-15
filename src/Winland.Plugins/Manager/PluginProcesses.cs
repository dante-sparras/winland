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

/// <summary>The outcome of asking Windows to run the elevated step.</summary>
public sealed record ElevatedRun(bool Started, int ExitCode, string? Error);

/// <summary>Asks for one UAC confirmation and runs <c>winlandctl</c> elevated with the given arguments.</summary>
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
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

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
