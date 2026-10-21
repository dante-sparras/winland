namespace Winland;

/// <summary>
/// Touchpad pinches, as Winland matches them: a pinch picks a gesture once its fingers have spread or
/// closed by a tenth, <c>pinchin</c> when they spread and <c>pinchout</c> when they close, and any action then runs
/// from it as from a swipe. Its travel is the change of the pinch's scale, as Winland counts it.
/// </summary>
public sealed partial class Core
{
    // How far a pinch must spread or close before a gesture is picked, and how its scale becomes travel.
    private const double PinchPickScale = 0.1;
    private const double PinchTravel = 400;
    private const double PinchOutTravel = 1.6;

    private bool _pinching;
    private double _pinchScale = 1;
    private double _lastPinchScale = 1;

    private Outcome BeginPinch()
    {
        if (_activeGesture is not null)
            return View([]);

        _pinching = true;
        _gestureFindFailed = false;
        _pinchScale = 1;
        _lastPinchScale = 1;
        return View([]);
    }

    private Outcome UpdatePinch(PinchUpdated pinched)
    {
        if (_gestureFindFailed || !_pinching)
            return View([]);

        _pinchScale = pinched.Scale;
        if (_activeGesture is null && Math.Abs(pinched.Scale - 1) < PinchPickScale)
            return View([]);

        Outcome? begun = null;
        if (_activeGesture is null && !PickPinch(pinched.Fingers, pinched.Scale, out begun))
        {
            _gestureFindFailed = true;
            return View([]);
        }

        return UpdateActive(new SwipeUpdated(pinched.Fingers, pinched.DeltaX, pinched.DeltaY) { TimeMs = pinched.TimeMs }, begun);
    }

    private Outcome EndPinch(bool cancelled)
    {
        var ended = EndSwipe(cancelled);
        _pinching = false;
        return ended;
    }

    private bool PickPinch(int fingers, double scale, out Outcome? begun)
    {
        begun = null;
        var direction = scale < 1 ? GestureDirection.PinchOut : GestureDirection.PinchIn;
        var held = _keyState.Held;
        foreach (var gesture in _gestures)
        {
            if (gesture.Direction != GestureDirection.Pinch && gesture.Direction != direction)
                continue;

            if (gesture.Fingers != fingers || gesture.Modifiers != held)
                continue;

            // The pinch's scale starts from where it was picked, so the first step counts only what follows.
            _lastPinchScale = 1;
            begun = Activate(gesture, direction);
            return true;
        }

        return false;
    }

    // How far a step travels along the gesture's direction: the fingers' movement for a swipe, and for a pinch the
    // change of its scale, times 400, negated for pinchin and times 1.6 otherwise.
    private double Travel(double dx, double dy)
    {
        if (!_pinching)
            return TrackpadGesture.Along(_gestureDirection, dx, dy);

        var change = _lastPinchScale - _pinchScale;
        _lastPinchScale = _pinchScale;
        return (_gestureDirection == GestureDirection.PinchIn ? -change : change * PinchOutTravel) * PinchTravel;
    }
}
