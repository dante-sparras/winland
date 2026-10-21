namespace Winland.Domain;

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
