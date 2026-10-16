using Winland.Windows;
using Flags = Windows.Win32.System.Threading.PROCESS_CREATION_FLAGS;

namespace Winland.Windows.Tests;

public class ProcessLauncherTests
{
    [Fact]
    public void LaunchHerePassesTheConfiguredEnvironmentToTheChild()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winland-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "env.txt");
        var launch = new Launch("set > env.txt", directory, false)
        {
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

    [Fact]
    public void ARawProgramGetsAConsoleOfItsOwnAndTheShellOneWithNoWindow()
    {
        var raw = ProcessLauncher.CreationFlags(new Launch("app.exe", null, false) { Raw = true });
        var shell = ProcessLauncher.CreationFlags(new Launch("app.exe", null, false));

        Assert.True(raw.HasFlag(Flags.CREATE_NEW_CONSOLE));
        Assert.False(raw.HasFlag(Flags.CREATE_NO_WINDOW));
        Assert.True(shell.HasFlag(Flags.CREATE_NO_WINDOW));
    }

    [Fact]
    public void ARawConsoleProgramGetsAConsoleWindow()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winland-console-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var probe = Path.Combine(directory, "probe.ps1");
        var file = Path.Combine(directory, "console.txt");
        File.WriteAllText(
            probe,
            "$t = Add-Type -MemberDefinition '[DllImport(\"kernel32.dll\")] public static extern IntPtr GetConsoleWindow();' -Name ConsoleProbe -Namespace Probe -PassThru\r\n"
            + "[string]$t::GetConsoleWindow() | Set-Content -Path console.txt\r\n");
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var launch = new Launch("\"" + powershell + "\" -NoProfile -ExecutionPolicy Bypass -File \"" + probe + "\"", directory, false) { Raw = true };

        try
        {
            var pid = ProcessLauncher.LaunchHere(launch);
            WaitForExit(pid);
            var deadline = Environment.TickCount64 + 20_000;
            while (!File.Exists(file) && Environment.TickCount64 < deadline)
                Thread.Sleep(100);

            Assert.True(File.Exists(file), "The console program did not run.");
            Assert.NotEqual("0", File.ReadAllText(file).Trim());
        }
        finally
        {
            Thread.Sleep(200);
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
