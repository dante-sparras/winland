using Winland;

namespace Winland.Tests;

public class ConfigValueSyntaxTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    [InlineData("on", true)]
    [InlineData("off", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("YES", true)]
    [InlineData("Off", false)]
    public void BoolWordsParse(string text, bool expected)
    {
        Assert.True(ConfigValueSyntax.TryParseBool(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2")]
    [InlineData("trueish")]
    [InlineData("nope")]
    public void OtherBoolTextIsRejected(string text)
    {
        Assert.False(ConfigValueSyntax.TryParseBool(text, out _));
    }

    [Fact]
    public void NamedIntsAcceptTheNameOrANumber()
    {
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["follow_mouse"] = 0,
            ["left"] = 1,
            ["right"] = 2,
        };

        Assert.True(ConfigValueSyntax.TryParseNamedInt("left", names, out var named));
        Assert.Equal(1, named);
        Assert.True(ConfigValueSyntax.TryParseNamedInt("RIGHT", names, out var upper));
        Assert.Equal(2, upper);
        Assert.True(ConfigValueSyntax.TryParseNamedInt("4", names, out var number));
        Assert.Equal(4, number);
        Assert.False(ConfigValueSyntax.TryParseNamedInt("middle", names, out _));
    }

    [Theory]
    [InlineData("10 20", 10, 20)]
    [InlineData("1.5 -2", 1.5, -2)]
    public void Vec2IsTwoNumbersSeparatedByOneSpace(string text, double x, double y)
    {
        Assert.True(ConfigValueSyntax.TryParseVec2(text, out var value));
        Assert.Equal(x, value.X);
        Assert.Equal(y, value.Y);
    }

    [Theory]
    [InlineData("10")]
    [InlineData("1 2 3")]
    [InlineData("1  2")]
    [InlineData("1\t2")]
    public void MalformedVec2IsRejected(string text)
    {
        Assert.False(ConfigValueSyntax.TryParseVec2(text, out _));
    }

    [Fact]
    public void GradientKeepsTheFirstColorAndAnOptionalAngle()
    {
        Assert.True(ConfigValueSyntax.TryParseGradient("rgba(33ccffee)", out var plain));
        Assert.Equal(new BorderColor(0xEE, 0x33, 0xCC, 0xFF), plain.Color);
        Assert.False(plain.HasAngle);
        Assert.Equal(1, plain.Colors);

        Assert.True(ConfigValueSyntax.TryParseGradient("rgba(33ccffee) rgba(00ff99ee) 45deg", out var angled));
        Assert.Equal(new BorderColor(0xEE, 0x33, 0xCC, 0xFF), angled.Color);
        Assert.True(angled.HasAngle);
        Assert.Equal(45, angled.AngleDegrees);
        Assert.Equal(2, angled.Colors);

        Assert.True(ConfigValueSyntax.TryParseGradient("rgba(ff00ffff) 90deg", out var oneAngle));
        Assert.True(oneAngle.HasAngle);
        Assert.Equal(90, oneAngle.AngleDegrees);
        Assert.Equal(1, oneAngle.Colors);
    }

    [Fact]
    public void DwindleBoolAndNamedIntUseTheSharedWords()
    {
        var document = ConfigParser.Parse("""
            dwindle:preserve_split = off
            dwindle:smart_split = 1
            dwindle:force_split = right
            dwindle:split_bias = current
            """);

        var options = LayoutOptions.From(document);

        Assert.False(options.PreserveSplit);
        Assert.True(options.SmartSplit);
        Assert.Equal(2, options.ForceSplit);
        Assert.Equal(1, options.SplitBias);
    }
}
