namespace Winland;

/// <summary>What one frame of an effect draws.</summary>
/// <param name="Rest">Where the window's picture sits when the mesh is at rest.</param>
/// <param name="Canvas">The box that holds everything the frame draws.</param>
/// <param name="Wobbling">Whether the mesh is off its rest.</param>
/// <param name="Blur">The blur to draw, or null.</param>
internal readonly record struct EffectFrame(EffectBox Rest, EffectBox Canvas, bool Wobbling, MotionBlurState? Blur);

/// <summary>
/// The wobble and motion blur of one window, from the boxes its motion shows frame after frame and the time between them.
/// It holds no window and draws nothing, so the clock is a number the caller passes in milliseconds.
/// </summary>
internal sealed class EffectTrack
{
    /// <summary>Room round the mesh's extents, so the edge of a wobbling window is not cut.</summary>
    internal const double Padding = 4;

    private readonly WobbleMesh _mesh = new();
    private readonly MotionBlurTrail _trail = new();
    private MotionEffects _effects;
    private EffectBox? _last;
    private double _ticked;
    private bool _wobbling;

    internal EffectTrack(MotionEffects effects, double now)
    {
        _effects = effects;
        _ticked = now;
        if (effects.Wobble is { } wobble)
            _mesh.Resize(wobble.Mesh);
    }

    internal MotionEffects Effects => _effects;

    /// <summary>Whether the mesh is off its rest and still moving.</summary>
    internal bool Wobbling => _wobbling;

    /// <summary>Takes the effects a window has now, which a reload may have changed.</summary>
    internal void Use(MotionEffects effects)
    {
        _effects = effects;
        if (effects.Wobble is { } wobble)
            _mesh.Resize(wobble.Mesh);
        else
            Calm();

        if (effects.BlurSamples < 2)
            _trail.Reset();
    }

    /// <summary>The window is at <paramref name="box"/> now. <paramref name="grab"/> is where a pointer holds it, if one does.</summary>
    internal void Step(EffectBox box, double now, (double X, double Y)? grab = null)
    {
        var previous = _last;
        _last = box;
        if (previous is not { } before)
            return;

        if (_effects.BlurSamples > 1)
            _ = _trail.Record(before, box, now);

        if (_effects.Wobble is not { } wobble || before == box)
            return;

        if (WobbleMesh.Jumps(before, box))
        {
            Calm();
            return;
        }

        if (!_wobbling)
            _ticked = now;

        _mesh.Resize(wobble.Mesh);
        _mesh.Push(before, box, wobble.Intensity, grab);
        _wobbling = true;
    }

    /// <summary>Lets the mesh spring for the time since the last tick. Returns whether it is still moving.</summary>
    internal bool Tick(double now)
    {
        if (!_wobbling)
        {
            _ticked = now;
            return false;
        }

        if (_effects.Wobble is not { } wobble)
        {
            Calm();
            return false;
        }

        var seconds = Math.Max(0, now - _ticked) / 1000;
        _ticked = now;
        _mesh.Advance(wobble, seconds);
        if (_mesh.Stable(wobble))
        {
            Calm();
            return false;
        }

        return true;
    }

    /// <summary>The window stopped where it is without a step: the blur ends and the mesh keeps springing.</summary>
    internal void EndBlur() => _trail.Reset();

    /// <summary>Whether a frame still has something to draw beyond the window at rest.</summary>
    internal bool Busy(double now) => _wobbling || _trail.State(now, _effects.BlurSamples) is not null;

    /// <summary>What to draw for a window at <paramref name="box"/>.</summary>
    internal EffectFrame Frame(EffectBox box, double now)
    {
        var canvas = _wobbling ? _mesh.Extents(box).Grow(Padding) : box;
        MotionBlurState? blur = null;
        if (_effects.BlurSamples > 1)
        {
            blur = _trail.State(now, _effects.BlurSamples);
            if (blur is { } state)
                canvas = canvas.Union(state.Extents);
        }

        return new EffectFrame(box, canvas, _wobbling, blur);
    }

    /// <summary>The triangles of the window's picture over the mesh.</summary>
    internal void Triangles(EffectBox box, List<MeshVertex> triangles) => _mesh.Triangles(box, triangles);

    private void Calm()
    {
        _mesh.Reset();
        _wobbling = false;
    }
}
