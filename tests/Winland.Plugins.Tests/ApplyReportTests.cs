using Winland.Plugins.Manager;

namespace Winland.Tests;

public class ApplyReportTests
{
    [Fact]
    public void ReportTravelsOverThePipe()
    {
        using var listener = ApplyReportListener.Start();

        var sent = ApplyReport.TrySend(listener.Name, ["Installed plugin 'sample'.", "Removed plugin 'gone'."], TimeSpan.FromSeconds(5), out var error);

        Assert.True(sent, error);
        Assert.Equal(["Installed plugin 'sample'.", "Removed plugin 'gone'."], listener.Collect());
    }

    [Fact]
    public void NoSenderCollectsNothing()
    {
        using var listener = ApplyReportListener.Start();

        Assert.Empty(listener.Collect());
    }

    [Fact]
    public void NamesAreWinlandApplyPipesOnly()
    {
        using var listener = ApplyReportListener.Start();

        Assert.True(ApplyReport.IsName(listener.Name));
        Assert.False(ApplyReport.IsName(@"C:\Users\me\apply.log"));
        Assert.False(ApplyReport.IsName(@"\\.\pipe\winland-apply-00000000000000000000000000000000"));
        Assert.False(ApplyReport.IsName("winland-apply-0000000000000000000000000000000"));
        Assert.False(ApplyReport.IsName("winland-apply-0000000000000000000000000000000G"));
        Assert.False(ApplyReport.TrySend(@"C:\Users\me\apply.log", ["x"], TimeSpan.FromSeconds(1), out _));
    }
}
