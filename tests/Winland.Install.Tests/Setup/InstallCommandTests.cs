using Winland.Install;

namespace Winland.Install.Tests;

public class InstallCommandTests
{
    [Fact]
    public void InstallAsksForAnAdministratorAndCarriesTheAccount()
    {
        var first = Assert.IsType<InstallCommand.Machine>(InstallCommand.Parse(["install"]));
        var relayed = Assert.IsType<InstallCommand.Machine>(InstallCommand.Parse(["INSTALL", @"DESKTOP\dante"]));

        Assert.Null(first.Account);
        Assert.Equal(@"DESKTOP\dante", relayed.Account);
        Assert.True(first.RequiresAdministrator(machineInstallPresent: false));
    }

    [Fact]
    public void UserInstallAsksForNoAdministrator()
    {
        var command = Assert.IsType<InstallCommand.ForUser>(InstallCommand.Parse(["install", "--user"]));

        Assert.False(command.RequiresAdministrator(machineInstallPresent: true));
    }

    [Fact]
    public void UninstallAsksForAnAdministratorOnlyWhenAMachineInstallIsPresent()
    {
        var command = Assert.IsType<InstallCommand.Remove>(InstallCommand.Parse(["uninstall"]));
        var purge = Assert.IsType<InstallCommand.Remove>(InstallCommand.Parse(["uninstall", "--purge"]));

        Assert.False(command.Purge);
        Assert.True(purge.Purge);
        Assert.False(command.RequiresAdministrator(machineInstallPresent: false));
        Assert.True(purge.RequiresAdministrator(machineInstallPresent: true));
    }

    [Fact]
    public void UpdateAsksForNoAdministratorUntilAReplaceNeedsOne()
    {
        var command = Assert.IsType<InstallCommand.Update>(InstallCommand.Parse(["update"]));
        var upper = Assert.IsType<InstallCommand.Update>(InstallCommand.Parse(["UPDATE"]));

        Assert.False(command.RequiresAdministrator(machineInstallPresent: true));
        Assert.False(upper.RequiresAdministrator(machineInstallPresent: false));
        Assert.Null(command.Channel);
        Assert.Null(upper.Channel);
    }

    [Fact]
    public void UpdateCanChooseStableOrDev()
    {
        var dev = Assert.IsType<InstallCommand.Update>(InstallCommand.Parse(["update", "--channel", "dev"]));
        var stable = Assert.IsType<InstallCommand.Update>(InstallCommand.Parse(["UPDATE", "--CHANNEL", "STABLE"]));

        Assert.Equal(UpdateChannel.Dev, dev.Channel);
        Assert.Equal(UpdateChannel.Stable, stable.Channel);
        Assert.False(dev.RequiresAdministrator(machineInstallPresent: true));
    }

    [Fact]
    public void OtherArgumentsAreNotAnInstall()
    {
        Assert.Null(InstallCommand.Parse([]));
        Assert.Null(InstallCommand.Parse(["--config", "winland.conf"]));
    }

    [Fact]
    public void TweaksTurnsOffSnapAndAsksForNoAdministrator()
    {
        var snap = Assert.IsType<InstallCommand.Tweaks>(InstallCommand.Parse(["tweaks", "--off"]));
        var swipes = Assert.IsType<InstallCommand.Tweaks>(InstallCommand.Parse(["TWEAKS", "--OFF", "--touchpad"]));

        Assert.False(snap.Touchpad);
        Assert.True(swipes.Touchpad);
        Assert.False(snap.RequiresAdministrator(machineInstallPresent: true));
        Assert.False(swipes.RequiresAdministrator(machineInstallPresent: false));
    }

    [Theory]
    [InlineData("install", "--purge")]
    [InlineData("uninstall", "--user")]
    [InlineData("install", "--user", "extra")]
    [InlineData("update", "--user")]
    [InlineData("update", "extra")]
    [InlineData("update", "--channel")]
    [InlineData("update", "--channel", "nightly")]
    [InlineData("update", "--channel", "dev", "extra")]
    [InlineData("tweaks")]
    [InlineData("tweaks", "--touchpad")]
    [InlineData("tweaks", "--off", "--user")]
    public void AMisusedVerbIsRefused(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => InstallCommand.Parse(args));
    }
}
