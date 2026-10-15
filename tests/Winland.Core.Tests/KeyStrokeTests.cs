using Winland;

namespace Winland.Tests;

public class KeyStrokeTests
{
    [Theory]
    [InlineData("F10", 0x79, false)]
    [InlineData("Return", 0x0D, false)]
    [InlineData("c", 0x43, false)]
    [InlineData("C", 0x43, false)]
    [InlineData("Right", 0x27, true)]
    public void ANameResolvesToItsVirtualKey(string key, int virtualKey, bool extended)
    {
        Assert.True(KeyStrokes.TryResolve(key, 0, KeyLayout.UnitedStates, out var stroke));

        Assert.Equal(virtualKey, stroke.VirtualKey);
        Assert.Equal(extended, stroke.Extended);
        Assert.False(stroke.IsMouse);
    }

    [Theory]
    [InlineData("code:38", 0x1E, false)]
    [InlineData("code:113", 0x4B, true)]
    [InlineData("code:133", 0x5B, true)]
    public void AKeycodeResolvesToItsScanCode(string key, int scan, bool extended)
    {
        Assert.True(KeyStrokes.TryResolve(key, 0, KeyLayout.UnitedStates, out var stroke));

        Assert.Equal(scan, stroke.Scan);
        Assert.Equal(extended, stroke.Extended);
    }

    [Fact]
    public void TheCodeIsUsedWhenTheNameDoesNotResolve()
    {
        Assert.True(KeyStrokes.TryResolve("NoSuchKey", 38, KeyLayout.UnitedStates, out var stroke));

        Assert.Equal(0x1E, stroke.Scan);
    }

    [Theory]
    [InlineData("mouse:272")]
    [InlineData("mouse:276")]
    public void AMouseButtonFrom272To276Resolves(string key)
    {
        Assert.True(KeyStrokes.TryResolve(key, 0, KeyLayout.UnitedStates, out var stroke));

        Assert.True(stroke.IsMouse);
    }

    [Theory]
    [InlineData("mouse:271")]
    [InlineData("mouse:277")]
    [InlineData("NoSuchKey")]
    [InlineData("")]
    public void AnythingElseDoesNotResolve(string key)
    {
        Assert.False(KeyStrokes.TryResolve(key, 0, KeyLayout.UnitedStates, out _));
    }

    [Theory]
    [InlineData(0x41, 0x1E, false)]
    [InlineData(0x25, 0x4B, true)]
    [InlineData(0x5B, 0x5B, true)]
    [InlineData(0x13, 0x45, false)]
    [InlineData(0x0D, 0x1C, true)]
    public void TheScanForAKeycodeIsTheInverseOfTheKeycode(int virtualKey, int scan, bool extended)
    {
        var code = KeyNames.XkbCode(virtualKey, scan, extended);

        Assert.True(KeyNames.TryScan(code, out var back, out var backExtended, out _));
        Assert.Equal(scan, back);
        Assert.Equal(extended, backExtended);
    }
}
