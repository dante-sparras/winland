using System.Globalization;

namespace Winland;

/// <summary>
/// Input options Winland reads for focus, scrolling, and mouse motion. Keys that would write a Windows setting are
/// not here: the parser warns and skips them.
/// </summary>
public sealed record InputOptions
{
    public bool? NaturalScroll { get; init; }

    /// <summary><c>input:scroll_points</c> as written.</summary>
    public string? ScrollPoints { get; init; }

    /// <summary>The scroll curve the mouse hook carries out, or null.</summary>
    public ScrollCurve? ScrollCurve => ScrollCurve.Parse(ScrollPoints);

    /// <summary><c>input:emulate_discrete_scroll</c>: 0 never, 1 for turns that are not whole notches, 2 for every turn.</summary>
    public int EmulateDiscreteScroll { get; init; } = 1;

    /// <summary><c>input:off_window_axis_events</c>: 0 ignore, 1 send, 2 clamp, 3 warp.</summary>
    public int OffWindowAxisEvents { get; init; } = 1;

    /// <summary><c>input:rotation</c>: the mouse's motion turns clockwise by this many degrees, 0 to 359. A touchpad move is not turned.</summary>
    public int Rotation { get; init; }

    public bool SpecialFallthrough { get; init; }

    public int FollowMouse { get; init; } = 1;

    public double FollowMouseThreshold { get; init; }

    public int FollowMouseShrink { get; init; }

    public bool MouseRefocus { get; init; } = true;

    public int FloatSwitchOverrideFocus { get; init; } = 1;

    public int FocusOnClose { get; init; }

    /// <summary><c>input:scroll_method</c>: <c>on_button_down</c> scrolls by moving the pointer with the scroll button held.</summary>
    public string? ScrollMethod { get; init; }

    /// <summary><c>input:scroll_button</c>: the button, by its Linux code; 0 is the middle button.</summary>
    public int ScrollButton { get; init; }

    /// <summary><c>input:scroll_button_lock</c>: a click of the scroll button turns scrolling on until the next click.</summary>
    public bool ScrollButtonLock { get; init; }

    /// <summary>The button scrolling the hook carries out.</summary>
    public ButtonScrollOptions ButtonScroll => new(
        string.Equals(ScrollMethod, "on_button_down", StringComparison.OrdinalIgnoreCase),
        $"mouse:{(ScrollButton <= 0 ? 274 : ScrollButton)}",
        ScrollButtonLock,
        NaturalScroll == true);

    public static InputOptions Documented { get; } = new();

    public bool InvertsScroll => NaturalScroll == true;

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

    private static readonly Dictionary<string, int> DiscreteScrollNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disable"] = 0,
        ["non_standard"] = 1,
        ["force_all"] = 2,
    };

    private static readonly Dictionary<string, int> OffWindowAxisNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ignore"] = 0,
        ["send"] = 1,
        ["clamp"] = 2,
        ["warp"] = 3,
    };

    private static readonly Dictionary<string, Func<InputOptions, string, InputOptions>> Appliers = new(StringComparer.Ordinal)
    {
        ["input:natural_scroll"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var natural) ? options with { NaturalScroll = natural } : options,
        ["input:scroll_points"] = (options, value) => options with { ScrollPoints = value.Trim() },
        ["input:rotation"] = (options, value) => TryInt(value, 0, 359, out var degrees) ? options with { Rotation = degrees } : options,
        ["input:emulate_discrete_scroll"] = (options, value) => TryMode(value, DiscreteScrollNames, 0, 2, out var mode) ? options with { EmulateDiscreteScroll = mode } : options,
        ["input:off_window_axis_events"] = (options, value) => TryMode(value, OffWindowAxisNames, 0, 3, out var mode) ? options with { OffWindowAxisEvents = mode } : options,
        ["input:special_fallthrough"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var fallthrough) ? options with { SpecialFallthrough = fallthrough } : options,
        ["input:follow_mouse"] = (options, value) => TryMode(value, FollowMouseNames, 0, 3, out var mode) ? options with { FollowMouse = mode } : options,
        ["input:follow_mouse_threshold"] = (options, value) => TryUnbounded(value, out var threshold) ? options with { FollowMouseThreshold = threshold } : options,
        ["input:follow_mouse_shrink"] = (options, value) => TryInt(value, 0, 300, out var shrink) ? options with { FollowMouseShrink = shrink } : options,
        ["input:mouse_refocus"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var refocus) ? options with { MouseRefocus = refocus } : options,
        ["input:float_switch_override_focus"] = (options, value) => TryInt(value, 0, 2, out var behavior) ? options with { FloatSwitchOverrideFocus = behavior } : options,
        ["input:scroll_method"] = (options, value) => options with { ScrollMethod = value.Trim() },
        ["input:scroll_button"] = (options, value) => TryInt(value, 0, 300, out var button) ? options with { ScrollButton = button } : options,
        ["input:scroll_button_lock"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var locked) ? options with { ScrollButtonLock = locked } : options,
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

    private static bool TryInt(string text, int min, int max, out int value)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
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
