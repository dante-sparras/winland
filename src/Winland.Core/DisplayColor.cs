namespace Winland;

/// <summary>The color mode a display shows: standard, wide color, or high dynamic range.</summary>
public enum DisplayColorMode
{
    Standard,
    Wide,
    HighDynamicRange,
}

/// <summary>
/// A request to show a display in the color mode its monitor rule names. <see cref="TenBit"/> asks for 10-bit color.
/// <see cref="WideOverride"/> and <see cref="HdrOverride"/> are the rule's <c>supports_wide_color</c> and
/// <c>supports_hdr</c>: -1 cannot, 0 whatever Windows reports, 1 can.
/// </summary>
public readonly record struct DisplayColorRequest(string Device, string Preset, bool TenBit = false, int WideOverride = 0, int HdrOverride = 0)
{
    public DisplayColorMode Mode => DisplayColors.ModeOf(Preset);
}

/// <summary>What a display can show and what Windows shows on it now.</summary>
public readonly record struct DisplayColorSupport(bool HdrSupported, bool WideSupported, bool HdrOn, bool WideOn);

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
    bool Chroma = false)
{
    public DisplayColorSupport Support => new(HdrSupported, WideSupported, HdrOn, WideOn);
}

/// <summary>
/// The switches to turn for a color mode: HDR and wide color on or off, null to leave one alone, and why the mode
/// cannot be shown when it cannot.
/// </summary>
public readonly record struct DisplayColorPlan(bool? Hdr, bool? Wide, string? Unsupported);

/// <summary>
/// The switches a monitor rule requires. A bool is the value that switch has to have, whether or not it has it
/// already, and null leaves the switch as Windows has it.
/// </summary>
public readonly record struct DisplayColorGoal(bool? Hdr, bool? Wide, string? Unsupported);

/// <summary>
/// The HDR and wide color switches Winland changed on one display, and the values they had before, so they can be
/// put back. <see cref="Split"/> is true when wide color had a switch of its own.
/// </summary>
public sealed record DisplayColorOriginal(string Device, bool Hdr, bool Wide, bool Split);

/// <summary>
/// The <c>cm</c> presets of a monitor rule, as Windows can show them. Windows has a switch for HDR and one for wide
/// color, which manages color by the display's EDID, so the wide gamut presets all turn wide color on. It has no
/// switch for the bits per channel: turning one of those on is what raises them above 8.
/// </summary>
public static class DisplayColors
{
    /// <summary><c>auto</c> leaves the display as Windows has it.</summary>
    public const string Auto = "auto";

    /// <summary>The preset a monitor reports when its rule names none.</summary>
    public const string StandardPreset = "srgb";

    private static readonly string[] Presets = [Auto, StandardPreset, "wide", "edid", "dcip3", "dp3", "adobe", "hdr", "hdredid"];

    public static bool IsPreset(string text) => Array.Exists(Presets, preset => preset.Equals(text, StringComparison.OrdinalIgnoreCase));

    public static DisplayColorMode ModeOf(string preset) => preset.ToLowerInvariant() switch
    {
        "hdr" or "hdredid" => DisplayColorMode.HighDynamicRange,
        "wide" or "edid" or "dcip3" or "dp3" or "adobe" => DisplayColorMode.Wide,
        _ => DisplayColorMode.Standard,
    };

    /// <summary>What to turn so the display shows <paramref name="mode"/>. A display that shows it already turns nothing.</summary>
    public static DisplayColorPlan Plan(DisplayColorMode mode, DisplayColorSupport display) => mode switch
    {
        DisplayColorMode.HighDynamicRange when !display.HdrSupported => new(null, null, "HDR"),
        DisplayColorMode.HighDynamicRange => new(display.HdrOn ? null : true, null, null),
        DisplayColorMode.Wide when !display.WideSupported => new(display.HdrOn ? false : null, null, "wide color"),
        DisplayColorMode.Wide => new(display.HdrOn ? false : null, display.WideOn ? null : true, null),
        _ => new(display.HdrOn ? false : null, display.WideOn ? false : null, null),
    };

