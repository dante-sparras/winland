using System.Globalization;

namespace Winland;

public sealed record CursorBehavior(bool HideOnKeyPress, double InactiveTimeoutSeconds)
{
    /// <summary><c>cursor:invisible</c>: the cursor is never shown.</summary>
    public bool Invisible { get; init; }

    /// <summary><c>cursor:warp_back_after_non_mouse_input</c>: a mouse move after another input moved the cursor starts where the mouse left it.</summary>
    public bool WarpBackAfterNonMouseInput { get; init; }

    /// <summary><c>cursor:hide_on_touch</c>: touch hides the cursor until the mouse moves.</summary>
    public bool HideOnTouch { get; init; } = true;

    /// <summary><c>cursor:hide_on_tablet</c>: a pen hides the cursor until the mouse moves.</summary>
    public bool HideOnTablet { get; init; }

    /// <summary><c>cursor:hotspot_padding</c>: how many logical pixels the cursor keeps from the outer edges of the monitors.</summary>
    public int HotspotPadding { get; init; }
}

public sealed record CursorOptions
{
    public int WarpOnWorkspace { get; init; }

    // -1 follows WarpOnWorkspace. Winland does not define this key; later modes are accepted.
    public int WarpOnMonitor { get; init; } = -1;

    public int WarpOnSpecial { get; init; }

    public bool NoWarps { get; init; }

    public bool PersistentWarps { get; init; }

    public double InactiveTimeoutSeconds { get; init; }

    public bool HideOnKeyPress { get; init; }

    public bool Invisible { get; init; }

    public bool WarpBackAfterNonMouseInput { get; init; }

    public bool HideOnTouch { get; init; } = true;

    public bool HideOnTablet { get; init; }

    public string DefaultMonitor { get; init; } = "";

    public int HotspotPadding { get; init; }

    public static CursorOptions Documented { get; } = new();

    public int MonitorMode() => WarpOnMonitor < 0 ? WarpOnWorkspace : WarpOnMonitor;

    public CursorBehavior Behavior() => new(HideOnKeyPress, InactiveTimeoutSeconds)
    {
        Invisible = Invisible,
        WarpBackAfterNonMouseInput = WarpBackAfterNonMouseInput,
        HideOnTouch = HideOnTouch,
        HideOnTablet = HideOnTablet,
        HotspotPadding = HotspotPadding,
    };

    private static readonly Dictionary<string, int> WorkspaceModes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disable"] = 0,
        ["enable"] = 1,
        ["force"] = 2,
    };

    private static readonly Dictionary<string, int> MonitorModes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["same_as_warp_on_change_workspace"] = -1,
        ["disable"] = 0,
        ["enable"] = 1,
        ["force"] = 2,
    };

    private static readonly Dictionary<string, Func<CursorOptions, string, CursorOptions>> Appliers = new(StringComparer.Ordinal)
    {
        ["cursor:warp_on_change_workspace"] = (options, value) => TryMode(value, WorkspaceModes, 0, 2, out var mode) ? options with { WarpOnWorkspace = mode } : options,
        ["cursor:warp_on_monitor_change"] = (options, value) => TryMode(value, MonitorModes, -1, 2, out var mode) ? options with { WarpOnMonitor = mode } : options,
        ["cursor:warp_on_toggle_special"] = (options, value) => TryMode(value, WorkspaceModes, 0, 2, out var mode) ? options with { WarpOnSpecial = mode } : options,
        ["cursor:no_warps"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var noWarps) ? options with { NoWarps = noWarps } : options,
        ["cursor:persistent_warps"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var persistent) ? options with { PersistentWarps = persistent } : options,
        ["cursor:inactive_timeout"] = (options, value) => TryTimeout(value, out var seconds) ? options with { InactiveTimeoutSeconds = seconds } : options,
        ["cursor:warp_back_after_non_mouse_input"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var back) ? options with { WarpBackAfterNonMouseInput = back } : options,
        ["cursor:hide_on_touch"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var touch) ? options with { HideOnTouch = touch } : options,
        ["cursor:hide_on_tablet"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var tablet) ? options with { HideOnTablet = tablet } : options,
        ["cursor:invisible"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var invisible) ? options with { Invisible = invisible } : options,
        ["cursor:hide_on_key_press"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var hide) ? options with { HideOnKeyPress = hide } : options,
        ["cursor:default_monitor"] = (options, value) => options with { DefaultMonitor = value.Trim() },
        ["cursor:hotspot_padding"] = (options, value) => int.TryParse(value.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var padding) ? options with { HotspotPadding = Math.Clamp(padding, 0, 20) } : options,
    };

    internal static IReadOnlySet<string> Keywords { get; } = Appliers.Keys.ToHashSet(StringComparer.Ordinal);

    public static CursorOptions From(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = Documented;
        foreach (var setting in document.Settings)
            options = Apply(options, setting);
        return options;
    }

    private static CursorOptions Apply(CursorOptions options, Setting setting) =>
        Appliers.TryGetValue(setting.Keyword, out var apply) ? apply(options, setting.Value) : options;

    private static bool TryMode(string text, IReadOnlyDictionary<string, int> names, int min, int max, out int mode)
    {
        if (!ConfigValueSyntax.TryParseNamedInt(text, names, out var parsed))
        {
            mode = 0;
            return false;
        }

        mode = Math.Clamp(parsed, min, max);
        return true;
    }

    private static bool TryTimeout(string text, out double seconds)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) || !double.IsFinite(seconds))
            return false;

        seconds = Math.Clamp(seconds, 0, 20);
        return true;
    }
}
