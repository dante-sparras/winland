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
    public void OtherArgumentsAreNotAnInstall()
    {
        Assert.Null(InstallCommand.Parse([]));
        Assert.Null(InstallCommand.Parse(["--config", "winland.conf"]));
    }

    [Theory]
    [InlineData("install", "--purge")]
    [InlineData("uninstall", "--user")]
    [InlineData("install", "--user", "extra")]
    public void AMisusedVerbIsRefused(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => InstallCommand.Parse(args));
    }
}
