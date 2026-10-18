using Winland;

namespace Winland.Tests;

public class TouchpadOptionsTests
{
    [Fact]
    public void UnsetTouchpadKeysLeaveTheTouchpadAlone()
    {
        Assert.Null(InputOptions.From(ConfigParser.Parse("input:sensitivity = 0\n")).Request()?.Touchpad);
    }

    [Fact]
    public void TouchpadKeysMapToTheWindowsSettings()
    {
        var options = InputOptions.From(ConfigParser.Parse("""
            input {
                touchpad {
                    tap-to-click = false
                    tap-and-drag = true
                    natural_scroll = true
                    disable_while_typing = false
                    clickfinger_behavior = true
                }
            }
            """));

        Assert.Equal(new TouchpadSettings(false, true, false, 0, false), options.Request()?.Touchpad);

        var other = InputOptions.From(ConfigParser.Parse("input:touchpad:natural_scroll = false\ninput:touchpad:disable_while_typing = true\n"));
        Assert.Equal(new TouchpadSettings(null, null, true, 2, null), other.Request()?.Touchpad);
    }

    [Fact]
    public void TheTouchpadOriginalsSurviveACrash()
    {
        var snapshot = new RecoverySnapshot([]) { Input = new InputOriginals { Touchpad = new TouchpadSettings(true, false, true, 3, null) } };

        Assert.True(RecoverySnapshot.TryParse(snapshot.ToJson(), out var read));
        Assert.Equal(new TouchpadSettings(true, false, true, 3, null), read.Input?.Touchpad);
    }

    [Fact]
    public void ForceNoAccelTurnsAccelerationOffOverTheProfile()
    {
        Assert.Equal(PointerAcceleration.Flat, InputOptions.From(ConfigParser.Parse("input:accel_profile = adaptive\ninput:force_no_accel = true\n")).Request()?.Acceleration);
        Assert.Equal(PointerAcceleration.Adaptive, InputOptions.From(ConfigParser.Parse("input:accel_profile = adaptive\ninput:force_no_accel = false\n")).Request()?.Acceleration);
        Assert.Null(InputOptions.From(ConfigParser.Parse("input:force_no_accel = false\n")).Request());
    }
}
