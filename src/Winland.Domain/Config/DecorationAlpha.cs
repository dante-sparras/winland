namespace Winland.Domain;

/// <summary>
/// Alpha for one Client. A rule's fraction multiplies the configured one.
/// The result is the window's alpha, from 0 to 255. A translucent Client shows what is behind it.
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
            ? 1f
            : input.Fullscreen ? rule.Value.Fullscreen
            : input.Active ? rule.Value.Active
            : rule.Value.Inactive;
        return ToByte(opaque ? 1f : factor * configured);
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

/// <summary>Active, inactive and fullscreen fractions from one <c>opacity</c> rule.</summary>
public readonly record struct OpacityRule(float Active, float Inactive, float Fullscreen);

/// <summary>Which opacity applies to one Client. Fullscreen wins over active and inactive.</summary>
public readonly record struct OpacityInput(bool Active, bool Fullscreen = false);
