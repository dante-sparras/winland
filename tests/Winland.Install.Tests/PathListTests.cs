using Winland.Install;

namespace Winland.Install.Tests;

public class PathListTests
{
    [Fact]
    public void AddAppendsTheFolderAndLeavesTheOtherEntriesUnexpanded()
    {
        var path = PathList.Add(@"%SystemRoot%\system32;C:\Tools", @"C:\Program Files\Winland");

        Assert.Equal(@"%SystemRoot%\system32;C:\Tools;C:\Program Files\Winland", path);
    }

    [Fact]
    public void AddDoesNothingWhenTheFolderIsAlreadyThere()
    {
        const string current = @"C:\Tools;C:\Program Files\Winland\;C:\Other";

        Assert.Null(PathList.Add(current, @"C:\Program Files\Winland"));
    }

    [Fact]
    public void RemoveDropsOnlyThatFolder()
    {
        var path = PathList.Remove(@"C:\Tools;C:\Program Files\Winland;C:\Other", @"C:\Program Files\Winland");

        Assert.Equal(@"C:\Tools;C:\Other", path);
    }

    [Fact]
    public void RemoveDoesNothingWhenTheFolderIsAbsent()
    {
        Assert.Null(PathList.Remove(@"%SystemRoot%\system32", @"C:\Program Files\Winland"));
        Assert.Null(PathList.Remove(null, @"C:\Program Files\Winland"));
    }
}
