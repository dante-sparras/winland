namespace Winland.Infrastructure.Tests;

public class CachedSettingTests
{
    [Fact]
    public void TheSystemIsAskedOnceNoMatterHowOftenTheValueIsRead()
    {
        var reads = 0;
        var setting = new CachedSetting(() =>
        {
            reads++;
            return true;
        });

        Assert.True(setting.Value);
        Assert.True(setting.Value);
        Assert.True(setting.Value);

        Assert.Equal(1, reads);
    }

    [Fact]
    public void AForgottenSettingIsReadAgainAndGivesTheNewValue()
    {
        var current = false;
        var reads = 0;
        var setting = new CachedSetting(() =>
        {
            reads++;
            return current;
        });
        Assert.False(setting.Value);

        current = true;
        Assert.False(setting.Value);
        setting.Forget();

        Assert.True(setting.Value);
        Assert.Equal(2, reads);
    }

    [Fact]
    public void ForgettingASettingNeverReadLeavesItUnread()
    {
        var reads = 0;
        var setting = new CachedSetting(() =>
        {
            reads++;
            return false;
        });

        setting.Forget();

        Assert.Equal(0, reads);
    }
}
