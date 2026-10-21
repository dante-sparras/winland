namespace Winland;

/// <summary>What a motion blur draws: the box one frame ago, the box now, and how many pictures are averaged between them.</summary>
internal readonly record struct MotionBlurState(EffectBox Previous, EffectBox Current, int Samples)
{
    /// <summary>The box that holds both.</summary>
    internal EffectBox Extents => Previous.Union(Current);

    /// <summary>
    /// The box of sample <paramref name="index"/>, from <see cref="Current"/> at 0 toward <see cref="Previous"/>. The blur
    /// averages the window drawn in each of its <see cref="Samples"/> boxes.
    /// </summary>
    internal EffectBox Sample(int index)
    {
        var t = (double)index / Samples;
        return new EffectBox(
            Current.X + (Previous.X - Current.X) * t,
            Current.Y + (Previous.Y - Current.Y) * t,
            Current.Width + (Previous.Width - Current.Width) * t,
            Current.Height + (Previous.Height - Current.Height) * t);
    }
}

/// <summary>
/// Remembers a window's last step so a blur can smear it. A step of two pixels or less on every edge is no motion,
/// and a step older than 100 ms is over.
/// </summary>
internal sealed class MotionBlurTrail
{
    internal const double MinEdgeDelta = 2;
    internal const double FreshMilliseconds = 100;

    /// <summary>How long after the last step Winland stops blurring on its own timer.</summary>
    internal const double ExpiryMilliseconds = 110;

    internal const int MaxSamples = 64;

    private EffectBox _previous;
    private EffectBox _current;
    private double _recordedAt;
    private bool _valid;

    /// <summary>Notes a step. A step too small to show, or an empty box, forgets the trail instead. Returns whether it was noted.</summary>
    internal bool Record(EffectBox previous, EffectBox current, double now)
    {
        var delta = Math.Max(
            Math.Max(Math.Abs(previous.X - current.X), Math.Abs(previous.Y - current.Y)),
            Math.Max(Math.Abs(previous.Right - current.Right), Math.Abs(previous.Bottom - current.Bottom)));
        if (previous == current || delta <= MinEdgeDelta || previous.IsEmpty || current.IsEmpty)
        {
            Reset();
            return false;
        }

        _previous = previous;
        _current = current;
        _recordedAt = now;
        _valid = true;
        return true;
    }

    internal void Reset()
    {
        _previous = default;
        _current = default;
        _valid = false;
    }

    /// <summary>The blur to draw at <paramref name="now"/>, or null when the last step is over or there is none.</summary>
    internal MotionBlurState? State(double now, int samples, bool allowStale = false)
    {
        if (!_valid || (!allowStale && now - _recordedAt > FreshMilliseconds))
            return null;

        return new MotionBlurState(_previous, _current, Math.Clamp(samples, 2, MaxSamples));
    }
}
