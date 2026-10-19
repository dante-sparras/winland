using System.Globalization;

namespace Winland;

/// <summary>
/// The <c>cursorZoom</c> gesture, as the pinned release plays it. With no mode it toggles between its zoom and
/// <c>cursor:zoom_factor</c>. <c>mult</c> multiplies the zoom by its value. <c>live</c> follows a pinch: the zoom when
/// it began times how far the fingers spread, and below 1.05 no zoom. The zoom it sets lasts until the config is read.
/// </summary>
public sealed partial class Core
{
    // The zoom a cursorZoom gesture set, which replaces cursor:zoom_factor, and whether a toggle gesture is zoomed in.
    private double? _gestureZoom;
    private bool _gestureZoomed;
    private double _liveZoomBegin;
    private bool _liveZoom;

    private double CurrentZoom => _gestureZoom ?? _cursor.Behavior().Zoom.Factor;

    private void BeginCursorZoom(TrackpadGesture gesture)
    {
        _liveZoom = false;
        var value = double.TryParse(gesture.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) ? parsed : 1;
        switch (gesture.DispatchArguments)
        {
            case "live":
                // Only a pinch zooms live.
                if (!_pinching)
                    return;

                _liveZoom = true;
                _liveZoomBegin = Math.Clamp(CurrentZoom, 1, 10);
                return;
            case "mult":
                _gestureZoom = Math.Clamp(CurrentZoom * value, 1, 10);
                return;
            default:
                _gestureZoomed = !_gestureZoomed;
                _gestureZoom = Math.Clamp(_gestureZoomed ? value : _cursor.Behavior().Zoom.Factor, 1, 10);
                return;
        }
    }

    private void StepCursorZoom()
    {
        if (!_liveZoom)
            return;

        var zoom = Math.Clamp(_liveZoomBegin * _pinchScale, 1, 10);
        _gestureZoom = zoom < 1.05 ? 1 : zoom;
    }

    private void EndCursorZoom() => _liveZoom = false;

    // The cursor behavior with the zoom a gesture set. A live pinch zooms at once, with no animation.
    private CursorBehavior WithGestureZoom(CursorBehavior behavior) => _gestureZoom is double zoom
        ? behavior with { Zoom = behavior.Zoom with { Factor = zoom, Animation = _liveZoom ? null : behavior.Zoom.Animation } }
        : behavior;

    private void ForgetGestureZoom()
    {
        _gestureZoom = null;
        _gestureZoomed = false;
        _liveZoom = false;
    }
}
