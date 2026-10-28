namespace Winland.Infrastructure.Tests;

public class TaskbarMarkerTests
{
    [Fact]
    public void TheCrashMarkerIsInTheDotWinlandFolder()
    {
        Assert.Equal(@"C:\Users\me\.winland\taskbar.marker", Taskbar.MarkerFile(@"C:\Users\me"));
    }

    [Fact]
    public void AWinlandOnItsOwnPipeKeepsAMarkerOfItsOwn()
    {
        Assert.Equal(@"C:\Users\me\.winland\taskbar.winland-test.marker", Taskbar.MarkerFile(@"C:\Users\me", "winland-test"));
    }
}
