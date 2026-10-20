namespace Winland;

/// <summary>
/// The values one Kawase pass reads. <see cref="Passes"/> is clamped to 1 through <see cref="BlurPipeline.MaxPasses"/>.
/// <see cref="Radius"/> is the tap distance. Brightness above 1 is applied while preparing the picture and brightness below 1
/// while finishing it, which is how <see cref="BlurPipeline"/> splits <see cref="BlurTuning.Brightness"/>.
/// </summary>
public readonly record struct BlurShaderParameters(
    int Passes,
    float Radius,
    float Contrast,
    float PrepareBrightness,
    float FinishBrightness,
    float Noise,
    float Vibrancy,
    float VibrancyDarkness)
{
    /// <summary>The pass values for <paramref name="tuning"/>. A non-finite number falls back to a pass that does not change that part.</summary>
    public static BlurShaderParameters From(BlurTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        var brightness = float.IsFinite(tuning.Brightness) ? tuning.Brightness : 1f;
        return new BlurShaderParameters(
            Math.Clamp(tuning.Passes, 1, BlurPipeline.MaxPasses),
            Math.Max(0, tuning.Size),
            float.IsFinite(tuning.Contrast) ? tuning.Contrast : 1f,
            Math.Max(1f, brightness),
            Math.Min(1f, brightness),
            float.IsFinite(tuning.Noise) ? tuning.Noise : 0f,
            float.IsFinite(tuning.Vibrancy) ? tuning.Vibrancy : 0f,
            float.IsFinite(tuning.VibrancyDarkness) ? tuning.VibrancyDarkness : 0f);
    }

    /// <summary>
    /// How far past the window the blur reads, in pixels. The release uses at least one pixel of tap distance, so a size of 0
    /// still spreads, and the pass count is the same clamp as <see cref="From"/>.
    /// </summary>
    public static int Spread(BlurTuning tuning)
    {
        var passes = From(tuning).Passes;
        return 2 * Math.Max(tuning.Size, 1) * ((1 << passes) - 1);
    }

    /// <summary>
    /// Writes the twelve floats a pass reads, then four unused floats, into a constant buffer of sixteen floats: radius,
    /// contrast, prepare brightness, finish brightness, noise, vibrancy, vibrancy darkness, passes, and the source and
    /// destination sizes.
    /// </summary>
    public void Write(Span<float> destination, float sourceWidth, float sourceHeight, float destinationWidth, float destinationHeight)
    {
        if (destination.Length < 16)
            throw new ArgumentException("A constant buffer holds sixteen floats.", nameof(destination));

        destination[0] = Radius;
        destination[1] = Contrast;
        destination[2] = PrepareBrightness;
        destination[3] = FinishBrightness;
        destination[4] = Noise;
        destination[5] = Vibrancy;
        destination[6] = VibrancyDarkness;
        destination[7] = Passes;
        destination[8] = sourceWidth;
        destination[9] = sourceHeight;
        destination[10] = destinationWidth;
        destination[11] = destinationHeight;
        destination[12] = 0;
        destination[13] = 0;
        destination[14] = 0;
        destination[15] = 0;
    }
}
