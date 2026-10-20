using Winland.Windows;
using Winland.Windows.Tests;

namespace Winland.Tests;

[Collection(DesktopCollection.Name)]
[Trait("Category", DesktopCollection.Name)]
public class GpuDeviceTests
{
    [Fact]
    public void TheSharedDeviceCanBeEndedAndMadeAgain()
    {
        if (Gpu.Acquire() is null)
            return;

        Gpu.Shutdown();
        var again = Gpu.Acquire();

        Assert.NotNull(again);
        Assert.False(again.Lost());
        Gpu.Shutdown();
    }
}
