namespace Winland.Infrastructure.Tests;

public class ProcessLauncherTests
{
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
