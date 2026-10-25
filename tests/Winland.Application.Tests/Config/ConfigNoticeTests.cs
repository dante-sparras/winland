namespace Winland.Application.Tests;

public class ConfigNoticeTests
{
    private const string Path = @"C:\winland\winland.conf";

    [Fact]
    public void OneErrorNamesTheFileAndTheLine()
    {
        var notice = Load("blur = on\n");

        Assert.NotNull(notice);
        Assert.Equal("Winland: 1 config errors", notice.Title);
        Assert.Equal(@"C:\winland\winland.conf:1: Unsupported keyword 'blur'.", notice.Text);
        Assert.Equal(Path, notice.File);
        Assert.True(notice.Generation > 0);
    }

    [Fact]
    public void TwoErrorsNameTheFirstAndALaterLoadReplacesTheNotice()
    {
        var core = Loaded("blur = on\n");
        var first = Notice(core);

        core.Handle(new Configure(ConfigParser.Parse("ghostkeyword = 1\nblur = on\n", path: Path)));
        var second = Notice(core);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("Winland: 2 config errors", second.Title);
        Assert.Equal(@"C:\winland\winland.conf:1: Unsupported keyword 'ghostkeyword'.", second.Text);
        Assert.True(second.Generation > first.Generation);
        Assert.DoesNotContain("blur", second.Text);
    }

    [Fact]
    public void TheSameErrorsAreShownAgain()
    {
        var core = Loaded("blur = on\n");
        var first = Notice(core);

        core.Handle(new Configure(ConfigParser.Parse("blur = on\n", path: Path)));
        var second = Notice(core);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Text, second.Text);
        Assert.NotEqual(first.Generation, second.Generation);
    }

    [Fact]
    public void ACleanLoadTakesTheNoticeAway()
    {
        var core = Loaded("blur = on\n");

        core.Handle(new Configure(ConfigParser.Parse("general:gaps_in = 4\n", path: Path)));

        Assert.Null(Notice(core));
    }



    [Fact]
    public void ASourcedErrorNamesThatFile()
    {
        var source = new MemorySource(new Dictionary<string, string> { ["extra.conf"] = "blur = on\n" });
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("source = extra.conf\n", source, path: Path)));
        var notice = Notice(core);

        Assert.NotNull(notice);
        Assert.Equal("Winland: 1 config errors", notice.Title);
        Assert.Equal("extra.conf:1: Unsupported keyword 'blur'.", notice.Text);
        Assert.Equal("extra.conf", notice.File);
    }

    [Fact]
    public void ALiveKeywordLeavesTheNotice()
    {
        var core = Loaded("blur = on\n");
        var before = Notice(core);

        Assert.Equal("ok", core.Execute("keyword general:gaps_in 8").Text);
        Assert.Equal(before, Notice(core));
    }

    [Fact]
    public void AFailedReloadLeavesTheNotice()
    {
        var core = Loaded("blur = on\n");
        var before = Notice(core);
        core.ReadConfig = () => new ConfigRead(null, new ConfigWarning(0, "Could not read config.", null));

        Assert.StartsWith("error:", core.Execute("reload").Text, StringComparison.Ordinal);
        Assert.Equal(before, Notice(core));
    }

    [Fact]
    public void ANoticeWithoutAFileOrALineStillSaysWhatItCan()
    {
        var noFile = ConfigNotice.For([new ConfigWarning(3, "Unsupported keyword 'blur'.")], null, 1);
        var noLine = ConfigNotice.For([new ConfigWarning(0, "Could not read config.")], Path, 2);
        var bare = ConfigNotice.For([new ConfigWarning(0, "Could not read config.")], null, 3);

        Assert.Equal("line 3: Unsupported keyword 'blur'.", noFile!.Text);
        Assert.Null(noFile.File);
        Assert.Equal(@"C:\winland\winland.conf: Could not read config.", noLine!.Text);
        Assert.Equal(Path, noLine.File);
        Assert.Equal("Could not read config.", bare!.Text);
        Assert.Null(bare.File);
    }

    [Fact]
    public void SetErrorIsNotARequest() =>
        Assert.Equal("error: unknown request", new WindowManager().Execute("seterror rgb(ff0000) broke").Text);

    private static ConfigNotice? Load(string text) => Notice(Loaded(text));

    private static WindowManager Loaded(string text)
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(text, path: Path)));
        return core;
    }

    private static ConfigNotice? Notice(WindowManager core) => core.Execute("dispatch resizewindow").Effect.ConfigNotice;
}
