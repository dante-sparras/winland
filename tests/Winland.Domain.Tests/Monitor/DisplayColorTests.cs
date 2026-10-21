
namespace Winland.Domain.Tests;

public class DisplayColorTests
{
    [Fact]
    public void TheReplyNamesTheDepthWindowsReports()
    {
        Assert.Equal("XRGB2101010", DisplayColors.Format(10));
        Assert.Equal("XRGB8888", DisplayColors.Format(8));
        Assert.Equal("", DisplayColors.Format(0));
        Assert.Equal("srgb", DisplayColors.StandardPreset);
    }
}
