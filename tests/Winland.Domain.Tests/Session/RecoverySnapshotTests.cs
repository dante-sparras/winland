namespace Winland.Domain.Tests;

public class RecoverySnapshotTests
{
    // A window made transparent while it was created hidden is not a Client. It is recorded apart, so a start after
    // the process was ended can make it opaque again.
    [Fact]
    public void AHeldWindowIsWrittenAndReadBack()
    {
        var snapshot = new RecoverySnapshot([]) { Held = [new HeldWindow(new WindowId(7), "Chrome_WidgetWin_1", 42)] };

        Assert.True(RecoverySnapshot.TryParse(snapshot.ToJson(), out var read));

        Assert.Equal(new HeldWindow(new WindowId(7), "Chrome_WidgetWin_1", 42), Assert.Single(read.Held));
        Assert.DoesNotContain("held", new RecoverySnapshot([]).ToJson(), StringComparison.Ordinal);
    }
}
