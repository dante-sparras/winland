using Winland;

namespace Winland.Tests;

public class PointerDeviceTests
{
    [Fact]
    public void ANamedDeviceKeepsSensitivityScrollAndRotation()
    {
        var document = ConfigParser.Parse("""
            device[Epic Mouse]:sensitivity = 1
            device[epic-mouse]:scroll_factor = 4
            device[epic-mouse] {
                rotation = 90
                tags = desk
                gaps_in = 9
            }
            device[other]:rotation = 400
            device[other]:sensitivity = no
            device:sensitivity = 1
            device[pad]:accel_profile = flat
            device[pad]:force_no_accel = true
            """);

        Assert.Equal(
            [
                new DevicePointer("epic-mouse", 1, 4, 90),
                new DevicePointer("other", null, null, 359),
            ],
            document.DevicePointers);
        var tag = Assert.Single(document.DeviceTags);
        Assert.Equal("epic-mouse", tag.Device);
        Assert.Equal(["desk"], tag.Tags);
        Assert.Equal(
            [
                new ConfigWarning(6, "Unsupported keyword 'device:gaps_in'."),
                new ConfigWarning(10, "Unsupported keyword 'device:sensitivity'."),
                new ConfigWarning(11, "Unsupported keyword 'device:accel_profile'."),
                new ConfigWarning(12, "Unsupported keyword 'device:force_no_accel'."),
            ],
            document.Warnings);
        Assert.True(ConfigWarnings.IsGapNotice("Unsupported keyword 'device:accel_profile'."));
        Assert.False(ConfigWarnings.IsGapNotice("Unsupported keyword 'device:sensitivity'."));
        Assert.False(ConfigWarnings.IsGapNotice("Unsupported keyword 'device:force_no_accel'."));
    }

    [Fact]
    public void ALaterLineReplacesOneFieldAndABadValueIsIgnored()
    {
        var document = ConfigParser.Parse("""
            device[mouse]:sensitivity = -2
            device[mouse]:sensitivity = 0.5
            device[mouse]:scroll_factor = 1000
            device[mouse]:rotation = nope
            """);

        Assert.Equal(new DevicePointer("mouse", 0.5, 100, null), Assert.Single(document.DevicePointers));
        Assert.Empty(document.Warnings);
    }

    [Fact]
    public void TheCorePublishesThePointerTable()
    {
        var core = new Core();
        Assert.Equal(0, core.PointerGeneration);
        Assert.False(core.Pointers.Active);
        Assert.Null(core.ConfiguredPointerSpeed);

        core.Handle(new ConfigLoaded(ConfigParser.Parse("device[mouse]:scroll_factor = 2\ninput:sensitivity = 0\ninput:rotation = 90\n")));

        Assert.Equal(1, core.PointerGeneration);
        Assert.True(core.Pointers.Active);
        Assert.False(core.Pointers.DefersMotion);
        Assert.Equal(10, core.ConfiguredPointerSpeed);
        Assert.Equal(90, core.Pointers.GlobalRotation);
        Assert.Equal(1, core.Pointers.ScrollBaseline);
        Assert.True(core.Pointers.TryScrollFactor("Mouse", out var factor));
        Assert.Equal(2, factor);
    }

    [Fact]
    public void SensitivityScalesTheMoveAndKeepsTheFraction()
    {
        var devices = Table("device[mouse]:sensitivity = 1\n");
        var correction = new PointerCorrection();
        correction.NoteMove(10, 0, 0);

        Assert.Equal((10, 0), correction.TakeMove(devices, "mouse", systemSpeed: 10, nowMs: 0));

        var half = Table("device[mouse]:sensitivity = 0.5\n");
        var fractions = new PointerCorrection();
        fractions.NoteMove(1, 0, 0);
        fractions.NoteMove(1, 0, 1);

        Assert.Equal((1, 0), fractions.TakeMove(half, "mouse", 10, 1));
        Assert.Null(fractions.TakeMove(half, "mouse", 10, 2));
    }

    [Fact]
    public void AnUnknownDeviceDropsTheMove()
    {
        var devices = Table("device[mouse]:sensitivity = 1\n");
        var correction = new PointerCorrection();
        correction.NoteMove(10, 0, 0);
        correction.NoteMove(4, 0, 1);

        Assert.Null(correction.TakeMove(devices, null, 10, 1));
        Assert.Equal((4, 0), correction.TakeMove(devices, "mouse", 10, 1));
    }

