using Winland.Install;

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
}
