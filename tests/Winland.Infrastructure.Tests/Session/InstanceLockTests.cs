
namespace Winland.Infrastructure.Tests;

public class InstanceLockTests
{
    [Fact]
    public void ASecondTakeFailsUntilTheFirstIsReleased()
    {
        var name = InstanceLock.NameFor("winland-test-" + Guid.NewGuid().ToString("N"));

        var first = InstanceLock.TryTake(name);
        Assert.NotNull(first);
        Assert.Null(InstanceLock.TryTake(name));

        first.Dispose();
        using var again = InstanceLock.TryTake(name);
        Assert.NotNull(again);
    }

    [Fact]
    public void TheNameIsPerSessionAndPerPipe()
    {
        Assert.Equal(@"Local\Winland-winland", InstanceLock.NameFor("winland"));
        Assert.Equal(@"Local\Winland-a_b", InstanceLock.NameFor(@"a\b"));
    }
}