    [Fact]
    public void RotationIsPerDeviceAndTheOthersKeepTheGlobalTurn()
    {
        var devices = Table("input:rotation = 90\ndevice[flat]:rotation = 0\ndevice[side]:rotation = 180\ndevice[fast]:sensitivity = 1\n");
        Assert.True(devices.DefersMotion);

        var correction = new PointerCorrection();
        correction.NoteMove(10, 0, 0);
        correction.NoteMove(10, 0, 1);
        correction.NoteMove(10, 0, 2);
        correction.NoteMove(10, 0, 3);

        Assert.Equal((-10, 10), correction.TakeMove(devices, "other", 10, 3));
        Assert.Null(correction.TakeMove(devices, "flat", 10, 3));
        Assert.Equal((-20, 0), correction.TakeMove(devices, "side", 10, 3));
        Assert.Equal((-10, 20), correction.TakeMove(devices, "fast", 10, 3));
    }

    [Fact]
    public void OnlyScrollDoesNotDeferTheHookTurn()
    {
        var devices = Table("device[mouse]:scroll_factor = 3\ninput:rotation = 90\n");

        Assert.True(devices.Active);
        Assert.False(devices.DefersMotion);
        Assert.False(PointerCorrection.ShouldNote(devices.DefersMotion, redirected: false, touchpad: false, dx: 4, dy: 0));
        Assert.False(PointerCorrection.ShouldNote(true, redirected: true, touchpad: false, dx: 4, dy: 0));
        Assert.False(PointerCorrection.ShouldNote(true, redirected: false, touchpad: true, dx: 4, dy: 0));
        Assert.True(PointerCorrection.ShouldNote(true, redirected: false, touchpad: false, dx: 4, dy: 0));
    }

    [Fact]
    public void ScrollFactorIsTheDifferenceFromTheGlobalFactor()
    {
        var doubled = Table("device[mouse]:scroll_factor = 2\n");
        var correction = new PointerCorrection();
        Assert.Equal(120, correction.CorrectWheel(doubled, "mouse", 120, horizontal: false));
        Assert.Null(correction.CorrectWheel(doubled, "other", 120, horizontal: false));

        var cancelled = Table("device[mouse]:scroll_factor = 0\ninput:scroll_factor = 1\n");
        Assert.Equal(-120, new PointerCorrection().CorrectWheel(cancelled, "mouse", 120, horizontal: true));

        var matched = Table("device[mouse]:scroll_factor = 2\ninput:scroll_factor = 2\n");
        Assert.Null(new PointerCorrection().CorrectWheel(matched, "mouse", 120, horizontal: false));

        var stopped = Table("device[mouse]:scroll_factor = 50\ninput:scroll_factor = 0\n");
        Assert.True(stopped.GlobalScrollStops);
        Assert.Null(new PointerCorrection().CorrectWheel(stopped, "mouse", 120, horizontal: false));
    }

    [Fact]
    public void AStaleMoveClearsTheQueue()
    {
        var devices = Table("device[mouse]:sensitivity = 1\n");
        var correction = new PointerCorrection();
        correction.NoteMove(10, 0, 0);
        correction.NoteMove(10, 0, 10);

        Assert.Null(correction.TakeMove(devices, "mouse", 10, PointerCorrection.StaleMs + 1));
        correction.NoteMove(10, 0, 100);
        Assert.Equal((10, 0), correction.TakeMove(devices, "mouse", 10, 100));
    }

    [Fact]
    public void TheQueueDropsTheOldestMovePastItsLimit()
    {
        var devices = Table("device[mouse]:sensitivity = 1\n");
        var correction = new PointerCorrection();
        for (var i = 0; i < PointerCorrection.QueueLimit + 1; i++)
            correction.NoteMove(i + 1, 0, 0);

        Assert.Equal((2, 0), correction.TakeMove(devices, "mouse", 10, 0));
    }

    private static PointerDevices Table(string text)
    {
        var document = ConfigParser.Parse(text);
        return PointerDevices.From(document, InputOptions.From(document));
    }
}
