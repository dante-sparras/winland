namespace Winland.Application.Tests;

public class AnimationTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);
    private static readonly Rectangle Left = new(0, 0, 960, 1080);
    private static readonly Rectangle Right = new(960, 0, 960, 1080);
    private static readonly Timing Default = new(Bezier.Default, 800);

    [Fact]
    public void AnOpenedWindowPopsInAndFadesIn()
    {
        var core = new WindowManager();

        var outcome = Appear(core, 1);

        Assert.Equal(Area, BoundsOf(outcome, 1));
        var motion = Assert.Single(outcome.Motions);
        Assert.Equal(new WindowId(1), motion.Window);
        Assert.Equal(Area, motion.To);
        Assert.Equal(new Rectangle(957, 537, 5, 5), motion.From);
        Assert.True(motion.Warp);
        Assert.Equal(Default, motion.Move);
        Assert.Equal(0, motion.FadeFrom);
        Assert.Equal(1, motion.FadeTo);
        Assert.Equal(Default, motion.Fade);
        Assert.Equal(MotionEnd.None, motion.End);
        Assert.Equal(Area, motion.Rest);
    }

    [Fact]
    public void TheOpenStyleAndCurveComeFromTheConfig()
    {
        var core = Load("""
            bezier = quick, 0.1, 1, 0.1, 1
            animation = windowsIn, 1, 3, quick, popin 50%
            animation = fadeIn, 0
            """);

        var motion = Assert.Single(Appear(core, 1).Motions);

        Assert.Equal(new Rectangle(480, 270, 960, 540), motion.From);
        Assert.Equal(new Timing(new Bezier(0.1, 1, 0.1, 1), 300), motion.Move);
        Assert.Null(motion.Fade);
        Assert.Null(motion.FadeFrom);
    }

    [Fact]
    public void ASlidingWindowEntersFromTheNearestMonitorEdge()
    {
        var core = Load("animation = windows, 1, 4, default, slide");
        Appear(core, 1);

        var opened = Appear(core, 2);

        var motion = Assert.Single(opened.Motions, candidate => candidate.Window == new WindowId(2));
        Assert.Equal(Right with { X = 1920 }, motion.From);
        Assert.Equal(Right, motion.To);
    }

    [Fact]
    public void AWindowThatMakesRoomMovesFromItsOldRectangle()
    {
        var core = new WindowManager();
        Appear(core, 1);

        var outcome = Appear(core, 2);

        Assert.Equal(Left, BoundsOf(outcome, 1));
        var moved = Assert.Single(outcome.Motions, motion => motion.Window == new WindowId(1));
        Assert.Equal(Left, moved.To);
        Assert.Equal(Area, moved.From);
        Assert.False(moved.Warp);
        Assert.Equal(Default, moved.Move);
        Assert.Null(moved.Fade);
        Assert.Contains(outcome.Motions, motion => motion.Window == new WindowId(2) && motion.FadeFrom == 0);
    }

    [Fact]
    public void ResizingADwindleTileAnimatesItAndItsNeighbour()
    {
        var core = Load("");
        Appear(core, 1);
        Appear(core, 2);

        var resized = core.Handle(new Dispatch("resizeactive", "-100 0"));

        Assert.Equal(2, resized.Motions.Count(motion => !motion.Warp && motion.Move is not null));
    }

    [Fact]
    public void ASwitchToAHigherWorkspaceSlidesTheOldOneOutToTheLeft()
    {
        var core = Load("""
            bind = SUPER, 1, workspace, 1
            bind = SUPER, 2, workspace, 2
            """);
        Appear(core, 1);
        Appear(core, 2);
        HoldSuper(core);

        var outcome = Tap(core, "2");

        Assert.Equal([new WindowId(1), new WindowId(2)], outcome.Hide);
        var first = Assert.Single(outcome.Motions, motion => motion.Window == new WindowId(1));
        Assert.Equal(Left with { X = -1920 }, first.To);
        Assert.Equal(Left, first.Rest);
        Assert.Equal(MotionEnd.Hide, first.End);
        Assert.Equal(Default, first.Move);
        Assert.Null(first.Fade);
        var second = Assert.Single(outcome.Motions, motion => motion.Window == new WindowId(2));
        Assert.Equal(Right with { X = 960 - 1920 }, second.To);
        Assert.Equal(Right, second.Rest);
    }

    [Fact]
    public void AWindowSlidingOutKeepsItsBorderUntilItHides()
    {
        var core = Load("""
            general:border_size = 2
            bind = SUPER, 1, workspace, 1
            bind = SUPER, 2, workspace, 2
            """);
        Appear(core, 1);
        HoldSuper(core);

        var away = Tap(core, "2");

        Assert.Equal(MotionEnd.Hide, Assert.Single(away.Motions).End);
        Assert.NotNull(Assert.Single(away.Decorations).Ring);
    }

    [Fact]
    public void AWindowHiddenWithoutMotionLosesItsBorderAtOnce()
    {
        var core = Load("""
            general:border_size = 2
            animations:enabled = false
            bind = SUPER, 2, workspace, 2
            """);
        Appear(core, 1);
        HoldSuper(core);

        Assert.Null(Assert.Single(Tap(core, "2").Decorations).Ring);
    }

    [Fact]
    public void AClientGoingIntoFullscreenMovesFromItsTileToTheMonitor()
    {
        var core = Load("");
        Appear(core, 1);
        Appear(core, 2);

        var into = core.Handle(new Dispatch("fullscreen", "0"));
        var grown = Assert.Single(into.Motions, motion => motion.Window == new WindowId(2));
        Assert.Equal(Right, grown.From);
        Assert.Equal(Area, grown.To);
        Assert.Equal(Default, grown.Move);
    }

    [Fact]
    public void AClientLeavingFullscreenMovesBackToItsTile()
    {
        var core = Load("");
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new Dispatch("fullscreen", "0"));

        var back = core.Handle(new Dispatch("fullscreen", "0"));

        var shrunk = Assert.Single(back.Motions, motion => motion.Window == new WindowId(2));
        Assert.Equal(Area, shrunk.From);
        Assert.Equal(Right, shrunk.To);
    }

    [Fact]
    public void ASwitchBackSlidesInFromTheLeftAndKeepsTheLayoutRectangles()
    {
        var core = Load("""
            bind = SUPER, 1, workspace, 1
            bind = SUPER, 2, workspace, 2
            """);
        Appear(core, 1);
        Appear(core, 2);
        HoldSuper(core);
        Tap(core, "2");
        Appear(core, 3);

        var back = Tap(core, "1");

        Assert.Equal(Left, BoundsOf(back, 1));
        Assert.Equal([new WindowId(1), new WindowId(2)], back.Show);
        var entering = Assert.Single(back.Motions, motion => motion.Window == new WindowId(1));
        Assert.Equal(Left with { X = -1920 }, entering.From);
        Assert.False(entering.Warp);
        Assert.Equal(Left, entering.To);
        Assert.Equal(MotionEnd.None, entering.End);
        var leaving = Assert.Single(back.Motions, motion => motion.Window == new WindowId(3));
        Assert.Equal(Area with { X = 1920 }, leaving.To);
        Assert.Equal(Area, leaving.Rest);
        Assert.Equal(MotionEnd.Hide, leaving.End);
    }

    [Fact]
    public void AFadingWorkspaceStaysInPlace()
    {
        var core = Load("""
            animation = workspaces, 1, 4, linear, fade
            bind = SUPER, 2, workspace, 2
            """);
        Appear(core, 1);
        HoldSuper(core);

        var motion = Assert.Single(Tap(core, "2").Motions);

        Assert.Equal(Area, motion.To);
        Assert.Null(motion.Move);
        Assert.Equal(0, motion.FadeTo);
        Assert.Equal(new Timing(Bezier.Linear, 400), motion.Fade);
        Assert.Equal(MotionEnd.Hide, motion.End);
    }

    [Fact]
    public void ASlideFadeTravelsItsPercentage()
    {
        var core = Load("""
            animation = workspaces, 1, 4, default, slidefade 20%
            bind = SUPER, 2, workspace, 2
            """);
        Appear(core, 1);
        HoldSuper(core);

        var motion = Assert.Single(Tap(core, "2").Motions);

        Assert.Equal(Area with { X = -384 }, motion.To);
        Assert.Equal(0, motion.FadeTo);
    }

    [Fact]
    public void AVerticalSlideTravelsTheMonitorHeight()
    {
        var core = Load("""
            animation = workspaces, 1, 4, default, slidevert
            bind = SUPER, 2, workspace, 2
            """);
        Appear(core, 1);
        HoldSuper(core);

        var motion = Assert.Single(Tap(core, "2").Motions);

        Assert.Equal(Area with { Y = -1080 }, motion.To);
    }

    [Fact]
    public void AWindowsOwnAnimationStyleReplacesTheOpenStyle()
    {
        var core = Load("windowrule = animation slide left, class:.*\n");

        var motion = Assert.Single(Appear(core, 1).Motions);

        Assert.Equal(Area with { X = -1920 }, motion.From);
        Assert.Equal(Default, motion.Move);
    }

    [Fact]
    public void ASetAnimationStyleReplacesTheCloseStyleAndUnsetGivesItBack()
    {
        var core = Load("");
        Appear(core, 1);
        core.Execute("setprop address:0x1 animation popin 50%");

        Assert.Equal(new Rectangle(480, 270, 960, 540), Assert.Single(core.Handle(new Dispatch("killactive", "")).Motions).To);

        core.Execute("setprop address:0x1 animation unset");
        Assert.Equal(new Rectangle(957, 537, 5, 5), Assert.Single(core.Handle(new Dispatch("killactive", "")).Motions).To);
    }

    [Fact]
    public void ClosingAWindowShrinksAndFadesItBeforeItIsAskedToClose()
    {
        var core = new WindowManager();
        Appear(core, 1);

        var outcome = core.Handle(new Dispatch("killactive", ""));

        Assert.Equal(new WindowId(1), outcome.Close);
        var motion = Assert.Single(outcome.Motions);
        Assert.Equal(MotionEnd.Close, motion.End);
        Assert.Equal(new Rectangle(957, 537, 5, 5), motion.To);
        Assert.Equal(Area, motion.Rest);
        Assert.Equal(Area, motion.From);
        Assert.False(motion.Warp);
        Assert.Equal(0, motion.FadeTo);
        Assert.Null(motion.FadeFrom);
    }

    [Fact]
    public void AnimationsOffPlansNoMotion()
    {
        var core = Load("""
            general:border_size = 2
            animations:enabled = false
            bind = SUPER, 2, workspace, 2
            """);
        Assert.Empty(Appear(core, 1).Motions);
        Assert.Empty(Appear(core, 2).Motions);
        HoldSuper(core);

        var outcome = Tap(core, "2");

        Assert.Equal(2, outcome.Hide.Count);
        Assert.Empty(outcome.Motions);
        Assert.Empty(core.Handle(new Dispatch("killactive", "")).Motions);
    }

    [Fact]
    public void ADisabledNodeAnimatesNothingButItsSiblingsStillDo()
    {
        var core = Load("""
            animation = windowsMove, 0
            """);
        Appear(core, 1);

        var outcome = Appear(core, 2);

        Assert.DoesNotContain(outcome.Motions, motion => motion.Window == new WindowId(1) && motion.Move is not null);
        Assert.Contains(outcome.Motions, motion => motion.Window == new WindowId(2));
    }

    [Fact]
    public void ReducedMotionPlansNoMotion()
    {
        var core = new WindowManager { ReducedMotion = () => true };

        Assert.Empty(Appear(core, 1).Motions);
        Assert.Empty(Appear(core, 2).Motions);
    }

    [Fact]
    public void ReloadingWithAnimationsOffStopsThem()
    {
        var core = new WindowManager();
        Appear(core, 1);

        core.Handle(new Configure(ConfigParser.Parse("animations:enabled = no")));

        Assert.Empty(Appear(core, 2).Motions);
    }

    [Fact]
    public void FocusMovesTheAlphaAndBorderOfBothClients()
    {
        var core = Load("""
            decoration:inactive_opacity = 0.5
            animation = fadeSwitch, 1, 2, linear
            animation = border, 1, 3, default
            """);
        Appear(core, 1);
        Appear(core, 2);

        var outcome = core.Handle(new Dispatch("movefocus", "l"));

        Assert.Equal(new WindowId(1), outcome.Focus);
        var focused = Assert.Single(outcome.Motions, motion => motion.Window == new WindowId(1));
        var left = Assert.Single(outcome.Motions, motion => motion.Window == new WindowId(2));
        Assert.Equal((byte)255, focused.AlphaTo);
        Assert.True(focused.AlphaFrom < 255);
        Assert.Equal(new Timing(Bezier.Linear, 200), focused.Alpha);
        Assert.Equal(left.AlphaFrom, focused.AlphaTo);
        Assert.Equal(focused.AlphaFrom, left.AlphaTo);
        Assert.Equal(DecorationOptions.Documented.InactiveBorder, focused.BorderFrom);
        Assert.Equal(DecorationOptions.Documented.ActiveBorder, focused.BorderTo);
        Assert.Equal(new Timing(Bezier.Default, 300), focused.Border);
        Assert.Null(focused.Move);
        Assert.Equal(Left, focused.To);
    }

    [Fact]
    public void ADraggedWindowMovesAtOnce()
    {
        var core = Load("bindm = SUPER, mouse:272, movewindow");
        Appear(core, 1);
        var floated = core.Handle(new Dispatch("togglefloating"));
        var bounds = Assert.Single(floated.Placements).Bounds;
        var grab = new Point(bounds.X + 10, bounds.Y + 20);
        HoldSuper(core);
        core.Handle(new PointerButtonPressed("mouse:272") { Position = grab });

        var moved = core.Handle(new PointerMoved(new Point(grab.X + 50, grab.Y + 30)));

        Assert.NotEmpty(moved.Placements);
        Assert.DoesNotContain(moved.Motions, motion => motion.Move is not null);
    }

    [Fact]
    public void TheTileThatMakesRoomForALiftedTileAnimatesWhileTheLiftedOneFollowsThePointer()
    {
        var core = Load("bindm = SUPER, mouse:272, movewindow");
        Appear(core, 1);
        Appear(core, 2);
        HoldSuper(core);
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1400, 500) });

        var lifted = core.Handle(new PointerMoved(new Point(1450, 520)));

        Assert.True(lifted.Live);
        var grown = Assert.Single(lifted.Motions, motion => motion.Move is not null);
        Assert.Equal(new WindowId(1), grown.Window);
        Assert.Equal(Left, grown.From);
        Assert.Equal(Area, grown.To);
        Assert.DoesNotContain(core.Handle(new PointerMoved(new Point(1500, 540))).Motions, motion => motion.Move is not null);
    }

    [Fact]
    public void AMouseResizeOfATileMovesItsNeighborAtOnce()
    {
        var core = Load("bindm = SUPER, mouse:273, resizewindow");
        Appear(core, 1);
        Appear(core, 2);
        HoldSuper(core);
        core.Handle(new PointerButtonPressed("mouse:273") { Position = new Point(1000, 500) });

        var resized = core.Handle(new PointerMoved(new Point(1100, 500)));

        Assert.NotEmpty(resized.Placements);
        Assert.DoesNotContain(resized.Motions, motion => motion.Move is not null);
    }

    [Fact]
    public void AnInterruptedSwitchLandsOnTheFinalRectangles()
    {
        var core = Load("""
            bind = SUPER, 1, workspace, 1
            bind = SUPER, 2, workspace, 2
            """);
        Appear(core, 1);
        Appear(core, 2);
        HoldSuper(core);
        var animator = new Animator();
        Play(animator, Tap(core, "2"), now: 0);
        animator.Step(400);

        Play(animator, Tap(core, "1"), now: 400);
        var landed = animator.Step(400 + Default.Milliseconds);

        Assert.All(landed, frame => Assert.True(frame.Done));
        Assert.Equal(Left, Assert.Single(landed, frame => frame.Window == new WindowId(1)).Bounds);
        Assert.Equal(Right, Assert.Single(landed, frame => frame.Window == new WindowId(2)).Bounds);
        Assert.All(landed, frame => Assert.Equal(MotionEnd.None, frame.End));
        Assert.Equal(0, animator.Count);
    }

    private static void Play(Animator animator, Outcome outcome, double now)
    {
        foreach (var motion in outcome.Motions)
            animator.Start(motion, now);
    }

    [Fact]
    public void ADroppedWindowSlidesIntoItsTileFromWhereTheSystemLeftIt()
    {
        var core = Load("""
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 0
            """);
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new WindowMoveStarted(new WindowId(2), new Point(1000, 100)));
        var left = new Rectangle(40, 300, 960, 1080);

        var dropped = core.Handle(new WindowMoveEnded(new WindowId(2), new Point(100, 500), left));

        var motion = Assert.Single(dropped.Motions, candidate => candidate.Window == new WindowId(2));
        Assert.Equal(left, motion.From);
        Assert.Equal(Left, motion.To);
    }

    // A loaded config applies its gaps and borders like a reload, and these rectangles have none.
    private const string Gapless = "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n";

    private static WindowManager Load(string text)
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(Gapless + text)));
        return core;
    }

    private static void HoldSuper(WindowManager core) => core.Handle(new KeyPressed("Super_L"));
}
