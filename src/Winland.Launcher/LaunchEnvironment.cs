using System.Diagnostics;

namespace Winland.Launcher;

internal static class LaunchEnvironment
{
    internal static void Clean(ProcessStartInfo start)
    {
        foreach (var name in start.Environment.Keys.ToArray())
        {
            if (name.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("COMPlus_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("COR_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("COREHOST_", StringComparison.OrdinalIgnoreCase))
                start.Environment.Remove(name);
        }
    }
}
