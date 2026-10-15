namespace Winland;

/// <summary>Which opacity and dim apply to one Client. Fullscreen wins over active and inactive.</summary>
public readonly record struct OpacityInput(
    bool Active,
    bool Fullscreen = false,
    bool ShadowedByModal = false,
    bool SpecialOpen = false,
    bool OnSpecial = false);

/// <summary>
/// Alpha for one Client. Rule and configured fractions multiply unless the rule value is overridden.
/// Inactive dim, modal dim and special-workspace dim then multiply the same way.
/// A black tint is not drawn on the window, so dimming lowers this alpha instead.
/// </summary>
public static class DecorationAlpha
{
    public static byte Compute(DecorationOptions options, OpacityInput input, OpacityRule? rule, bool opaque)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configured = input.Fullscreen ? options.FullscreenOpacity
            : input.Active ? options.ActiveOpacity
            : options.InactiveOpacity;
        var factor = rule is null
            ? OpacityFactor.Identity
            : input.Fullscreen ? rule.Value.Fullscreen
            : input.Active ? rule.Value.Active
            : rule.Value.Inactive;
        var opacity = opaque ? 1f : factor.Apply(configured);

        var dim = 0f;
        if (!input.Active && options.DimInactive)
            dim = options.DimStrength;
        if (input.ShadowedByModal && options.DimModal)
            dim += (1f - dim) / 2f;

        var keep = 1f - dim;
        if (input.SpecialOpen && !input.OnSpecial)
            keep *= 1f - options.DimSpecial;

        return ToByte(opacity * keep);
    }

    private static byte ToByte(float fraction)
    {
        if (!float.IsFinite(fraction) || fraction <= 0f)
            return 0;

        var scaled = fraction * 255f;
        if (scaled >= 255f)
            return 255;

        return (byte)Math.Round(scaled, MidpointRounding.AwayFromZero);
    }
}
