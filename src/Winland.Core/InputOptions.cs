using System.Globalization;

namespace Winland;

public enum PointerAcceleration
{
    Flat,
    Adaptive,
}

public sealed record InputSettingsRequest
{
    public int? KeyboardDelay { get; init; }

    public int? KeyboardSpeed { get; init; }

    public bool? NumLock { get; init; }

    public bool? SwapButtons { get; init; }

    public PointerAcceleration? Acceleration { get; init; }

    public int? MouseSpeed { get; init; }

    public int? WheelLines { get; init; }

    public bool Any =>
        KeyboardDelay is not null
        || KeyboardSpeed is not null
        || NumLock is not null
        || SwapButtons is not null
        || Acceleration is not null
        || MouseSpeed is not null
        || WheelLines is not null;
}

public sealed record InputOptions
{
    public int? RepeatRate { get; init; }

    public int? RepeatDelay { get; init; }

    public bool? NumLock { get; init; }

    public bool? LeftHanded { get; init; }

    public bool? NaturalScroll { get; init; }

    public PointerAcceleration? Acceleration { get; init; }

    public double? Sensitivity { get; init; }

    public double? ScrollFactor { get; init; }

    public bool SpecialFallthrough { get; init; }

    public int FollowMouse { get; init; } = 1;

    public double FollowMouseThreshold { get; init; }

    public int FollowMouseShrink { get; init; }

    public bool MouseRefocus { get; init; } = true;

    public int FloatSwitchOverrideFocus { get; init; } = 1;

    public int FocusOnClose { get; init; }

    /// <summary><c>input:kb_layout</c>: comma-separated XKB layout names. Null leaves the system's layouts alone.</summary>
    public string? KbLayout { get; init; }

    /// <summary><c>input:kb_variant</c>: the variant of each layout, at the same places.</summary>
    public string? KbVariant { get; init; }

    public static InputOptions Documented { get; } = new();

    public bool InvertsScroll => NaturalScroll == true;

    public InputSettingsRequest? Request()
    {
        var request = new InputSettingsRequest
        {
            KeyboardDelay = RepeatDelay is int delay ? InputMapping.KeyboardDelay(delay) : null,
            KeyboardSpeed = RepeatRate is int rate ? InputMapping.KeyboardSpeed(rate) : null,
            NumLock = NumLock,
            SwapButtons = LeftHanded,
            Acceleration = Acceleration,
            MouseSpeed = Sensitivity is double sensitivity ? InputMapping.MouseSpeed(sensitivity) : null,
            WheelLines = ScrollFactor is double factor ? InputMapping.WheelLines(factor) : null,
        };
        return request.Any ? request : null;
    }

    private static readonly Dictionary<string, int> FollowMouseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disabled"] = 0,
        ["follow"] = 1,
        ["detached"] = 2,
        ["separate"] = 3,
    };

    private static readonly Dictionary<string, int> FocusOnCloseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["next"] = 0,
        ["cursor"] = 1,
        ["mru"] = 2,
    };

    private static readonly Dictionary<string, Func<InputOptions, string, InputOptions>> Appliers = new(StringComparer.Ordinal)
    {
        ["input:repeat_rate"] = (options, value) => TryInt(value, 0, 200, out var rate) ? options with { RepeatRate = rate } : options,
        ["input:repeat_delay"] = (options, value) => TryInt(value, 0, 2000, out var delay) ? options with { RepeatDelay = delay } : options,
        ["input:numlock_by_default"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var numLock) ? options with { NumLock = numLock } : options,
        ["input:left_handed"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var left) ? options with { LeftHanded = left } : options,
        ["input:natural_scroll"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var natural) ? options with { NaturalScroll = natural } : options,
        ["input:accel_profile"] = (options, value) => options with { Acceleration = ParseAcceleration(value, options.Acceleration) },
        ["input:sensitivity"] = (options, value) => TryFloat(value, -1, 1, out var sensitivity) ? options with { Sensitivity = sensitivity } : options,
        ["input:scroll_factor"] = (options, value) => TryFloat(value, 0, 2, out var factor) ? options with { ScrollFactor = factor } : options,
        ["input:special_fallthrough"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var fallthrough) ? options with { SpecialFallthrough = fallthrough } : options,
        ["input:follow_mouse"] = (options, value) => TryMode(value, FollowMouseNames, 0, 3, out var mode) ? options with { FollowMouse = mode } : options,
        ["input:follow_mouse_threshold"] = (options, value) => TryUnbounded(value, out var threshold) ? options with { FollowMouseThreshold = threshold } : options,
        ["input:follow_mouse_shrink"] = (options, value) => TryInt(value, 0, 300, out var shrink) ? options with { FollowMouseShrink = shrink } : options,
        ["input:mouse_refocus"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var refocus) ? options with { MouseRefocus = refocus } : options,
        ["input:float_switch_override_focus"] = (options, value) => TryInt(value, 0, 2, out var behavior) ? options with { FloatSwitchOverrideFocus = behavior } : options,
        ["input:kb_layout"] = (options, value) => options with { KbLayout = value.Trim() },
        ["input:kb_variant"] = (options, value) => options with { KbVariant = value.Trim() },
        ["input:focus_on_close"] = (options, value) => TryMode(value, FocusOnCloseNames, 0, 2, out var close) ? options with { FocusOnClose = close } : options,
    };

    internal static IReadOnlySet<string> Keywords { get; } = Appliers.Keys.ToHashSet(StringComparer.Ordinal);

    public static InputOptions From(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = Documented;
        foreach (var setting in document.Settings)
            options = Apply(options, setting);
        return options;
    }

    private static InputOptions Apply(InputOptions options, Setting setting) =>
        Appliers.TryGetValue(setting.Keyword, out var apply) ? apply(options, setting.Value) : options;

    private static PointerAcceleration? ParseAcceleration(string text, PointerAcceleration? current)
    {
        if (text.Equals("flat", StringComparison.OrdinalIgnoreCase))
            return PointerAcceleration.Flat;

        if (text.Equals("adaptive", StringComparison.OrdinalIgnoreCase))
            return PointerAcceleration.Adaptive;

        // custom is a known profile with no system equivalent. Leave acceleration untouched.
        if (text.Equals("custom", StringComparison.OrdinalIgnoreCase))
            return null;

        return current;
    }

    private static bool TryInt(string text, int min, int max, out int value)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return false;

        value = Math.Clamp(value, min, max);
        return true;
    }

    private static bool TryFloat(string text, double min, double max, out double value)
    {
        if (!TryUnbounded(text, out value))
            return false;

        value = Math.Clamp(value, min, max);
        return true;
    }

    private static bool TryUnbounded(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static bool TryMode(string text, IReadOnlyDictionary<string, int> names, int min, int max, out int value)
    {
        if (!ConfigValueSyntax.TryParseNamedInt(text, names, out var parsed))
        {
            value = 0;
            return false;
        }

        value = Math.Clamp(parsed, min, max);
        return true;
    }
}
