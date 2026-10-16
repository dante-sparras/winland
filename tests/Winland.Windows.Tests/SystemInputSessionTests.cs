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
        var input = new FakeInput { Failing = { nameof(FakeInput.MouseSpeed) } };
        var session = new SystemInputSession(input);

        session.Apply(new InputSettingsRequest { MouseSpeed = 12 });

        Assert.Equal(0, input.Writes.MouseSpeed);
        Assert.Null(session.Originals);
        Assert.Equal(10, input.MouseSpeed);
    }

    private static readonly InputSettingsRequest Everything = new()
    {
        KeyboardDelay = 0,
        KeyboardSpeed = 31,
        NumLock = true,
        SwapButtons = true,
        Acceleration = PointerAcceleration.Flat,
        MouseSpeed = 20,
        WheelLines = 6,
    };

    private static readonly InputOriginals Defaults = new()
    {
        KeyboardDelay = 2,
        KeyboardSpeed = 16,
        NumLock = false,
        SwappedButtons = false,
        MouseCurve = new MouseCurve(6, 10, 1),
        MouseSpeed = 10,
        WheelLines = 3,
    };

    [Fact]
    public void EveryFieldIsWrittenRememberedAndPutBack()
    {
        var input = new FakeInput();
        var session = new SystemInputSession(input);

        session.Apply(Everything);
        var applied = input.Values();
        var originals = session.Originals;
        session.Restore();

        Assert.Equal(new InputOriginals
        {
            KeyboardDelay = 0,
            KeyboardSpeed = 31,
            NumLock = true,
            SwappedButtons = true,
            MouseCurve = new MouseCurve(0, 0, 0),
            MouseSpeed = 20,
            WheelLines = 6,
        }, applied);
        Assert.Equal(Defaults, originals);
        Assert.Equal(Defaults, input.Values());
        Assert.Null(session.Originals);
    }

    // A changed value is written once, and an unchanged one not again.
    [Fact]
    public void AChangedRequestWritesOnlyWhatChanged()
    {
        var input = new FakeInput();
        var session = new SystemInputSession(input);
        session.Apply(Everything);

        session.Apply(Everything with
        {
            KeyboardDelay = 1,
            KeyboardSpeed = 30,
            NumLock = false,
            SwapButtons = false,
            Acceleration = PointerAcceleration.Adaptive,
            MouseSpeed = 19,
            WheelLines = 5,
        });

        Assert.Equal(new FakeInput.Counts { KeyboardDelay = 2, KeyboardSpeed = 2, NumLock = 2, SwappedButtons = 2, Curve = 2, MouseSpeed = 2, WheelLines = 2 }, input.Writes);
        Assert.Equal(new FakeInput.Counts { KeyboardDelay = 1, KeyboardSpeed = 1, NumLock = 1, SwappedButtons = 1, Curve = 1, MouseSpeed = 1, WheelLines = 1 }, input.Reads);
        Assert.Equal(Defaults, session.Originals);
    }

    [Fact]
    public void AnEmptyRequestPutsEveryFieldBack()
    {
        var input = new FakeInput();
        var session = new SystemInputSession(input);
        session.Apply(Everything);

        session.Apply(new InputSettingsRequest());

        Assert.Equal(Defaults, input.Values());
        Assert.Null(session.Originals);
    }

    [Fact]
    public void NoFieldWhoseReadFailsIsWrittenOrRemembered()
    {
        var input = new FakeInput();
        input.Failing.UnionWith(FakeInput.Fields);
        var session = new SystemInputSession(input);

        session.Apply(Everything);

        Assert.Equal(new FakeInput.Counts(), input.Writes);
        Assert.Null(session.Originals);
        Assert.Equal(Defaults, input.Values());
    }

    // The same request again reads a field whose first read failed, and applies it once it can.
    [Theory]
    [MemberData(nameof(EachField))]
    public void AFieldWhoseReadFailedIsReadAgain(string field)
    {
        var input = new FakeInput();
        input.Failing.Add(field);
        var session = new SystemInputSession(input);
        session.Apply(Everything);

        input.Failing.Clear();
        session.Apply(Everything);

        Assert.Equal(Defaults, session.Originals);
        Assert.Equal(1, input.Writes.Of(field));
        Assert.Equal(2, input.Reads.Of(field));
    }

    public static TheoryData<string> EachField() => [.. FakeInput.Fields];

    [Fact]
    public void RevertWithNothingAppliedWritesNothing()
    {
        var input = new FakeInput();
        var session = new SystemInputSession(input);

        session.Revert();

        Assert.Equal(new FakeInput.Counts(), input.Writes);
    }

    // A request that could not be read is forgotten by a revert, so it is tried again from the start.
    [Fact]
    public void RevertForgetsARequestThatWroteNothing()
    {
        var input = new FakeInput { Failing = { nameof(FakeInput.MouseSpeed) } };
        var session = new SystemInputSession(input);
        var request = new InputSettingsRequest { MouseSpeed = 12 };
        session.Apply(request);

        session.Revert();
        input.Failing.Clear();
        session.Apply(request);

        Assert.Equal(12, input.MouseSpeed);
        Assert.Equal(new InputOriginals { MouseSpeed = 10 }, session.Originals);
    }

    [Fact]
    public void MissingArgumentsAreRefused()
    {
        var session = new SystemInputSession(new FakeInput());

        Assert.Throws<ArgumentNullException>(() => new SystemInputSession(null!));
        Assert.Throws<ArgumentNullException>(() => session.Apply(null!));
        Assert.Throws<ArgumentNullException>(() => session.RestoreSaved(null!));
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
        public HashSet<string> Failing { get; } = [];
        public Counts Reads { get; } = new();
        public Counts Writes { get; } = new();

        public bool TryReadKeyboardDelay(out int delay)
        {
            Reads.KeyboardDelay++;
            delay = KeyboardDelay;
            return !Failing.Contains(nameof(KeyboardDelay));
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
            return !Failing.Contains(nameof(KeyboardSpeed));
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
            return !Failing.Contains(nameof(NumLock));
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
            return !Failing.Contains(nameof(Swapped));
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
            return !Failing.Contains(nameof(Curve));
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
            return !Failing.Contains(nameof(MouseSpeed));
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
            return !Failing.Contains(nameof(WheelLines));
        }

        public void WriteWheelLines(int lines)
        {
            Writes.WheelLines++;
            WheelLines = lines;
        }

        public static readonly string[] Fields =
            [nameof(KeyboardDelay), nameof(KeyboardSpeed), nameof(NumLock), nameof(Swapped), nameof(Curve), nameof(MouseSpeed), nameof(WheelLines)];

        public InputOriginals Values() => new()
        {
            KeyboardDelay = KeyboardDelay,
            KeyboardSpeed = KeyboardSpeed,
            NumLock = NumLock,
            SwappedButtons = Swapped,
            MouseCurve = Curve,
            MouseSpeed = MouseSpeed,
            WheelLines = WheelLines,
        };

        public sealed record Counts
        {
            public int KeyboardDelay { get; set; }
            public int KeyboardSpeed { get; set; }
            public int NumLock { get; set; }
            public int SwappedButtons { get; set; }
            public int Curve { get; set; }
            public int MouseSpeed { get; set; }
            public int WheelLines { get; set; }

            public int Of(string field) => field switch
            {
                nameof(FakeInput.KeyboardDelay) => KeyboardDelay,
                nameof(FakeInput.KeyboardSpeed) => KeyboardSpeed,
                nameof(FakeInput.NumLock) => NumLock,
                nameof(FakeInput.Swapped) => SwappedButtons,
                nameof(FakeInput.Curve) => Curve,
                nameof(FakeInput.MouseSpeed) => MouseSpeed,
                nameof(FakeInput.WheelLines) => WheelLines,
                _ => throw new ArgumentOutOfRangeException(nameof(field)),
            };
        }
    }
}
