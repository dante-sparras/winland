namespace Winland.Infrastructure.Tests;

/// <summary>Start and Search are flyouts of the shell. Winland leaves them unmanaged so Super does not tile them.</summary>
public class ShellFlyoutsTests
{
    [Theory]
    [InlineData("StartMenuExperienceHost.exe")]
    [InlineData("searchhost.exe")]
    [InlineData("ShellExperienceHost.exe")]
    public void TheShellFlyoutHostsAreFlyouts(string exe) => Assert.True(ShellFlyouts.Is(exe, "Windows.UI.Core.CoreWindow"));

    [Theory]
    [InlineData("")]
    [InlineData("Cursor.exe")]
    [InlineData("explorer.exe")]
    public void OtherProgramsAreNot(string exe) => Assert.False(ShellFlyouts.Is(exe, "Windows.UI.Core.CoreWindow"));

    // A taskbar button's preview and a notification icon's tip are XAML popups of Explorer. A File Explorer window,
    // and another program's XAML popup, are not.
    [Fact]
    public void ExplorersPopupsAreFlyouts()
    {
        Assert.True(ShellFlyouts.Is("explorer.exe", "Xaml_WindowedPopupClass"));
        Assert.False(ShellFlyouts.Is("explorer.exe", "CabinetWClass"));
        Assert.False(ShellFlyouts.Is("Notepad.exe", "Xaml_WindowedPopupClass"));
    }
}
