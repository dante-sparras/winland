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
    public void AFadeThatReplacesAMoveToAnotherRectangleReleasesTheWindow()
    {
        var target = new RecordingTarget();
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        player.Begin(new Motion(Window, Goal) { From = Start, Move = Linear });
        clock.Now = 50;
        player.Tick();
        Assert.Empty(target.Released);

        player.Begin(new Motion(Window, new Rectangle(300, 0, 100, 100)) { AlphaTo = 200, Alpha = Linear });

        Assert.Equal([Window], target.Released);
        clock.Now = 150;
        Assert.False(player.Tick());
        Assert.Equal([Window, Window], target.Released);
    }

    [Fact]
    public void AFadeOnTheSameRectangleKeepsTheMoveRunning()
    {
        var target = new RecordingTarget();
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        player.Begin(new Motion(Window, Goal) { From = Start, Move = Linear });
        clock.Now = 50;
        player.Tick();

        player.Begin(new Motion(Window, Goal) { AlphaTo = 200, Alpha = Linear });

        Assert.Empty(target.Released);
        clock.Now = 150;
        Assert.False(player.Tick());
        Assert.Contains("place 100", target.Calls);
        Assert.Empty(target.Released);
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
    public void ACloseMotionWaitsItsOwnGraceBeforeTheWindowReturns()
    {
        var target = new RecordingTarget { Open = true };
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        player.Begin(new Motion(Window, new Rectangle(47, 47, 5, 5))
        {
            From = Start,
            Move = Linear,
            End = MotionEnd.Close,
            Rest = Start,
            CloseGraceMilliseconds = 200,
        });

        clock.Now = 100;
        Assert.True(player.Tick());
        clock.Now = 299;
        Assert.True(player.Tick());
        Assert.DoesNotContain("place 0", target.Calls);
        clock.Now = 300;
        Assert.False(player.Tick());

        Assert.Equal("place 0", target.Calls[^2]);
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

    [Fact]
    public void HaltingOrDroppingAWindowStopsItsMotion()
    {
        var target = new RecordingTarget();
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        var other = new WindowId(8);
        player.Begin(new Motion(Window, Goal) { From = Start, Move = Linear });
        player.Begin(new Motion(other, Goal) { From = Start, Move = Linear });

        player.Halt(Window);
        player.Drop(other);
        clock.Now = 50;

        Assert.False(player.Tick());
        Assert.False(player.Running(Window));
        Assert.False(player.Running(other));
        Assert.Equal(["move 0", "move 0"], target.Calls);
    }

    [Fact]
    public void EachFrameNamesTheGoalItIsHeadingFor()
    {
        var target = new RecordingTarget();
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        var grown = new Rectangle(0, 0, 300, 200);

        player.Begin(new Motion(Window, grown) { From = Start, Move = Linear });
        clock.Now = 50;
        player.Tick();

        Assert.Equal([grown, grown], target.Goals);
    }

    // The frames may have done more to the window than move it, and a motion stopped early undoes that.
    [Fact]
    public void AMotionStoppedEarlyReleasesItsWindowAndOneThatLandsThereDoesNot()
    {
        var target = new RecordingTarget();
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        var other = new WindowId(8);
        player.Begin(new Motion(Window, Goal) { From = Start, Move = Linear });
        player.Begin(new Motion(other, Goal) { From = Start, Move = Linear });

        Assert.True(player.Settle(Window, Goal));
        Assert.Empty(target.Released);

        Assert.False(player.Settle(Window, new Rectangle(300, 0, 100, 100)));
        player.Halt(other);

        Assert.Equal([Window, other], target.Released);
    }

    // A frame already past its check is still being applied when the window is dropped. Dropping waits for it,
    // so the caller's reset of the window comes after the frame and is not undone by it.
    [Fact]
    public async Task DroppingAWindowWaitsForTheFrameBeingApplied()
    {
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        var target = new GatedTarget(entered, gate);
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        player.Begin(new Motion(Window, Goal) { From = Start, Move = Linear });
        target.Hold = true;
        clock.Now = 50;

        var tick = Task.Run(player.Tick);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "The frame never started.");
        var drop = Task.Run(() => player.Drop(Window));
        var droppedEarly = await Task.WhenAny(drop, Task.Delay(100)) == drop;
        gate.Set();

        Assert.False(droppedEarly, "Drop returned while the frame was still being applied.");
        await Task.WhenAll(tick, drop).WaitAsync(TimeSpan.FromSeconds(5));
    }

    // A closing window counts as running until its grace is over, and is put back only then.
    [Fact]
    public void AClosingWindowRunsUntilItsGraceIsOver()
    {
        var target = new RecordingTarget { Open = true };
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        player.Begin(new Motion(Window, Goal) { From = Start, Move = Linear, End = MotionEnd.Close, Rest = Start });
        clock.Now = 100;
        player.Tick();
        target.Calls.Clear();

        clock.Now = 100 + (MotionPlayer.CloseGrace.TotalMilliseconds / 2);
        var halfway = player.Tick();
        var runningHalfway = player.Running(Window);
        var callsHalfway = target.Calls.Count;
        clock.Now = 100 + MotionPlayer.CloseGrace.TotalMilliseconds;
        player.Tick();

        Assert.True(halfway);
        Assert.True(runningHalfway);
        Assert.Equal(0, callsHalfway);
        Assert.Equal(["place 0", "alpha 255"], target.Calls);
        Assert.False(player.Running(Window));
    }

    // A window that closed is not put back when a new motion starts on it before its grace is over.
    [Fact]
    public void ANewMotionCancelsAPendingClose()
    {
        var target = new RecordingTarget { Open = true };
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        player.Begin(new Motion(Window, Goal) { From = Start, Move = Linear, End = MotionEnd.Close, Rest = Start });
        clock.Now = 100;
        player.Tick();

        player.Begin(new Motion(Window, Goal) { From = Goal });
        target.Calls.Clear();
        clock.Now = 100 + MotionPlayer.CloseGrace.TotalMilliseconds;
        player.Tick();

        Assert.DoesNotContain("place 0", target.Calls);
    }

    [Fact]
    public void LandingAClosingMotionClosesTheWindowAndPutsNothingBack()
    {
        var target = new RecordingTarget { Open = true };
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        player.Begin(new Motion(Window, Goal) { From = Start, Move = Linear, FadeTo = 0, Fade = Linear, End = MotionEnd.Close, Rest = Start });
        target.Calls.Clear();

        player.Land();
        clock.Now = 100 + MotionPlayer.CloseGrace.TotalMilliseconds;
        player.Tick();

        Assert.Equal(["alpha 0", "close"], target.Calls);
        Assert.False(player.Running(Window));
    }

    // Landing puts back a window still in its close grace when it did not close, and forgets the grace.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LandingPutsBackAWindowThatDidNotClose(bool open)
    {
        var target = new RecordingTarget { Open = open };
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        player.Begin(new Motion(Window, Goal) { From = Start, Move = Linear, End = MotionEnd.Close, Rest = Start });
        clock.Now = 100;
        player.Tick();
        target.Calls.Clear();

        player.Land();
        var landed = target.Calls.ToList();
        clock.Now = 100 + MotionPlayer.CloseGrace.TotalMilliseconds;
        player.Tick();

        Assert.Equal(open ? ["place 0", "alpha 255"] : [], landed);
        Assert.Equal(landed, target.Calls);
    }

    [Fact]
    public void BorderAndAlphaChannelsAreShownAndLanded()
    {
        var target = new RecordingTarget();
        var clock = new Clock();
        using var player = new MotionPlayer(target, () => clock.Now, threaded: false);
        var red = new BorderColor(0xFF, 0xFF, 0, 0);
        var blue = new BorderColor(0xFF, 0, 0, 0xFF);
        player.Begin(new Motion(Window, Goal) { AlphaFrom = 255, AlphaTo = 105, Alpha = Linear, BorderFrom = red, BorderTo = blue, Border = Linear });
        clock.Now = 50;
        player.Tick();
        clock.Now = 100;
        player.Tick();
        var stepped = target.Calls.ToList();
        target.Calls.Clear();

        player.Begin(new Motion(Window, Goal) { AlphaFrom = 105, AlphaTo = 255, Alpha = Linear, BorderFrom = blue, BorderTo = red, Border = Linear });
        player.Land();

        Assert.Contains("alpha 180", stepped);
        Assert.True(stepped.Count(call => call == "border") >= 3);
        Assert.Equal(["border", "alpha 105"], stepped.TakeLast(2));
        Assert.Equal(["border", "place 100", "alpha 255"], target.Calls.TakeLast(3));
    }

    // The player's own thread steps a motion to its end, and starts again after a landing.
    [Fact]
    public void TheThreadStepsAMotionToItsEndAndStartsAgainAfterLanding()
    {
        var target = new RecordingTarget();
        var player = new MotionPlayer(target);
        // Long enough that the thread still has frames to pace when it first steps, even on a busy machine where it
        // starts late. A shorter motion can land in one step and never pace.
        var quick = new Timing(Bezier.Linear, 400);

        player.Begin(new Motion(Window, Goal) { From = Start, Move = quick });
        var first = WaitUntil(() => target.Contains("place 100"));
        player.Land();
        target.Clear();
        player.Begin(new Motion(Window, Start) { From = Goal, Move = quick });
        var second = WaitUntil(() => target.Contains("place 0"));
        player.Dispose();

        Assert.True(first, "The first motion never landed.");
        Assert.True(second, "The motion after the landing never landed.");
        Assert.True(target.Paced > 0);
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;

            Thread.Sleep(10);
        }

        return condition();
    }

    private sealed class Clock
    {
        public double Now { get; set; }
    }

    // A target whose move, once held, signals that it started and then waits for the gate.
    private sealed class GatedTarget(ManualResetEventSlim entered, ManualResetEventSlim gate) : IMotionTarget
    {
        public bool Hold { get; set; }

        public bool Hung(WindowId window) => false;

        public void Release(WindowId window)
        {
        }

        public void Move(WindowId window, Rectangle bounds, Rectangle goal)
        {
            if (!Hold)
                return;

            entered.Set();
            gate.Wait(TimeSpan.FromSeconds(10));
        }

        public void Place(WindowId window, Rectangle bounds)
        {
        }

        public void Alpha(WindowId window, byte alpha)
        {
        }

        public void Border(WindowId window, BorderColor color)
        {
        }

        public void Hide(WindowId window)
        {
        }

        public void Close(WindowId window)
        {
        }

        public bool Shown(WindowId window) => true;

        public void Pace()
        {
        }
    }

    private sealed class RecordingTarget : IMotionTarget
    {
        public List<string> Calls { get; } = [];

        public bool Open { get; init; }

        public bool Frozen { get; init; }

        public bool Hung(WindowId window) => Frozen;

        public List<Rectangle> Goals { get; } = [];

        public List<WindowId> Released { get; } = [];

        public void Move(WindowId window, Rectangle bounds, Rectangle goal)
        {
            Goals.Add(goal);
            Add($"move {bounds.X}");
        }

        public void Release(WindowId window) => Released.Add(window);

        public void Place(WindowId window, Rectangle bounds) => Add($"place {bounds.X}");

        public void Alpha(WindowId window, byte alpha) => Add($"alpha {alpha}");

        public void Border(WindowId window, BorderColor color) => Add("border");

        public void Hide(WindowId window) => Add("hide");

        public void Close(WindowId window) => Add("close");

        public bool Shown(WindowId window) => Open;

        public int Paced { get; private set; }

        private void Add(string call)
        {
            lock (Calls)
                Calls.Add(call);
        }

        public void Pace()
        {
            Paced++;
            Thread.Sleep(5);
        }

        public bool Contains(string call)
        {
            lock (Calls)
                return Calls.Contains(call);
        }

        public void Clear()
        {
            lock (Calls)
                Calls.Clear();
        }
    }
}
