using Winland;
using Winland.Windows;

namespace Winland.Tests;

public class MotionPlayerTests
{
    private static readonly WindowId Window = new(7);
    private static readonly Timing Linear = new(Bezier.Linear, 100);
    private static readonly Rectangle Start = new(0, 0, 100, 100);
    private static readonly Rectangle Goal = new(100, 0, 100, 100);

    [Fact]
    public void AMotionShowsItsFirstFrameAndEndsWithThePlacement()
    {
        var target = new RecordingTarget();
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);

        Assert.True(player.Begin(new Motion(Window, Goal) { From = Start, Move = Linear }));
        clock.Now = 50;
        Assert.True(player.Tick());
        clock.Now = 100;
        Assert.False(player.Tick());

        Assert.Equal(["move 0", "move 50", "place 100"], target.Calls);
    }

    [Fact]
    public void APlacementThatStopsAMotionIsTheLastOne()
    {
        var target = new RecordingTarget();
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        player.Begin(new Motion(Window, Goal) { From = Start, Move = Linear });

        Assert.False(player.Settle(Window, new Rectangle(300, 0, 100, 100)));
        target.Calls.Add("place 300");
        clock.Now = 100;
        player.Tick();

        Assert.Equal("place 300", target.Calls[^1]);
    }

    [Fact]
    public void ALeavingWindowIsHiddenAndPutBackOnItsLayoutRectangle()
    {
        var target = new RecordingTarget();
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        player.Begin(new Motion(Window, Start with { X = -1920 }) { From = Start, Move = Linear, End = MotionEnd.Hide, Rest = Start, AlphaTo = 200 });

        clock.Now = 100;
        player.Tick();

        Assert.Equal(["move 0", "hide", "place 0", "alpha 200"], target.Calls);
    }

    [Fact]
    public void AWindowThatRefusesToCloseReturnsToItsLayoutRectangle()
    {
        var target = new RecordingTarget { Open = true };
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        player.Begin(new Motion(Window, new Rectangle(47, 47, 5, 5)) { From = Start, Move = Linear, FadeTo = 0, Fade = Linear, End = MotionEnd.Close, Rest = Start });

        clock.Now = 100;
        Assert.True(player.Tick());
        clock.Now = 100 + MotionPlayer.CloseGrace.TotalMilliseconds;
        Assert.False(player.Tick());

        Assert.Equal(["move 0", "alpha 255", "alpha 0", "close", "place 0", "alpha 255"], target.Calls);
    }

    [Fact]
    public void AHungWindowSkipsTheFramesInBetween()
    {
        var target = new RecordingTarget { Frozen = true };
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        player.Begin(new Motion(Window, Goal) { From = Start, Move = Linear });
        target.Calls.Clear();

        clock.Now = 50;
        player.Tick();
        clock.Now = 100;
        player.Tick();

        Assert.Equal(["place 100"], target.Calls);
    }

    [Fact]
    public void LandingPutsARunningMotionOnItsLayoutRectangle()
    {
        var target = new RecordingTarget();
        var clock = new Clock();
        var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        player.Begin(new Motion(Window, Goal) { From = Start, Move = Linear });

        player.Land();

        Assert.Equal(["move 0", "place 100", "alpha 255"], target.Calls);
        Assert.False(player.Running(Window));
        Assert.True(player.Begin(new Motion(Window, Goal) { From = Start, Move = Linear }));
        player.Dispose();
    }

    private sealed class Clock
    {
        public double Now { get; set; }
    }

    private sealed class RecordingTarget : IMotionTarget
    {
        public List<string> Calls { get; } = [];

        public bool Open { get; init; }

        public bool Frozen { get; init; }

        public bool Hung(WindowId window) => Frozen;

        public void Move(WindowId window, Rectangle bounds) => Calls.Add($"move {bounds.X}");

        public void Place(WindowId window, Rectangle bounds) => Calls.Add($"place {bounds.X}");

        public void Alpha(WindowId window, byte alpha) => Calls.Add($"alpha {alpha}");

        public void Border(WindowId window, BorderColor color) => Calls.Add("border");

        public void Hide(WindowId window) => Calls.Add("hide");

        public void Close(WindowId window) => Calls.Add("close");

        public bool Shown(WindowId window) => Open;

        public void Pace()
        {
        }
    }
}
