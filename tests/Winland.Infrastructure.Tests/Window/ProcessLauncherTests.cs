using Flags = Windows.Win32.System.Threading.PROCESS_CREATION_FLAGS;

namespace Winland.Infrastructure.Tests;

public class ProcessLauncherTests
{
    [Fact]
    public void ARawCommandIsPassedAsWritten()
    {
        var launch = new Launch("\"C:\\Program Files\\App\\app.exe\" --flag \"a b\"", null) { Raw = true };

        Assert.Equal(launch.CommandLine, ProcessLauncher.CommandLine(launch));
    }

    [Fact]
    public void AShellCommandIsQuotedWholeForCmd()
    {
        var shell = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var launch = new Launch("\"C:\\Program Files\\App\\app.exe\" \"a b\" && echo done", null);

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
        var launch = new Launch("\"" + where + "\" \"cmd.exe\" > \"out file.txt\"", directory);

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
        var raw = ProcessLauncher.CreationFlags(new Launch("app.exe", null) { Raw = true });
        var shell = ProcessLauncher.CreationFlags(new Launch("app.exe", null));

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
        var launch = new Launch("\"" + powershell + "\" -NoProfile -ExecutionPolicy Bypass -File \"" + probe + "\"", directory) { Raw = true };

        try
        {
            // Add-Type compiles the probe before it runs, which takes more than ten seconds on a slow runner.
            var pid = ProcessLauncher.LaunchHere(launch);
            WaitForExit(pid, 60_000);
            var deadline = Environment.TickCount64 + 20_000;
            while (!File.Exists(file) && Environment.TickCount64 < deadline)
                Thread.Sleep(100);

            Assert.True(File.Exists(file), "The console program did not run.");
            Assert.NotEqual("0", File.ReadAllText(file).Trim());
        }
        finally
        {
            DeleteWhenFree(directory);
        }
    }

    // A child that is still exiting holds its working folder for a moment. Cleanup must not hide the
    // assertion that failed, so a folder that stays in use is left in the temp folder.
    private static void DeleteWhenFree(string directory)
    {
        var deadline = Environment.TickCount64 + 5_000;
        while (Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (IOException) when (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(200);
            }
            catch (IOException)
            {
                return;
            }
        }
    }

    // Window rules match a Client by the processes that started it, nearest first.
    [Fact]
    public void AncestorsStartWithTheParent()
    {
        using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /c ping -n 30 127.0.0.1 >nul")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        try
        {
            var ancestors = ProcessLauncher.Ancestors(child.Id);

            Assert.Equal(Environment.ProcessId, ancestors[0]);
            Assert.DoesNotContain(child.Id, ancestors);
            Assert.All(ancestors, id => Assert.True(id > 0));
            Assert.Equal(ancestors.Count, ancestors.Distinct().Count());
            Assert.InRange(ancestors.Count, 1, 25);
            Assert.Equal(ancestors.Skip(1), ProcessLauncher.Ancestors(Environment.ProcessId).Take(ancestors.Count - 1));
        }
        finally
        {
            child.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void NoProcessHasNoAncestors()
    {
        Assert.Empty(ProcessLauncher.Ancestors(0));
        Assert.Empty(ProcessLauncher.Ancestors(-4));
        Assert.Throws<ArgumentNullException>(() => ProcessLauncher.LaunchHere(null!));
        Assert.Throws<ArgumentNullException>(() => ProcessLauncher.CreationFlags(null!));
    }

    private static void WaitForExit(int pid, int milliseconds = 10_000)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            if (!process.WaitForExit(milliseconds))
                throw new TimeoutException("The child did not exit.");
        }
        catch (ArgumentException)
        {
        }
    }
}
