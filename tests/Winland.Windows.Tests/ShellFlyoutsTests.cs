using Winland.Windows;
using Winland.Core;

namespace Winland.Windows.Tests;

/// <summary>Start and Search are flyouts of the shell. Winland leaves them unmanaged so Super does not tile them.</summary>
public class ShellFlyoutsTests
{
    [Theory]
    [InlineData("StartMenuExperienceHost.exe")]
    [InlineData("searchhost.exe")]
    [InlineData("ShellExperienceHost.exe")]
    public void TheShellFlyoutHostsAreFlyouts(string exe) => Assert.True(ShellFlyouts.Is(exe));

    [Theory]
    [InlineData("")]
    [InlineData("Cursor.exe")]
    [InlineData("explorer.exe")]
    public void OtherProgramsAreNot(string exe) => Assert.False(ShellFlyouts.Is(exe));
}
