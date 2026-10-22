namespace Winland.Domain.Tests;

public class TrayOpenerTests
{
    [Fact]
    public void NotepadOpensAFileWhoseTypeHasNoCommand()
    {
        var launch = TrayOpener.Open(@"C:\notes\winland.conf", null);

        Assert.Equal("notepad.exe \"C:\\notes\\winland.conf\"", launch.CommandLine);
        Assert.Null(launch.WorkingDirectory);
    }

    [Fact]
    public void ABlankAssociationAlsoUsesNotepad()
    {
        Assert.Equal("notepad.exe \"C:\\notes\\winland.conf\"", TrayOpener.Command(@"C:\notes\winland.conf", "  "));
    }

    [Fact]
    public void AnUnquotedPlaceholderIsQuoted()
    {
        var command = TrayOpener.Command(
            @"C:\Program Files\winland.conf",
            @"C:\Tools\editor.exe %1");

        Assert.Equal("C:\\Tools\\editor.exe \"C:\\Program Files\\winland.conf\"", command);
    }

    [Fact]
    public void AQuotedPlaceholderKeepsThePathRaw()
    {
        var command = TrayOpener.Command(
            @"C:\Program Files\winland.conf",
            "\"C:\\Program Files\\Editor\\editor.exe\" \"%1\"");

        Assert.Equal(
            "\"C:\\Program Files\\Editor\\editor.exe\" \"C:\\Program Files\\winland.conf\"",
            command);
    }

    [Theory]
    [InlineData("%L")]
    [InlineData("%l")]
    [InlineData("%*")]
    public void TheOtherPlaceholdersAreFilled(string token)
    {
        Assert.Equal("editor.exe \"C:\\a.conf\"", TrayOpener.Command(@"C:\a.conf", "editor.exe " + token));
    }

    [Fact]
    public void ACommandWithoutAPlaceholderGainsThePath()
    {
        Assert.Equal("editor.exe \"C:\\a.conf\"", TrayOpener.Command(@"C:\a.conf", "editor.exe"));
    }

    [Fact]
    public void QuotesInThePathAreLeftOut()
    {
        Assert.Equal("notepad.exe \"C:\\ab.conf\"", TrayOpener.Command("C:\\a\"b.conf", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankPathIsRejected(string path)
    {
        Assert.Throws<ArgumentException>(() => TrayOpener.Open(path, null));
        Assert.Throws<ArgumentException>(() => TrayOpener.Command(path, "editor.exe %1"));
    }
}