    /// <summary>
    /// The support a rule's overrides leave. -1 says the display cannot, 1 says it can, and 0 uses
    /// <paramref name="display"/>. HDR requires wide color, except before the two switches exist, where wide color has
    /// no bit of its own and only an explicit -1 takes HDR away.
    /// </summary>
    public static DisplayColorSupport Effective(DisplayColorReading display, int wideOverride, int hdrOverride)
    {
        var wide = wideOverride switch
        {
            < 0 => false,
            > 0 => true,
            _ => display.Split && display.WideSupported,
        };
        var hdr = hdrOverride switch
        {
            < 0 => false,
            > 0 => true,
            _ => display.HdrSupported,
        };
        if (wideOverride < 0 || (display.Split && !wide))
            hdr = false;

        return new DisplayColorSupport(hdr, wide, display.HdrOn, display.WideOn);
    }

    /// <summary>The switches <paramref name="request"/> requires of <paramref name="display"/>.</summary>
    public static DisplayColorGoal Intend(DisplayColorRequest request, DisplayColorReading display)
    {
        var support = Effective(display, request.WideOverride, request.HdrOverride);
        var preset = request.Preset.Length == 0 ? Auto : request.Preset.ToLowerInvariant();
        if (preset == StandardPreset)
            return new DisplayColorGoal(false, false, request.TenBit ? "10-bit color" : null);

        if (preset == "hdr")
            return support.HdrSupported ? new DisplayColorGoal(true, null, null) : new DisplayColorGoal(null, null, "HDR");

        // hdredid is HDR when the display can show it, and standard color otherwise.
        if (preset == "hdredid")
            return support.HdrSupported ? new DisplayColorGoal(true, null, null) : new DisplayColorGoal(false, false, null);

        if (preset is "wide" or "edid" or "dcip3" or "dp3" or "adobe")
        {
            if (!display.Split || !support.WideSupported)
                return new DisplayColorGoal(false, null, "wide color");

            return new DisplayColorGoal(false, true, null);
        }

        // auto, or no preset. Only 10-bit asks for a switch, and wide color keeps the picture out of HDR.
        if (!request.TenBit)
            return new DisplayColorGoal(null, null, null);

        if (display.Split && support.WideSupported)
            return new DisplayColorGoal(null, true, null);

        if (support.HdrSupported)
            return new DisplayColorGoal(true, null, null);

        return new DisplayColorGoal(null, null, "10-bit color");
    }

    /// <summary>Why <paramref name="goal"/> cannot be shown, or null when it can.</summary>
    public static string? Explain(string device, DisplayColorRequest request, DisplayColorGoal goal)
    {
        if (goal.Unsupported is not string missing)
            return null;

        if (request.TenBit && request.Preset.Equals(StandardPreset, StringComparison.OrdinalIgnoreCase))
            return $"The display {device} cannot show 10-bit color while cm srgb is set, because Windows only raises bit depth with HDR or wide color.";

        if (request.Preset.Length == 0 || request.Preset.Equals(Auto, StringComparison.OrdinalIgnoreCase))
            return $"The display {device} cannot show {missing}.";

        return $"The display {device} cannot show {missing}, which cm {request.Preset} asks for.";
    }

    /// <summary>
    /// The bit depth the monitors reply should name. <c>srgb</c> stays at 8 bits when a depth is known: Windows only
    /// raises the depth by turning HDR or wide color on, which <c>srgb</c> turns off. An unknown depth stays unknown.
    /// </summary>
    public static int ReportedBits(int bits, string? preset, bool tenBit, DisplayColorSupport support)
    {
        var name = preset?.ToLowerInvariant() ?? "";
        if (name == StandardPreset)
            return bits > 0 ? 8 : 0;

        var hdrPreset = name is "hdr" or "hdredid";
        var widePreset = name is "wide" or "edid" or "dcip3" or "dp3" or "adobe";
        var high = (hdrPreset && support.HdrSupported)
            || (widePreset && support.WideSupported)
            || (tenBit && (support.HdrSupported || support.WideSupported));
        return high ? Math.Max(bits, 10) : bits;
    }

    /// <summary>The monitors reply's <c>currentFormat</c> for a bit depth. Empty when the depth is not known.</summary>
    public static string Format(int bits) => bits switch
    {
        >= 10 => "XRGB2101010",
        > 0 => "XRGB8888",
        _ => "",
    };
}
