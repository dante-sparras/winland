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
    public static readonly WindowProp<BorderColor?> ActiveBorderColor = new("active_border_color", PropKind.Color);
    public static readonly WindowProp<BorderColor?> InactiveBorderColor = new("inactive_border_color", PropKind.Color);
    public static readonly WindowProp<bool?> Decorate = new("decorate", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> FocusOnActivate = new("focus_on_activate", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> NoAnim = new("no_anim", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> NoBlur = new("no_blur", PropKind.Bool, IsRuleEffect: true);
    public static readonly WindowProp<bool?> NoFocus = new("no_focus", PropKind.Bool);
    public static readonly WindowProp<bool?> Opaque = new("opaque", PropKind.Bool);
    public static readonly WindowProp<long?> BorderSize = new("border_size", PropKind.Int);
    public static readonly WindowProp<long?> Rounding = new("rounding", PropKind.Int);

    public static IReadOnlyList<WindowProp> All { get; } =
    [
        Animation, MaxSize, MinSize, Opacity, OpacityInactive, ActiveBorderColor, InactiveBorderColor, Decorate,
        FocusOnActivate, NoAnim, NoBlur, NoFocus, Opaque, BorderSize, Rounding,
    ];
}

/// <summary>A window property whose value, when it is set, is a <typeparamref name="T"/>.</summary>
internal sealed record WindowProp<T>(string Name, PropKind Kind, bool IsRuleEffect = false) : WindowProp(Name, Kind, IsRuleEffect);
