namespace Winland.Domain;

/// <summary>
/// A window property of <c>getprop</c> and <c>setprop</c>: its name, how its value is read and shown, and whether a
/// window rule of the same name only sets it. This is the one list of properties; the parser, <c>setprop</c> and the
/// readers all use it.
/// </summary>
internal abstract record WindowProp(string Name, PropKind Kind, bool IsRuleEffect)
{
    public static readonly WindowProp<string?> Animation = new("animation", PropKind.Text);
    public static readonly WindowProp<PropSize?> MaxSize = new("max_size", PropKind.Size);
    public static readonly WindowProp<PropSize?> MinSize = new("min_size", PropKind.Size);
    public static readonly WindowProp<PropAlpha?> Opacity = new("opacity", PropKind.Alpha);
    public static readonly WindowProp<PropAlpha?> OpacityInactive = new("opacity_inactive", PropKind.Alpha);
    public static readonly WindowProp<PropAlpha?> OpacityFullscreen = new("opacity_fullscreen", PropKind.Alpha);
    public static readonly WindowProp<bool?> OpacityOverride = new("opacity_override", PropKind.AlphaOverride);
    public static readonly WindowProp<bool?> OpacityInactiveOverride = new("opacity_inactive_override", PropKind.AlphaOverride);
    public static readonly WindowProp<bool?> OpacityFullscreenOverride = new("opacity_fullscreen_override", PropKind.AlphaOverride);
    public static readonly WindowProp<BorderColor?> ActiveBorderColor = new("active_border_color", PropKind.Color);
    public static readonly WindowProp<BorderColor?> InactiveBorderColor = new("inactive_border_color", PropKind.Color);
    public static readonly WindowProp<bool?> AllowsInput = new("allows_input", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> Decorate = new("decorate", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> FocusOnActivate = new("focus_on_activate", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> KeepAspectRatio = new("keep_aspect_ratio", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> NearestNeighbor = new("nearest_neighbor", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> NoAnim = new("no_anim", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> NoBlur = new("no_blur", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> NoFocus = new("no_focus", PropKind.Bool);
    public static readonly WindowProp<bool?> NoMaxSize = new("no_max_size", PropKind.Bool);
    public static readonly WindowProp<bool?> NoShortcutsInhibit = new("no_shortcuts_inhibit", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> Opaque = new("opaque", PropKind.Bool);
    public static readonly WindowProp<bool?> ForceRgbx = new("force_rgbx", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> SyncFullscreen = new("sync_fullscreen", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> Immediate = new("immediate", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> RenderUnfocused = new("render_unfocused", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> NoFollowMouse = new("no_follow_mouse", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> NoScreenShare = new("no_screen_share", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> NoAutoHdr = new("no_auto_hdr", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> PersistentSize = new("persistent_size", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> StayFocused = new("stay_focused", PropKind.Bool);
    public static readonly WindowProp<long?> BorderSize = new("border_size", PropKind.Int);
    public static readonly WindowProp<long?> Rounding = new("rounding", PropKind.Int);
    public static readonly WindowProp<float?> RoundingPower = new("rounding_power", PropKind.Float, IsRuleEffect: true);
    public static readonly WindowProp<float?> ScrollMouse = new("scroll_mouse", PropKind.Float, IsRuleEffect: true);
    public static readonly WindowProp<float?> ScrollTouchpad = new("scroll_touchpad", PropKind.Float, IsRuleEffect: true);

    public static IReadOnlyList<WindowProp> All { get; } =
    [
        Animation, MaxSize, MinSize, Opacity, OpacityInactive, OpacityFullscreen, OpacityOverride, OpacityInactiveOverride,
        OpacityFullscreenOverride, ActiveBorderColor, InactiveBorderColor, AllowsInput, Decorate, FocusOnActivate,
        KeepAspectRatio, NearestNeighbor, NoAnim, NoBlur, NoFocus, NoMaxSize, NoShortcutsInhibit, Opaque, ForceRgbx,
        SyncFullscreen, Immediate, RenderUnfocused, NoFollowMouse, NoScreenShare, NoAutoHdr, PersistentSize, StayFocused,
        BorderSize, Rounding, RoundingPower, ScrollMouse, ScrollTouchpad,
    ];
}

/// <summary>A window property whose value, when it is set, is a <typeparamref name="T"/>.</summary>
internal sealed record WindowProp<T>(string Name, PropKind Kind, bool IsRuleEffect = false) : WindowProp(Name, Kind, IsRuleEffect);
