using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Winland.Install;
using Winland.Core;

namespace Winland.Daemon;

static class Elevation
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static int Relaunch(IReadOnlyList<string> args)
    {
        var path = Environment.ProcessPath
            ?? throw new InvalidOperationException("The executable path is not available.");
        RelaunchSpec spec;
        try
        {
            spec = RelaunchPlan.Create(
                path,
                args,
                Environment.CurrentDirectory,
                RuntimeEnvironment.GetRuntimeDirectory(),
                Environment.GetEnvironmentVariable("DOTNET_ROOT"),
                Environment.GetEnvironmentVariable("WINLAND_PIPE"),
                Environment.GetEnvironmentVariable("WINLAND_STATE"));
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        var start = new ProcessStartInfo
        {
            FileName = spec.FileName,
            Arguments = spec.Arguments,
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Elevation did not start.");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            Console.Error.WriteLine("Elevation was cancelled.");
            return 1;
        }
        catch (Win32Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
