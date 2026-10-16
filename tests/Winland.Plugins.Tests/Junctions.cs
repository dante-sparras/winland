namespace Winland.Tests;

/// <summary>Makes a directory junction. Unlike a symbolic link, it needs no privilege.</summary>
internal static class Junctions
{
    public static void Create(string link, string target)
    {
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target })
            start.ArgumentList.Add(argument);

        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>
    /// Removes the junction itself. A recursive delete of the folder holding it first tries to
    /// unmount it as a volume, which a process that is not elevated may not do.
    /// </summary>
    public static void Delete(string link)
    {
        if (Directory.Exists(link))
            Directory.Delete(link);
    }
}
