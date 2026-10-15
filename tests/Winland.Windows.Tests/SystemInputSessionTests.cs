using Winland;
using Winland.Windows;

namespace Winland.Windows.Tests;

public class SystemInputSessionTests
{
    [Fact]
    public void OnlyAnExplicitFieldIsReadAndWritten()
    {
        var input = new FakeInput();
        var session = new SystemInputSession(input);

        session.Apply(new InputSettingsRequest { MouseSpeed = 15 });

        Assert.Equal(15, input.MouseSpeed);
        Assert.Equal(1, input.Reads.MouseSpeed);
        Assert.Equal(0, input.Reads.KeyboardDelay);
        Assert.Equal(0, input.Writes.KeyboardDelay);
        Assert.Equal(0, input.Reads.NumLock);
        Assert.Equal(0, input.Writes.SwappedButtons);
        Assert.Equal(0, input.Reads.WheelLines);
        Assert.Equal(new InputOriginals { MouseSpeed = 10 }, session.Originals);
    }

    [Fact]
    public void ASecondApplyOfTheSameRequestDoesNotWriteAgain()
    {
        var input = new FakeInput();
        var session = new SystemInputSession(input);
        var request = new InputSettingsRequest { KeyboardSpeed = 25, KeyboardDelay = 1 };

        session.Apply(request);
        session.Apply(request);

        Assert.Equal(1, input.Writes.KeyboardSpeed);
        Assert.Equal(1, input.Writes.KeyboardDelay);
    }

    [Fact]
    public void RestorePutsTheOriginalsBack()
    {
        var input = new FakeInput { NumLock = true, Swapped = false, WheelLines = 3 };
        var session = new SystemInputSession(input);
        session.Apply(new InputSettingsRequest
        {
            NumLock = false,
            SwapButtons = true,
            WheelLines = 6,
            Acceleration = PointerAcceleration.Flat,
        });

        Assert.Equal(new MouseCurve(0, 0, 0), input.Curve);
        session.Restore();

        Assert.True(input.NumLock);
        Assert.False(input.Swapped);
        Assert.Equal(3, input.WheelLines);
        Assert.Equal(new MouseCurve(6, 10, 1), input.Curve);
        Assert.Null(session.Originals);
    }

    [Fact]
    public void RevertRestoresEveryFieldWhenTheRequestGoesAway()
    {
        var input = new FakeInput();
        var session = new SystemInputSession(input);
        session.Apply(new InputSettingsRequest { MouseSpeed = 15, KeyboardDelay = 0 });

        session.Revert();

        Assert.Equal(10, input.MouseSpeed);
        Assert.Equal(2, input.KeyboardDelay);
        Assert.Null(session.Originals);
    }

    [Fact]
    public void DroppingAFieldRestoresJustThatField()
    {
        var input = new FakeInput();
        var session = new SystemInputSession(input);
        session.Apply(new InputSettingsRequest { MouseSpeed = 20, WheelLines = 6 });
        session.Apply(new InputSettingsRequest { WheelLines = 6 });

        Assert.Equal(10, input.MouseSpeed);
        Assert.Equal(6, input.WheelLines);
        Assert.Equal(new InputOriginals { WheelLines = 3 }, session.Originals);
    }

    [Fact]
    public void AdaptiveAccelerationWritesTheEnhancedCurve()
    {
        var input = new FakeInput();
        var session = new SystemInputSession(input);

        session.Apply(new InputSettingsRequest { Acceleration = PointerAcceleration.Adaptive });

        Assert.Equal(new MouseCurve(6, 10, 1), input.Curve);
        Assert.Equal(new MouseCurve(6, 10, 1), session.Originals!.MouseCurve);
    }

    [Fact]
    public void CrashRecoveryWritesTheSavedOriginalsBeforeANewRequest()
    {
        var input = new FakeInput { MouseSpeed = 4, KeyboardDelay = 3 };
        var session = new SystemInputSession(input);
        var saved = new InputOriginals { MouseSpeed = 10, KeyboardDelay = 1 };

        session.RestoreSaved(saved);
        Assert.Equal(10, input.MouseSpeed);
        Assert.Equal(1, input.KeyboardDelay);
        Assert.Null(session.Originals);

        session.Apply(new InputSettingsRequest { MouseSpeed = 18 });

        Assert.Equal(18, input.MouseSpeed);
        Assert.Equal(new InputOriginals { MouseSpeed = 10 }, session.Originals);
        Assert.Equal(1, input.KeyboardDelay);
    }

    [Fact]
    public void AFailedReadDoesNotWriteOrRemember()
    {
        var input = new FakeInput { FailMouseSpeed = true };
        var session = new SystemInputSession(input);

        session.Apply(new InputSettingsRequest { MouseSpeed = 12 });

        Assert.Equal(0, input.Writes.MouseSpeed);
        Assert.Null(session.Originals);
        Assert.Equal(10, input.MouseSpeed);
    }

    private sealed class FakeInput : ISystemInput
    {
        public int KeyboardDelay { get; set; } = 2;
        public int KeyboardSpeed { get; set; } = 16;
        public bool NumLock { get; set; }
        public bool Swapped { get; set; }
        public MouseCurve Curve { get; set; } = new(6, 10, 1);
        public int MouseSpeed { get; set; } = 10;
        public int WheelLines { get; set; } = 3;
        public bool FailMouseSpeed { get; set; }
        public Counts Reads { get; } = new();
        public Counts Writes { get; } = new();

        public bool TryReadKeyboardDelay(out int delay)
        {
            Reads.KeyboardDelay++;
            delay = KeyboardDelay;
            return true;
        }

        public void WriteKeyboardDelay(int delay)
        {
            Writes.KeyboardDelay++;
            KeyboardDelay = delay;
        }

        public bool TryReadKeyboardSpeed(out int speed)
        {
            Reads.KeyboardSpeed++;
            speed = KeyboardSpeed;
            return true;
        }

        public void WriteKeyboardSpeed(int speed)
        {
            Writes.KeyboardSpeed++;
            KeyboardSpeed = speed;
        }

        public bool TryReadNumLock(out bool enabled)
        {
            Reads.NumLock++;
            enabled = NumLock;
            return true;
        }

        public void WriteNumLock(bool enabled)
        {
            Writes.NumLock++;
            NumLock = enabled;
        }

        public bool TryReadSwappedButtons(out bool swapped)
        {
            Reads.SwappedButtons++;
            swapped = Swapped;
            return true;
        }

        public void WriteSwappedButtons(bool swapped)
        {
            Writes.SwappedButtons++;
            Swapped = swapped;
        }

        public bool TryReadMouseCurve(out MouseCurve curve)
        {
            Reads.Curve++;
            curve = Curve;
            return true;
        }

        public void WriteMouseCurve(MouseCurve curve)
        {
            Writes.Curve++;
            Curve = curve;
        }

        public bool TryReadMouseSpeed(out int speed)
        {
            Reads.MouseSpeed++;
            speed = MouseSpeed;
            return !FailMouseSpeed;
        }

        public void WriteMouseSpeed(int speed)
        {
            Writes.MouseSpeed++;
            MouseSpeed = speed;
        }

        public bool TryReadWheelLines(out int lines)
        {
            Reads.WheelLines++;
            lines = WheelLines;
            return true;
        }

        public void WriteWheelLines(int lines)
        {
            Writes.WheelLines++;
            WheelLines = lines;
        }

        public sealed class Counts
        {
            public int KeyboardDelay;
            public int KeyboardSpeed;
            public int NumLock;
            public int SwappedButtons;
            public int Curve;
            public int MouseSpeed;
            public int WheelLines;
        }
    }
}
