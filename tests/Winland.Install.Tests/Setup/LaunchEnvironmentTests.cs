using System.Diagnostics;
using Winland.Launcher;

namespace Winland.Install.Tests;

public class LaunchEnvironmentTests
{
    [Fact]
    public void RuntimeInjectionAndTracingVariablesAreRemovedBeforeStartingTheDaemon()
    {
        var start = new ProcessStartInfo();
        start.Environment.Clear();
        foreach (var name in new[] { "DOTNET_STARTUP_HOOKS", "dotnet_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE",
                     "DOTNET_ROOT_X64", "DOTNET_JitName", "CORECLR_ENABLE_PROFILING", "CORECLR_PROFILER_PATH",
                     "COMPlus_AltJitName", "COR_ENABLE_PROFILING", "COREHOST_TRACEFILE", "DOTNET_HOST_TRACEFILE" })
            start.Environment[name] = "untrusted";
        start.Environment["PATH"] = "user path";
        start.Environment["WINLAND_PIPE"] = "isolated";

        LaunchEnvironment.Clean(start);

        Assert.Equal(2, start.Environment.Count);
        Assert.Equal("user path", start.Environment["PATH"]);
        Assert.Equal("isolated", start.Environment["WINLAND_PIPE"]);
    }
}
