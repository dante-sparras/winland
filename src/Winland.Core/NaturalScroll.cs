namespace Winland;

public static class NaturalScroll
{
    public static WheelTurned Apply(InputOptions options, WheelTurned wheel)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(wheel);
        if (!options.InvertsScroll)
            return wheel;

        var key = wheel.Key switch
        {
            "mouse_up" => "mouse_down",
            "mouse_down" => "mouse_up",
            "mouse_left" => "mouse_right",
            "mouse_right" => "mouse_left",
            _ => wheel.Key,
        };
        return key == wheel.Key ? wheel : wheel with { Key = key };
    }
}
