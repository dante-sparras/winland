using System.Globalization;

namespace Winland;

public enum PointerAcceleration
{
    Flat,
    Adaptive,
}

/// <summary>
/// The Windows precision touchpad settings Winland sets: tapping, tap and drag, a reversed scroll direction, how
/// sensitive the touchpad stays while typing (0, the most, never turns it off), the right-click zone, and whether a
/// two-finger tap is a right click. A null field is left alone.
/// </summary>
public readonly record struct TouchpadSettings(bool? Tap, bool? TapAndDrag, bool? ReverseScroll, int? Sensitivity, bool? RightClickZone, bool? TwoFingerTap = null)
{
    public bool Any => Tap is not null || TapAndDrag is not null || ReverseScroll is not null || Sensitivity is not null || RightClickZone is not null || TwoFingerTap is not null;
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

    public TouchpadSettings? Touchpad { get; init; }

    public bool Any =>
        KeyboardDelay is not null
        || KeyboardSpeed is not null
        || NumLock is not null
        || SwapButtons is not null
        || Acceleration is not null
        || MouseSpeed is not null
        || WheelLines is not null
        || Touchpad is not null;
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

    /// <summary>The <c>input:accel_profile</c> text as written, which <c>scroll_points</c> reads for <c>custom</c>.</summary>
    public string? AccelProfileText { get; init; }

    /// <summary><c>input:scroll_points</c> as written.</summary>
    public string? ScrollPoints { get; init; }

    /// <summary>The custom scroll curve the mouse hook carries out, or null.</summary>
    public ScrollCurve? ScrollCurve => ScrollCurve.Parse(AccelProfileText, ScrollPoints);

    /// <summary><c>input:force_no_accel</c>: pointer acceleration is off, whatever <c>accel_profile</c> says.</summary>
    public bool ForceNoAccel { get; init; }

    /// <summary><c>input:rotation</c>: the mouse's motion turns clockwise by this many degrees, 0 to 359.</summary>
    public int Rotation { get; init; }

    public bool SpecialFallthrough { get; init; }

    public int FollowMouse { get; init; } = 1;

    public double FollowMouseThreshold { get; init; }

    public int FollowMouseShrink { get; init; }

    public bool MouseRefocus { get; init; } = true;

    public int FloatSwitchOverrideFocus { get; init; } = 1;

    public int FocusOnClose { get; init; }

    public bool? TouchpadTapToClick { get; init; }

    public bool? TouchpadTapAndDrag { get; init; }

    public bool? TouchpadNaturalScroll { get; init; }

    public bool? TouchpadDisableWhileTyping { get; init; }

    public bool? TouchpadClickfinger { get; init; }

    /// <summary><c>input:touchpad:scroll_factor</c>: the touchpad's scrolling is multiplied by this, 0 to 100.</summary>
    public double TouchpadScrollFactor { get; init; } = 1;

    /// <summary><c>input:touchpad:flip_x</c>: the touchpad moves the cursor the other way across.</summary>
    public bool TouchpadFlipX { get; init; }

    /// <summary><c>input:touchpad:flip_y</c>: the touchpad moves the cursor the other way up and down.</summary>
    public bool TouchpadFlipY { get; init; }

    /// <summary><c>input:touchpad:drag_3fg</c>: 0 is off, 1 drags with three fingers and 2 with four.</summary>
    public int TouchpadDrag3fg { get; init; }

    /// <summary><c>input:touchpad:drag_lock</c>: 0 is off, 1 lets the dragged button go after a timeout and 2 at the next tap.</summary>
    public int TouchpadDragLock { get; init; }

    /// <summary><c>input:touchpad:tap_button_map</c>: <c>lrm</c> or <c>lmr</c>. Null leaves Windows' two-finger tap alone.</summary>
    public string? TouchpadTapButtonMap { get; init; }

    /// <summary>The touchpad behavior the mouse hook and the touchpad reader carry out.</summary>
    public TouchpadBehavior Touchpad => new(
        TouchpadScrollFactor,
        ScrollFactor is double factor ? InputMapping.WheelLines(factor) / 3d : null,
        TouchpadFlipX,
        TouchpadFlipY,
        TouchpadDrag3fg switch
        {
            1 => 3,
            2 => 4,
            _ => 0,
        },
        TouchpadTapButtonMap == "lmr" && TouchpadTapToClick != false,
        DragLockActive ? TouchpadDragLock : 0);

    // Winland does the tap and drag itself when it has a lock to hold, and only where tapping and tap and drag are on.
    private bool DragLockActive => TouchpadDragLock > 0 && TouchpadTapToClick != false && TouchpadTapAndDrag != false;

    /// <summary><c>input:kb_layout</c>: comma-separated XKB layout names. Null leaves the system's layouts alone.</summary>
    public string? KbLayout { get; init; }

    /// <summary><c>input:kb_variant</c>: the variant of each layout, at the same places.</summary>
    public string? KbVariant { get; init; }

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

    /// <summary><c>input:resolve_binds_by_sym</c>: binds read keys in the layout in use, not the first one.</summary>
    public bool ResolveBindsBySym { get; init; }

    /// <summary><c>input:kb_options</c>: the XKB options, comma-separated.</summary>
    public string? KbOptions { get; init; }

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
            Acceleration = ForceNoAccel ? PointerAcceleration.Flat : Acceleration,
            MouseSpeed = Sensitivity is double sensitivity ? InputMapping.MouseSpeed(sensitivity) : null,
            WheelLines = ScrollFactor is double factor ? InputMapping.WheelLines(factor) : null,
            Touchpad = TouchpadRequest(),
        };
        return request.Any ? request : null;
    }

    // libinput's natural scrolling is Windows' default direction, and its button areas are Windows' right-click zone.
    // lrm is Windows' two-finger tap to right-click; lmr turns that off, and Winland makes the two-finger tap a middle click.
    // Disabling the touchpad while typing is Windows' medium sensitivity, its default; not disabling it is the most.
    private TouchpadSettings? TouchpadRequest()
    {
        var settings = new TouchpadSettings(
            TouchpadTapToClick,
            DragLockActive ? false : TouchpadTapAndDrag,
            TouchpadNaturalScroll is bool natural ? !natural : null,
            TouchpadDisableWhileTyping is bool disable ? (disable ? 2 : 0) : null,
            TouchpadClickfinger is bool clickfinger ? !clickfinger : null,
            TouchpadTapButtonMap switch
            {
                "lrm" => true,
                "lmr" => false,
                _ => null,
            });
        return settings.Any ? settings : null;
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

    private static readonly Dictionary<string, int> Drag3fgNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disable"] = 0,
        ["3_finger"] = 1,
        ["4_finger"] = 2,
    };

    // lrm or lmr, as the pinned release's validator allows. Empty is its default, lrm, which leaves Windows alone here.
    private static string? ParseTapMap(string text)
    {
        var map = text.Trim().ToLowerInvariant();
        return map is "lrm" or "lmr" ? map : null;
    }

    private static readonly Dictionary<string, Func<InputOptions, string, InputOptions>> Appliers = new(StringComparer.Ordinal)
    {
        ["input:repeat_rate"] = (options, value) => TryInt(value, 0, 200, out var rate) ? options with { RepeatRate = rate } : options,
        ["input:repeat_delay"] = (options, value) => TryInt(value, 0, 2000, out var delay) ? options with { RepeatDelay = delay } : options,
        ["input:numlock_by_default"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var numLock) ? options with { NumLock = numLock } : options,
        ["input:left_handed"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var left) ? options with { LeftHanded = left } : options,
        ["input:natural_scroll"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var natural) ? options with { NaturalScroll = natural } : options,
        ["input:accel_profile"] = (options, value) => options with { Acceleration = ParseAcceleration(value, options.Acceleration), AccelProfileText = value.Trim() },
        ["input:scroll_points"] = (options, value) => options with { ScrollPoints = value.Trim() },
        ["input:rotation"] = (options, value) => TryInt(value, 0, 359, out var degrees) ? options with { Rotation = degrees } : options,
        ["input:force_no_accel"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var none) ? options with { ForceNoAccel = none } : options,
        ["input:sensitivity"] = (options, value) => TryFloat(value, -1, 1, out var sensitivity) ? options with { Sensitivity = sensitivity } : options,
        ["input:scroll_factor"] = (options, value) => TryFloat(value, 0, 2, out var factor) ? options with { ScrollFactor = factor } : options,
        ["input:special_fallthrough"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var fallthrough) ? options with { SpecialFallthrough = fallthrough } : options,
        ["input:follow_mouse"] = (options, value) => TryMode(value, FollowMouseNames, 0, 3, out var mode) ? options with { FollowMouse = mode } : options,
        ["input:follow_mouse_threshold"] = (options, value) => TryUnbounded(value, out var threshold) ? options with { FollowMouseThreshold = threshold } : options,
        ["input:follow_mouse_shrink"] = (options, value) => TryInt(value, 0, 300, out var shrink) ? options with { FollowMouseShrink = shrink } : options,
        ["input:mouse_refocus"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var refocus) ? options with { MouseRefocus = refocus } : options,
        ["input:float_switch_override_focus"] = (options, value) => TryInt(value, 0, 2, out var behavior) ? options with { FloatSwitchOverrideFocus = behavior } : options,
        ["input:touchpad:tap-to-click"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var tap) ? options with { TouchpadTapToClick = tap } : options,
        ["input:touchpad:tap-and-drag"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var drag) ? options with { TouchpadTapAndDrag = drag } : options,
        ["input:touchpad:natural_scroll"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var natural) ? options with { TouchpadNaturalScroll = natural } : options,
        ["input:touchpad:disable_while_typing"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var disable) ? options with { TouchpadDisableWhileTyping = disable } : options,
        ["input:touchpad:clickfinger_behavior"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var clickfinger) ? options with { TouchpadClickfinger = clickfinger } : options,
        ["input:touchpad:scroll_factor"] = (options, value) => TryFloat(value, 0, 100, out var factor) ? options with { TouchpadScrollFactor = factor } : options,
        ["input:touchpad:flip_x"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var flip) ? options with { TouchpadFlipX = flip } : options,
        ["input:touchpad:flip_y"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var flip) ? options with { TouchpadFlipY = flip } : options,
        ["input:touchpad:drag_3fg"] = (options, value) => TryMode(value, Drag3fgNames, 0, 2, out var drag) ? options with { TouchpadDrag3fg = drag } : options,
        ["input:touchpad:drag_lock"] = (options, value) => TryInt(value, 0, 2, out var hold) ? options with { TouchpadDragLock = hold } : options,
        ["input:touchpad:tap_button_map"] = (options, value) => ParseTapMap(value) is var map && (map is not null || value.Trim().Length == 0) ? options with { TouchpadTapButtonMap = map } : options,
        ["input:kb_layout"] = (options, value) => options with { KbLayout = value.Trim() },
        ["input:kb_variant"] = (options, value) => options with { KbVariant = value.Trim() },
        ["input:scroll_method"] = (options, value) => options with { ScrollMethod = value.Trim() },
        ["input:scroll_button"] = (options, value) => TryInt(value, 0, 300, out var button) ? options with { ScrollButton = button } : options,
        ["input:scroll_button_lock"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var locked) ? options with { ScrollButtonLock = locked } : options,
        ["input:resolve_binds_by_sym"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var bySym) ? options with { ResolveBindsBySym = bySym } : options,
        ["input:kb_options"] = (options, value) => options with { KbOptions = value.Trim() },
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
        if (text.TrimStart().StartsWith("custom", StringComparison.OrdinalIgnoreCase))
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
