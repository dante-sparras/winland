using System.Security.Cryptography;
using Winland.Install;

namespace Winland.Install.Tests;

public class ReleaseChecksumTests
{
    private static readonly byte[] Archive = "zip-bytes"u8.ToArray();

    [Fact]
    public void ThePublishedLineMatchesTheZip()
    {
        var sums = Hash(Archive).ToLowerInvariant() + "  Winland-win-x64.zip\n"
            + new string('b', 64) + "  version.json\n";

        Assert.True(ReleaseChecksum.Matches(Archive, sums));
    }

    [Fact]
    public void AnUppercaseHashAndACarriageReturnStillMatch()
    {
        Assert.True(ReleaseChecksum.Matches(Archive, Hash(Archive) + "  Winland-win-x64.zip\r\n"));
    }

    [Theory]
    [InlineData("  ")]
    [InlineData(" ")]
    [InlineData(" *")]
    [InlineData("  *")]
    public void ASha256SumSeparatorIsAccepted(string separator)
    {
        Assert.True(ReleaseChecksum.Matches(Archive, Hash(Archive).ToLowerInvariant() + separator + "Winland-win-x64.zip\n"));
    }

    [Fact]
    public void ADifferentHashIsRefused()
    {
        Assert.False(ReleaseChecksum.Matches(Archive, new string('a', 64) + "  Winland-win-x64.zip\n"));
    }

    [Fact]
    public void ALineForAnotherFileIsRefused()
    {
        Assert.False(ReleaseChecksum.Matches(Archive, Hash(Archive) + "  version.json\n"));
        Assert.False(ReleaseChecksum.Matches(Archive, Hash(Archive) + "  Winland-win-x64.zip.extra\n"));
    }

    [Fact]
    public void TheSameVersionTextIsCurrent()
    {
        Assert.True(ReleaseVersion.IsCurrent("0.1.0", "0.1.0"));
        Assert.False(ReleaseVersion.IsCurrent("0.1.0", "0.1.1"));
        Assert.False(ReleaseVersion.IsCurrent("0.0.0-dev", "0.1.0"));
        Assert.False(ReleaseVersion.IsCurrent("0.1.0", "0.1.0 "));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
