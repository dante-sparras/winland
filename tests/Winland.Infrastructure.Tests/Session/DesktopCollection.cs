using System.Diagnostics;

namespace Winland.Infrastructure.Tests;

/// <summary>
/// The tests that open real top-level windows or set process-wide state such as the DPI awareness.
/// xunit runs this collection by itself, after the others, one test at a time.
/// Each class in it also carries <c>[Trait("Category", DesktopCollection.Name)]</c>, which the
/// mutation lane filters out: Stryker runs several test processes at once, and xunit cannot keep
/// those from opening windows at the same time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DesktopCollection
{
    public const string Name = "Desktop";
}

/// <summary>
/// A test that a running daemon would disturb: it adopts the windows the test opens, tiles them,
/// and restores them when they are minimized. The test skips while any Winland process exists,
/// and runs where no daemon is running, as in CI.
/// </summary>
public class DesktopFactAttribute : FactAttribute
{
    public DesktopFactAttribute()
    {
        if (DaemonRunning())
            Skip = "A Winland process is running. It adopts and restores the windows this test opens. Stop it to run the test.";
    }

    private static bool DaemonRunning()
    {
        var running = Process.GetProcessesByName("winland");
        try
        {
            return running.Length > 0;
        }
        finally
        {
            foreach (var process in running)
                process.Dispose();
        }
    }
}
