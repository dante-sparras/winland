using Winland.Windows;

namespace Winland.Tests;

public class BarBackdropTests
{
    [Fact]
    public void TheSystemBackdropIsTriedFirstAndTheAccentIsLeftAlone()
    {
        var surface = new Surface();
        var backdrop = new BarBackdrop(surface);

        backdrop.Set(true);

        Assert.True(backdrop.Blurred);
        Assert.Equal([BarBackdrop.BlurBackdrop], surface.Systems);
        Assert.Empty(surface.Accents);
    }

    [Fact]
    public void AWindowThatRefusesTheSystemBackdropGetsTheAcrylicAccent()
    {
        var surface = new Surface { SystemWorks = false };
        var backdrop = new BarBackdrop(surface);

        backdrop.Set(true);

        Assert.True(backdrop.Blurred);
        Assert.Equal([true], surface.Accents);

        backdrop.Set(false);

        Assert.False(backdrop.Blurred);
        Assert.Equal([true, false], surface.Accents);
    }

    [Fact]
    public void AWindowThatRefusesBothStaysAsItIsAndIsNotAskedAgain()
    {
        var surface = new Surface { SystemWorks = false, AccentWorks = false };
        var backdrop = new BarBackdrop(surface);

        backdrop.Set(true);
        backdrop.Set(true);

        Assert.False(backdrop.Blurred);
        Assert.Single(surface.Systems);
        Assert.Single(surface.Accents);

        backdrop.Set(false);

        Assert.Single(surface.Systems);
        Assert.Single(surface.Accents);
    }

    [Fact]
    public void TurningTheBlurOffPutsTheBackdropBackAndAskingTwiceDoesNothing()
    {
        var surface = new Surface();
        var backdrop = new BarBackdrop(surface);
        backdrop.Set(true);
        backdrop.Set(true);

        backdrop.Set(false);
        backdrop.Set(false);

        Assert.False(backdrop.Blurred);
        Assert.Equal([BarBackdrop.BlurBackdrop, BarBackdrop.NoBackdrop], surface.Systems);
    }

    private sealed class Surface : IBackdropSurface
    {
        public bool SystemWorks { get; init; } = true;

        public bool AccentWorks { get; init; } = true;

        public List<uint> Systems { get; } = [];

        public List<bool> Accents { get; } = [];

        public bool TrySetSystemBackdrop(uint type)
        {
            Systems.Add(type);
            return SystemWorks;
        }

        public bool TrySetAccent(bool acrylic)
        {
            Accents.Add(acrylic);
            return AccentWorks;
        }
    }
}
