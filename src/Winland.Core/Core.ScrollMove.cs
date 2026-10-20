namespace Winland;

/// <summary>
/// The scrollMove gesture, as the pinned release has it: the swipe moves the scrolling Layout's tape on the focused
/// Monitor, and the fingers lifting carry it on by the swipe's speed, settle it on a column with
/// <c>gestures:scrolling:move_snap_to_grid</c>, and focus a window there.
/// </summary>
public sealed partial class Core
{
    private const double ScrollVelocityDecay = 4.0;
    private const double ScrollMaxProjection = 2.0;
    private const double ScrollVelocitySmooth = 0.35;

    private ScrollMove? _scrollMove;

    private sealed class ScrollMove(LiveWorkspace desk, double startedOffset, int? startedColumn)
    {
        public LiveWorkspace Desk { get; } = desk;

        public double StartedOffset { get; } = startedOffset;

        public int? StartedColumn { get; } = startedColumn;

        public double Velocity { get; set; }

        public long LastTime { get; set; }
    }

    private void BeginScrollMove()
    {
        var desk = CurrentWorkspace();
        if (desk.Layout is not ScrollingLayout tape)
        {
            _scrollMove = null;
            return;
        }

        var column = ActiveWindow() is WindowId active ? tape.ColumnOf(active) : null;
        _scrollMove = new ScrollMove(desk, tape.NormalizedOffset, column);
    }

    private Outcome StepScrollMove(TrackpadGesture gesture, SwipeUpdated updated)
    {
        if (_scrollMove is not { Desk.Layout: ScrollingLayout tape } move)
            return View([]);

        var delta = gesture.Scale * _gestureDirection switch
        {
            GestureDirection.Left or GestureDirection.Right or GestureDirection.Horizontal => updated.DeltaX,
            GestureDirection.Up or GestureDirection.Down or GestureDirection.Vertical => updated.DeltaY,
            _ => Math.Abs(updated.DeltaX) > Math.Abs(updated.DeltaY) ? updated.DeltaX : updated.DeltaY,
        };
        var primary = tape.ViewLength;
        if (delta == 0 || primary <= 0)
            return View([]);

        var placements = tape.MoveTape(delta);
        if (move.LastTime > 0 && updated.TimeMs > move.LastTime)
        {
            var seconds = (updated.TimeMs - move.LastTime) / 1000.0;
            var instant = -delta / primary / seconds;
            if (double.IsFinite(instant))
                move.Velocity = (move.Velocity * (1 - ScrollVelocitySmooth)) + (instant * ScrollVelocitySmooth);
        }

        if (updated.TimeMs > 0)
            move.LastTime = updated.TimeMs;

        return (IsShown(move.Desk.Id) ? View(placements) : View([])) with { Live = true };
    }

    private Outcome EndScrollMove(bool cancelled)
    {
        var move = _scrollMove;
        _scrollMove = null;
        if (move is not { Desk.Layout: ScrollingLayout tape })
            return View([]);

        var before = ActiveWindow();
        var projection = cancelled ? 0 : Math.Clamp(move.Velocity / ScrollVelocityDecay, -ScrollMaxProjection, ScrollMaxProjection);
        WindowId? focus;
        if (_swipe.MoveSnapToGrid)
        {
            var landed = tape.SnapToProjected(tape.NormalizedOffset + projection);
            if (landed is int column && landed == move.StartedColumn)
            {
                _ = tape.MoveTape((tape.NormalizedOffset - move.StartedOffset) * tape.ViewLength);
                focus = null;
            }
            else
            {
                focus = landed is int index ? tape.FocusOfColumn(index) : null;
            }
        }
        else
        {
            // The pinned release moves the tape by the projection itself here, not by that share of the view.
            _ = tape.MoveTape(projection);
            focus = tape.FirstAtCenter();
        }

        var placements = tape.TapePlacements();
        if (focus is not WindowId target || target == before)
            return IsShown(move.Desk.Id) ? View(placements) : View([]);

        TakeFocus(move.Desk, target);
        Because(FocusReason.FollowMouse);
        if (_swipe.MoveSnapCursor && move.Desk.ClientOf(target) is Client client)
            _requestedWarp = CursorWarp.Place(1, _cursor.NoWarps, persistent: false, client.Bounds, null);

        return IsShown(move.Desk.Id) ? View(placements, focus: target) : View([], focus: target);
    }
}
