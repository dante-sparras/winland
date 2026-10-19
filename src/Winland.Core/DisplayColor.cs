namespace Winland;

/// <summary>The color mode a display shows: standard, wide color, or high dynamic range.</summary>
public enum DisplayColorMode
{
    Standard,
    Wide,
    HighDynamicRange,
}

/// <summary>A request to show a display in the color mode its monitor rule's <c>cm</c> preset names.</summary>
public readonly record struct DisplayColorRequest(string Device, string Preset)
{
    public DisplayColorMode Mode => DisplayColors.ModeOf(Preset);
}

/// <summary>What a display can show and what Windows shows on it now.</summary>
public readonly record struct DisplayColorSupport(bool HdrSupported, bool WideSupported, bool HdrOn, bool WideOn);

/// <summary>
/// The switches to turn for a color mode: HDR and wide color on or off, null to leave one alone, and why the mode
/// cannot be shown when it cannot.
/// </summary>
public readonly record struct DisplayColorPlan(bool? Hdr, bool? Wide, string? Unsupported);

/// <summary>
/// The <c>cm</c> presets of a monitor rule, as Windows can show them. Windows has a switch for HDR and one for wide
/// color, which manages color by the display's EDID, so the wide gamut presets all turn wide color on.
/// </summary>
public static class DisplayColors
{
    /// <summary><c>auto</c> leaves the display as Windows has it.</summary>
    public const string Auto = "auto";

    private static readonly string[] Presets = [Auto, "srgb", "wide", "edid", "dcip3", "dp3", "adobe", "hdr", "hdredid"];

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
}
