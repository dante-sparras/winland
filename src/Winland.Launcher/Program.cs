using System.Diagnostics;
using Winland.Launcher;

// This is published as native code. No managed runtime reads the inherited environment before it is cleared.
var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "winland.exe"))
{
    UseShellExecute = false,
    WorkingDirectory = AppContext.BaseDirectory,
};
LaunchEnvironment.Clean(start);

foreach (var argument in args)
    start.ArgumentList.Add(argument);
using var daemon = Process.Start(start) ?? throw new InvalidOperationException("Winland did not start.");
daemon.WaitForExit();
return daemon.ExitCode;
