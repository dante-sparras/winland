using Winland.Install;

namespace Winland.Install.Tests;

public class LogonInstallTests
{
    [Fact]
    public void InstallCreatesTheLogonTaskWhenNoneExists()
    {
        var catalog = new MemoryTaskCatalog();
        var install = new LogonInstall(catalog);

        install.Install(@"C:\Tools\winland.exe", @"DESKTOP\dante");

        var task = catalog.Only();
        Assert.Equal("Winland", task.Name);
        Assert.Equal(@"C:\Tools\winland.exe", task.Executable);
        Assert.Equal(@"DESKTOP\dante", task.User);
        Assert.Equal("create", Assert.Single(catalog.Operations));
    }

    [Fact]
    public void InstallReplacesTheExistingTaskAndLeavesOne()
    {
        var catalog = new MemoryTaskCatalog();
        catalog.Seed(LogonTaskDefinition.Build(@"C:\old\winland.exe", @"DESKTOP\dante"));
        var install = new LogonInstall(catalog);

        install.Install(@"C:\new\winland.exe", @"DESKTOP\dante");

        var task = catalog.Only();
        Assert.Equal(@"C:\new\winland.exe", task.Executable);
        Assert.Equal("replace", Assert.Single(catalog.Operations));
    }

    [Fact]
    public void InstallTwiceLeavesOneTask()
    {
        var catalog = new MemoryTaskCatalog();
        var install = new LogonInstall(catalog);

        install.Install(@"C:\Tools\winland.exe", @"DESKTOP\dante");
        install.Install(@"C:\Tools\winland.exe", @"DESKTOP\dante");

        Assert.Equal(@"C:\Tools\winland.exe", catalog.Only().Executable);
        Assert.Equal(["create", "replace"], catalog.Operations);
    }

    [Fact]
    public void InstallRejectsABlankExecutable()
    {
        var install = new LogonInstall(new MemoryTaskCatalog());

        var error = Assert.Throws<ArgumentException>(() => install.Install(" ", @"DESKTOP\dante"));

        Assert.Equal("executable", error.ParamName);
    }

    [Fact]
    public void InstallRejectsABlankUser()
    {
        var install = new LogonInstall(new MemoryTaskCatalog());

        var error = Assert.Throws<ArgumentException>(() => install.Install(@"C:\Tools\winland.exe", " "));

        Assert.Equal("user", error.ParamName);
    }

    [Fact]
    public void UninstallRemovesTheLogonTask()
    {
        var catalog = new MemoryTaskCatalog();
        catalog.Seed(LogonTaskDefinition.Build(@"C:\Tools\winland.exe", @"DESKTOP\dante"));
        var install = new LogonInstall(catalog);

        install.Uninstall();

        Assert.False(catalog.Contains("Winland"));
        Assert.Equal("delete", Assert.Single(catalog.Operations));
    }

    [Fact]
    public void UninstallWhenTheTaskIsAbsentDoesNothing()
    {
        var catalog = new MemoryTaskCatalog();
        var install = new LogonInstall(catalog);

        install.Uninstall();

        Assert.Empty(catalog.Operations);
    }
}
