namespace Winland.Domain;

/// <summary>How a display's color is reported. Winland reads it and does not change it.</summary>
public static class DisplayColors
{
    /// <summary>The preset a monitor reports when Windows did not name one.</summary>
    public const string StandardPreset = "srgb";

    public static string Format(int bits) => bits switch
    {
        >= 10 => "XRGB2101010",
        > 0 => "XRGB8888",
        _ => "",
    };
}
