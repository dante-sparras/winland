using Winland.Windows;

namespace Winland.Windows.Tests;

public class TaskbarMarkerTests
{
    [Fact]
    public void TheCrashMarkerIsInTheDotWinlandFolder()
    {
        Assert.Equal(@"C:\Users\me\.winland\taskbar.marker", Taskbar.MarkerFile(@"C:\Users\me"));
    }
}
