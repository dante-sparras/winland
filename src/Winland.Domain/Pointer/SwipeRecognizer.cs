namespace Winland.Domain;

/// <summary>
/// Picks the <c>gesture</c> a touchpad swipe makes. The swipe picks one once it has moved
/// <see cref="PickDistance"/> on one axis, by its fingers, its direction and the modifiers held. A swipe that matches no
/// gesture then is ignored until the next one begins.
/// </summary>
public sealed class SwipeRecognizer
{
    /// <summary>How far a swipe moves on one axis before it picks its gesture.</summary>
    public const double PickDistance = 5;

    private bool _failed;
    private double _totalX;
    private double _totalY;

    /// <summary>The gesture this swipe picked, or null before it picks one.</summary>
    public TrackpadGesture? Active { get; private set; }

    /// <summary>Starts a swipe. A swipe whose end never came keeps its gesture.</summary>
    public void Begin()
    {
        if (Active is not null)
            return;

        _failed = false;
        _totalX = 0;
        _totalY = 0;
    }

    /// <summary>
    /// Reads one update. <paramref name="held"/> is the modifiers held now. The update that picks the gesture is also
    /// its first step.
    /// </summary>
    public SwipeStep Update(SwipeUpdated updated, IReadOnlyList<TrackpadGesture> gestures, Modifier held)
    {
        ArgumentNullException.ThrowIfNull(updated);
        ArgumentNullException.ThrowIfNull(gestures);
        if (_failed)
            return default;

        _totalX += updated.DeltaX;
        _totalY += updated.DeltaY;
        var picked = false;
        if (Active is null)
        {
            if (Math.Abs(_totalX) < PickDistance && Math.Abs(_totalY) < PickDistance)
                return default;

            Active = Pick(updated.Fingers, gestures, held);
            if (Active is null)
            {
                _failed = true;
                return default;
            }

            picked = true;
        }

        var along = Active.Scale * TrackpadGesture.Along(Active.Direction, updated.DeltaX, updated.DeltaY);
        return new SwipeStep(picked, along);
    }

    /// <summary>Ends the swipe and returns the gesture it had picked. An update after this belongs to no swipe.</summary>
    public TrackpadGesture? End()
    {
        _failed = true;
        var gesture = Active;
        Active = null;
        return gesture;
    }

    private TrackpadGesture? Pick(int fingers, IReadOnlyList<TrackpadGesture> gestures, Modifier held)
    {
        var axis = Math.Abs(_totalX) > Math.Abs(_totalY) ? GestureDirection.Horizontal : GestureDirection.Vertical;
        var direction = axis == GestureDirection.Horizontal
            ? _totalX < 0 ? GestureDirection.Left : GestureDirection.Right
            : _totalY < 0 ? GestureDirection.Up : GestureDirection.Down;
        foreach (var gesture in gestures)
        {
            if (gesture.Direction != axis && gesture.Direction != direction && gesture.Direction != GestureDirection.Swipe)
                continue;

            if (gesture.Fingers == fingers && gesture.Modifiers == held)
                return gesture;
        }

        return null;
    }
}

/// <summary>
/// What one update did. <paramref name="Picked"/> when it picked the gesture, and <paramref name="Along"/> how far it
/// moved along the gesture's direction, scaled, or null when it belongs to no gesture.
/// </summary>
public readonly record struct SwipeStep(bool Picked, double? Along);
