namespace Winland.Install.Tests;

public class InstallLocationChoiceTests
{
    [Fact]
    public void ThePerUserCopyIsRecognizedWhateverItsCase()
    {
        var directory = @"C:\Users\me\AppData\Local\Programs\Winland";

        Assert.True(InstallLocations.IsPerUserInstall(Path.Combine(directory, "winland.exe"), directory));
        Assert.True(InstallLocations.IsPerUserInstall(Path.Combine(directory.ToLowerInvariant(), "winland.exe"), directory + @"\"));
    }

    [Fact]
    public void AnotherFolderIsNotThePerUserCopy()
    {
        Assert.False(InstallLocations.IsPerUserInstall(
            @"C:\Program Files\Winland\winland.exe",
            @"C:\Users\me\AppData\Local\Programs\Winland"));
        Assert.False(InstallLocations.IsPerUserInstall(null, @"C:\Users\me\AppData\Local\Programs\Winland"));
    }

    [Fact]
    public void ConfigAndStateShareTheDotWinlandFolder()
    {
        var profile = @"C:\Users\me";

        Assert.Equal(@"C:\Users\me\.winland", InstallLocations.Config(profile));
        Assert.Equal(InstallLocations.Config(profile), InstallLocations.State(profile));
    }

    [Fact]
    public void TheInstalledProgramFoldersStayWhereTheyAre()
    {
        Assert.Equal(@"C:\Program Files\Winland", InstallLocations.Machine(@"C:\Program Files"));
        Assert.Equal(
            @"C:\Users\me\AppData\Local\Programs\Winland",
            InstallLocations.User(@"C:\Users\me\AppData\Local"));
    }

    [Fact]
    public void TheRunningFolderSelectsTheInstall()
    {
        var machine = @"C:\Program Files\Winland";
        var user = @"C:\Users\me\AppData\Local\Programs\Winland";

        Assert.Equal(InstallKind.Machine, InstalledCopy.Of(Path.Combine(machine, "winland.exe"), machine, user));
        Assert.Equal(InstallKind.Machine, InstalledCopy.Of(Path.Combine(machine.ToLowerInvariant(), "winland.exe"), machine + @"\", user));
        Assert.Equal(InstallKind.User, InstalledCopy.Of(Path.Combine(user, "winland.exe"), machine, user + @"\"));
        Assert.Equal(InstallKind.None, InstalledCopy.Of(@"C:\Dev\winland\winland.exe", machine, user));
    }
}
