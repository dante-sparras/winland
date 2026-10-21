namespace Winland.Core;

/// <summary>
/// What Windows reports for one display. <see cref="Split"/> is true when HDR and wide color are two switches, which
/// they are from Windows 11 24H2 on. Luminance is in nits, or -1 when Windows did not report it.
/// </summary>
public readonly record struct DisplayColorReading(
    bool HdrSupported,
    bool WideSupported,
    bool HdrOn,
    bool WideOn,
    bool AdvancedSupported,
    bool Split,
    int BitsPerChannel,
    float MinLuminance = -1,
    float MaxLuminance = -1,
    float MaxAvgLuminance = -1,
    bool Chroma = false);

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
