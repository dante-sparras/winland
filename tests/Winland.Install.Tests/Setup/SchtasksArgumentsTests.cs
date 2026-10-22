namespace Winland.Install.Tests;

public class SchtasksArgumentsTests
{
    [Fact]
    public void CreateRegistersTheTaskXmlWithoutReplacing()
    {
        Assert.Equal(
            ["/Create", "/TN", "Winland", "/XML", @"C:\temp\winland.xml"],
            SchtasksArguments.Create("Winland", @"C:\temp\winland.xml"));
    }

    [Fact]
    public void UpdateForcesTheSameTaskXmlSoASecondInstallReplacesIt()
    {
        Assert.Equal(
            ["/Create", "/TN", "Winland", "/XML", @"C:\temp\winland.xml", "/F"],
            SchtasksArguments.Update("Winland", @"C:\temp\winland.xml"));
    }

    [Fact]
    public void DeleteRemovesTheTaskWithoutAPrompt()
    {
        Assert.Equal(["/Delete", "/TN", "Winland", "/F"], SchtasksArguments.Delete("Winland"));
    }

    [Fact]
    public void QueryLooksUpTheTaskByName()
    {
        Assert.Equal(["/Query", "/TN", "Winland"], SchtasksArguments.Query("Winland"));
    }
}
