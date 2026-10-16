using Winland.Windows;

namespace Winland.Windows.Tests;

public class ProcessLauncherTests
{
    [Fact]
    public void LaunchHerePassesTheConfiguredEnvironmentToTheChild()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winland-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "env.txt");
        var command = "\"" + Path.Combine(Environment.SystemDirectory, "cmd.exe") + "\" /d /c set > env.txt";
        var launch = new Launch(command, directory, false)
        {
            Raw = true,
            Environment =
            [
                new EnvironmentAssignment("WINLAND_ENV_PROBE", "first"),
                new EnvironmentAssignment("WINLAND_ENV_PROBE", "second"),
                new EnvironmentAssignment("WINLAND_ENV_OTHER", "kept"),
            ],
        };

        try
        {
            var pid = ProcessLauncher.LaunchHere(launch);
            WaitForExit(pid);
            var text = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(file));
            Assert.Contains("WINLAND_ENV_PROBE=second", text);
            Assert.DoesNotContain("WINLAND_ENV_PROBE=first", text);
            Assert.Contains("WINLAND_ENV_OTHER=kept", text);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ARawCommandIsPassedAsWritten()
    {
        var launch = new Launch("\"C:\\Program Files\\App\\app.exe\" --flag \"a b\"", null, false) { Raw = true };

        Assert.Equal(launch.CommandLine, ProcessLauncher.CommandLine(launch));
    }

    [Fact]
    public void AShellCommandIsQuotedWholeForCmd()
    {
        var shell = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var launch = new Launch("\"C:\\Program Files\\App\\app.exe\" \"a b\" && echo done", null, false);

        Assert.Equal(
            "\"" + shell + "\" /d /s /c \"\"C:\\Program Files\\App\\app.exe\" \"a b\" && echo done\"",
            ProcessLauncher.CommandLine(launch));
    }

    [Fact]
    public void AShellCommandThatStartsWithAQuoteAndHasMoreQuotesRuns()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winland-quote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "out file.txt");
        var where = Path.Combine(Environment.SystemDirectory, "where.exe");
        var launch = new Launch("\"" + where + "\" \"cmd.exe\" > \"out file.txt\"", directory, false);

        try
        {
            var pid = ProcessLauncher.LaunchHere(launch);
            WaitForExit(pid);
            Assert.True(File.Exists(file), "The command did not run as written.");
            Assert.Contains("cmd.exe", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static void WaitForExit(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            if (!process.WaitForExit(10_000))
                throw new TimeoutException("The child did not exit.");
        }
        catch (ArgumentException)
        {
        }
    }
}
