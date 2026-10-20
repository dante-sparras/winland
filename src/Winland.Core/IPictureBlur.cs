namespace Winland;

/// <summary>Blurs one opaque picture with the pinned release's dual Kawase blur. A GPU blur and the CPU blur both implement it.</summary>
public interface IPictureBlur
{
    /// <summary>
    /// Blurs <paramref name="source"/>, <paramref name="width"/> by <paramref name="height"/> opaque 0xAARRGGBB pixels, into
    /// <paramref name="destination"/>. Returns false when it cannot, so the caller can try another implementation.
    /// </summary>
    bool TryApply(ReadOnlySpan<uint> source, int width, int height, BlurTuning tuning, Span<uint> destination);
}

/// <summary><see cref="BlurPipeline"/> behind <see cref="IPictureBlur"/>.</summary>
public sealed class CpuPictureBlur : IPictureBlur
{
    /// <inheritdoc />
    public bool TryApply(ReadOnlySpan<uint> source, int width, int height, BlurTuning tuning, Span<uint> destination)
    {
        if (tuning is null || width <= 0 || height <= 0 || source.Length < (long)width * height || destination.Length < (long)width * height)
            return false;

        BlurPipeline.Blur(source, width, height, tuning).AsSpan().CopyTo(destination);
        return true;
    }
}
