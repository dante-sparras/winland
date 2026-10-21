using Winland.Install;

namespace Winland.Install.Tests;

public class UpdateChannelTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n")]
    [InlineData("stable")]
    [InlineData("STABLE\n")]
    [InlineData("\uFEFFstable\n")]
    public void AMissingOrStableFileUsesStable(string? text)
    {
        Assert.Equal(UpdateChannel.Stable, UpdateChannelFile.Read(text));
        Assert.Equal(UpdateChannel.Stable, UpdateChannelFile.Choose(null, text));
    }

    [Fact]
    public void ASavedDevChannelIsUsedWhenTheCommandDoesNotChoose()
    {
        Assert.Equal(UpdateChannel.Dev, UpdateChannelFile.Read("dev\n"));
        Assert.Equal(UpdateChannel.Dev, UpdateChannelFile.Choose(null, " DEV "));
    }

    [Fact]
    public void AnExplicitChannelWinsOverTheSavedOne()
    {
        Assert.Equal(UpdateChannel.Stable, UpdateChannelFile.Choose(UpdateChannel.Stable, "dev\n"));
        Assert.Equal(UpdateChannel.Dev, UpdateChannelFile.Choose(UpdateChannel.Dev, "stable\n"));
    }

    [Fact]
    public void TheSavedWordRoundTripsAndLivesUnderTheWinlandFolder()
    {
        Assert.Equal(UpdateChannel.Dev, UpdateChannelFile.Read(UpdateChannelFile.Text(UpdateChannel.Dev)));
        Assert.Equal(UpdateChannel.Stable, UpdateChannelFile.Read(UpdateChannelFile.Text(UpdateChannel.Stable)));
        Assert.Equal("dev\n", UpdateChannelFile.Text(UpdateChannel.Dev));
        Assert.Equal(
            Path.Combine(@"C:\Users\me", ".winland", "channel"),
            UpdateChannelFile.PathFor(@"C:\Users\me"));
    }

    [Fact]
    public void AnythingOtherThanTheTwoChannelsIsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => UpdateChannelFile.Read("nightly"));

        Assert.Contains("not stable or dev", error.Message, StringComparison.Ordinal);
    }
}
