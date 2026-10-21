namespace Winland.Domain;

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

    // A curve that does not read warns. An empty one turns the curve off.
    private static readonly OptionValue<string> Curve = OptionValue.Of(
        "str",
        (string text, out string points) =>
        {
            OptionValue.Text.Read(text, out points);
            return points.Length == 0 || ScrollCurve.Parse(points) is not null;
        },
        OptionValue.Text.Json);

    internal static OptionSet<InputOptions> Options { get; } = new(
    [
        Of("input:natural_scroll", OptionValue.Bool, o => o.NaturalScroll == true, (o, v) => o with { NaturalScroll = v }),
        Of("input:scroll_points", Curve, o => o.ScrollPoints ?? "", (o, v) => o with { ScrollPoints = v }),
        Of("input:rotation", OptionValue.Int(0, 359), o => o.Rotation, (o, v) => o with { Rotation = v }),
        Of("input:emulate_discrete_scroll", OptionValue.Int(0, 2, DiscreteScrollNames), o => o.EmulateDiscreteScroll, (o, v) => o with { EmulateDiscreteScroll = v }),
        Of("input:off_window_axis_events", OptionValue.Int(0, 3, OffWindowAxisNames), o => o.OffWindowAxisEvents, (o, v) => o with { OffWindowAxisEvents = v }),
        Of("input:special_fallthrough", OptionValue.Bool, o => o.SpecialFallthrough, (o, v) => o with { SpecialFallthrough = v }),
        Of("input:follow_mouse", OptionValue.Int(0, 3, FollowMouseNames), o => o.FollowMouse, (o, v) => o with { FollowMouse = v }),
        Of("input:follow_mouse_threshold", OptionValue.Number(), o => o.FollowMouseThreshold, (o, v) => o with { FollowMouseThreshold = v }),
        Of("input:follow_mouse_shrink", OptionValue.Int(0, 300), o => o.FollowMouseShrink, (o, v) => o with { FollowMouseShrink = v }),
        Of("input:mouse_refocus", OptionValue.Bool, o => o.MouseRefocus, (o, v) => o with { MouseRefocus = v }),
        Of("input:float_switch_override_focus", OptionValue.Int(0, 2), o => o.FloatSwitchOverrideFocus, (o, v) => o with { FloatSwitchOverrideFocus = v }),
        Of("input:scroll_method", OptionValue.OneOf("2fg", "edge", "on_button_down", "no_scroll"), o => o.ScrollMethod ?? "", (o, v) => o with { ScrollMethod = v }),
        Of("input:scroll_button", OptionValue.Int(0, 300), o => o.ScrollButton, (o, v) => o with { ScrollButton = v }),
        Of("input:scroll_button_lock", OptionValue.Bool, o => o.ScrollButtonLock, (o, v) => o with { ScrollButtonLock = v }),
        Of("input:focus_on_close", OptionValue.Int(0, 2, FocusOnCloseNames), o => o.FocusOnClose, (o, v) => o with { FocusOnClose = v }),
    ]);

    public static InputOptions From(ConfigDocument document) => Options.From(Documented, document);

    private static Option<InputOptions> Of<T>(string name, OptionValue<T> value, Func<InputOptions, T> get, Func<InputOptions, T, InputOptions> set) =>
        Option<InputOptions>.Of(name, value, get, set);
}
