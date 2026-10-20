using Winland;
using Winland.Windows;

namespace Winland.Tests;

public class MotionFxTests
{
    private static readonly WindowId First = new(9001);
    private static readonly WindowId Second = new(9002);

    [Fact]
    public void AWindowHasTheEffectsItsDecorationGaveUntilTheyAreTakenAway()
    {
        var effects = new MotionEffects(WobbleSettings.Documented, 7);
        MotionFx.Set(First, effects);

        Assert.Equal(effects, MotionFx.Of(First));
        Assert.Null(MotionFx.Of(Second));

        MotionFx.Set(First, null);

        Assert.Null(MotionFx.Of(First));
    }

    [Fact]
    public void AnEffectThatDrawsNothingIsNotKept()
    {
        MotionFx.Set(First, new MotionEffects(null, 0));

        Assert.Null(MotionFx.Of(First));
    }

    [Fact]
    public void ForgettingOneWindowKeepsTheOthers()
    {
        var effects = new MotionEffects(null, 4);
        MotionFx.Set(First, effects);
        MotionFx.Set(Second, effects);

        MotionFx.Forget(First);

        Assert.Null(MotionFx.Of(First));
        Assert.Equal(effects, MotionFx.Of(Second));

        MotionFx.ForgetAll();

        Assert.Null(MotionFx.Of(Second));
    }
}
