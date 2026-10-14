namespace Winland.Core.Tiling;

/// <summary>How to put one window on its tile.</summary>
public enum TileMotionKind
{
    /// <summary>The window already covers its tile.</summary>
    Hold,

    /// <summary>Glide from where the window is to the tile.</summary>
    Ease,

    /// <summary>Move the window to the tile now.</summary>
    Snap,
}

/// <summary>How fast the tile clock runs.</summary>
public enum TilePace
{
    /// <summary>Stopped. Nothing is gliding and nothing is held.</summary>
    Parked,

    /// <summary>16ms, while a button, Super, or a resize is held.</summary>
    Coast,

    /// <summary>8ms, while a window glides or is carried.</summary>
    Fine,
}

/// <summary>What one move does to a window.</summary>
public enum TileMoveKind
{
    /// <summary>Hide the window, and keep it on its tile while hidden. It is an inactive group member.</summary>
    Hide,

    /// <summary>Put the window on its tile now.</summary>
    Land,

    /// <summary>Draw the window at one sample of its glide.</summary>
    Glide,

    /// <summary>Draw the carried window where the pointer took it.</summary>
    Carry,
}

/// <summary>One window to move. Rectangles are the visible frame, the same measure as a tile.</summary>
/// <param name="Id">Window identity.</param>
/// <param name="Kind">Hide, land, or glide.</param>
/// <param name="Rect">The tile for a landing or a hidden window, or where to draw this sample of a glide.</param>
/// <param name="Raised">True when the window covers the others, as in fullscreen.</param>
public readonly record struct TileMove(long Id, TileMoveKind Kind, TileRect Rect, bool Raised = false);

/// <summary>Where the pointer is, in pixels.</summary>
/// <param name="X">Pointer x.</param>
/// <param name="Y">Pointer y.</param>
public readonly record struct TilePoint(int X, int Y);

/// <summary>A left press the mouse hook took for a Carry, or the release that ends it.</summary>
/// <param name="Down">True for the press, false for the release.</param>
/// <param name="X">Pointer x, in pixels.</param>
/// <param name="Y">Pointer y, in pixels.</param>
/// <param name="Id">The window under a press. Unused for a release.</param>
/// <param name="From">The window's visible frame at the press. Unused for a release.</param>
/// <param name="Floated">True when the pressed window is floated or Sticky, so it stays where it is let go. Unused for a release.</param>
public readonly record struct TilePress(bool Down, int X, int Y, long Id = 0, TileRect From = default, bool Floated = false);

/// <summary>What the placer knows when it asks how the tiles move.</summary>
/// <param name="Now">Clock reading, in milliseconds.</param>
/// <param name="Super">True while a Super finger is held.</param>
/// <param name="MouseDown">True while the left button is held.</param>
/// <param name="RightDown">True while the right button is held.</param>
/// <param name="Pointer">Where the pointer is on a tick, or null when Windows will not say.</param>
/// <param name="Presses">Carry presses and releases the mouse hook queued since the last tick, oldest first.</param>
public readonly record struct TileFacts(
    long Now,
    bool Super,
    bool MouseDown,
    bool RightDown = false,
    TilePoint? Pointer = null,
    IReadOnlyList<TilePress>? Presses = null);

/// <summary>How far one step went.</summary>
public enum TileStepKind
{
    /// <summary>Nothing is gliding and nothing is held. No window was read, and the placer does nothing.</summary>
    Idle,

    /// <summary>A held button keeps unchanged tiles still. No window was read.</summary>
    Held,

    /// <summary>The step went through the tiles.</summary>
    Placed,
}

/// <summary>What the placer does after one step.</summary>
/// <param name="Kind">How far the step went.</param>
/// <param name="Moves">Windows to move, in order.</param>
/// <param name="Gliding">Windows this step keeps on a glide, with or without a move. The placer restores one that is minimized or maximized.</param>
/// <param name="Pace">How fast the tile clock runs until the next step.</param>
/// <param name="Gesture">A Carry drop or a resize the shell should apply, or null. The placer raises it outside its lock.</param>
public sealed record TileStep(TileStepKind Kind, IReadOnlyList<TileMove> Moves, IReadOnlyList<long> Gliding, TilePace Pace, TileDrag? Gesture = null);

/// <summary>
/// Decides how tiles move (ADR 0047). The placer hands it the tiles, what it knows, and a way to read a window's frame,
/// and performs the moves it answers. One thread calls an instance at a time: the placer's, under its lock.
/// </summary>
public sealed class TileMotion
{
    /// <summary>Pixels of mismatch that mean a window missed the tile it was given.</summary>
    public const int RepairSlop = 24;

    /// <summary>How long a look stands when nothing has moved, in milliseconds. The safety net behind the move events.</summary>
    public const long QuietMilliseconds = 5000;

    private readonly TileReturn _return = new();
    private readonly TileCarry _carry = new();

    // The Sticky window a Carry let go past the slop. The shell answers the drop with that rectangle,
    // so the window rests there until its tile changes, and is not started home in between.
    private long? _rest;
    private long _trackId;
    private int _lastX;
    private int _lastY;
    private readonly Dictionary<long, TileRect> _settled = [];

    // The tile a window was sent to and has not been seen on yet. The shell does not arrange again while the tiles hold,
    // so the step that lands a window is often the last one before its tile changes.
    private readonly Dictionary<long, TileRect> _landed = [];
    private readonly HashSet<long> _known = [];
    private readonly HashSet<long> _shown = [];
    private readonly Dictionary<long, Leave> _leaving = [];
    private readonly HashSet<long> _live = [];
    private readonly List<TileMove> _moves = [];
    private readonly List<long> _gliding = [];
    private readonly List<ReturnWindow> _ease = [];
    private readonly HashSet<long> _covers = [];
    private IReadOnlyList<Placement> _tiles = [];
    private bool _looked;
    private int _lookedGeneration = -1;
    private long _lookedAt;
    private int _arrange;
    private readonly Dictionary<long, Refusal> _refused = [];

    // A Super hold and an idle clock return the same step every tick. The lists are never written.
    private static readonly TileStep IdleStep = new(TileStepKind.Idle, [], [], TilePace.Parked);
    private static readonly TileStep HeldCoast = new(TileStepKind.Held, [], [], TilePace.Coast);

    /// <summary>
    /// How long the window that takes a tile has to be on it before the one behind it hides, in milliseconds.
    /// A window that was just moved or resized has not drawn its new size yet, and the wallpaper would show through it.
    /// </summary>
    public const int CoverMilliseconds = 48;

    /// <summary>The longest a window waits to hide, in milliseconds, when the one that takes its tile never gets there.</summary>
    public const int LeaveMilliseconds = MotionCurve.GlideMilliseconds + CoverMilliseconds + 192;

    /// <summary>True when <paramref name="now"/> is far from the rect Winoma gave the window.</summary>
    /// <param name="target">The rect Winoma last applied.</param>
    /// <param name="now">Where the window is.</param>
    /// <returns>True when the miss is larger than <see cref="RepairSlop"/>.</returns>
    public static bool Missed(TileRect target, TileRect now) =>
        Math.Abs(target.X - now.X) > RepairSlop
        || Math.Abs(target.Y - now.Y) > RepairSlop
        || Math.Abs(target.Width - now.Width) > RepairSlop
        || Math.Abs(target.Height - now.Height) > RepairSlop;

    /// <summary>
    /// A window that already has a tile glides when that tile moves or resizes.
    /// A window that has never been tiled, or that missed the tile it was given, jumps there.
    /// A tile that did not change stays, unless the pointer dragged it. A window already gliding home keeps gliding.
    /// </summary>
    /// <param name="sameTile">The tile rect is the one already applied.</param>
    /// <param name="arrived">The window's pixels already cover that tile.</param>
    /// <param name="dragged">The pointer carried this window off its tile.</param>
    /// <param name="settled">The window has already been placed on a tile.</param>
    /// <param name="returning">The window is still easing home. The release that started it is only seen on one sample.</param>
    /// <returns>Hold, ease, or snap.</returns>
    public static TileMotionKind Choose(bool sameTile, bool arrived, bool dragged, bool settled, bool returning = false)
    {
        if (!settled || !sameTile)
        {
            return settled ? TileMotionKind.Ease : TileMotionKind.Snap;
        }

        if (dragged || returning)
        {
            return TileMotionKind.Ease;
        }

        if (!arrived)
        {
            return TileMotionKind.Snap;
        }

        return TileMotionKind.Hold;
    }

    /// <summary>
    /// True when the last look still stands, so the placer does not read a frame.
    /// The first look does not stand, and neither does a move, a generation change, a clock the motion thread has not reported on, or the end of <see cref="QuietMilliseconds"/>.
    /// </summary>
    /// <param name="generation">The window-motion generation.</param>
    /// <param name="now">Clock reading, in milliseconds.</param>
    /// <param name="moved">True when a window moved or the tiles changed since the last look.</param>
    /// <param name="watched">True once the motion thread has reported, so its events are known to arrive.</param>
    /// <returns>True when the windows are not read.</returns>
    public bool Quiet(int generation, long now, bool moved, bool watched) =>
        watched && _looked && !moved && generation == _lookedGeneration && now - _lookedAt < QuietMilliseconds;

    /// <summary>The look stood. The next quiet interval starts here.</summary>
    /// <param name="generation">The generation this look read.</param>
    /// <param name="now">Clock reading, in milliseconds.</param>
    public void Noted(int generation, long now)
    {
        _looked = true;
        _lookedGeneration = generation;
        _lookedAt = now;
    }

    /// <summary>A placement was applied, so a window that stayed on the same wrong frame is not asked again.</summary>
    public void Arranged() => _arrange++;

    /// <summary>How many windows are remembered as refusing their tile.</summary>
    public int Refused => _refused.Count;

    /// <summary>
    /// One shown window against its tile, in visible-frame pixels.
    /// A frame back on the tile is forgotten, so the next time it leaves it is asked again.
    /// The same wrong frame against the same tile, after a placement, is not a miss.
    /// A frame that changed is a miss even when the tile did not.
    /// </summary>
    /// <param name="placement">Where the window belongs.</param>
    /// <param name="frame">The window's visible frame.</param>
    /// <returns>True when the window should be placed.</returns>
    public bool OffTile(Placement placement, TileRect frame)
    {
        var home = new TileRect(placement.X, placement.Y, placement.Width, placement.Height);
        if (Near(frame, home))
        {
            _refused.Remove(placement.Id);
            return false;
        }

        if (_refused.TryGetValue(placement.Id, out var refused)
            && refused.Arrange != _arrange
            && refused.Asked.Equals(home)
            && refused.Got.Equals(frame))
        {
            return false;
        }

        _refused[placement.Id] = new Refusal(home, frame, _arrange);
        return true;
    }

    /// <summary>Forgets a refusal for a window that is no longer on the glass.</summary>
    /// <param name="live">Windows the current tiles name.</param>
    public void Keep(HashSet<long> live)
    {
        if (_refused.Count == 0)
        {
            return;
        }

        List<long>? gone = null;
        foreach (var id in _refused.Keys)
        {
            if (!live.Contains(id))
            {
                gone ??= [];
                gone.Add(id);
            }
        }

        if (gone is null)
        {
            return;
        }

        foreach (var id in gone)
        {
            _refused.Remove(id);
        }
    }

    private readonly record struct Refusal(TileRect Asked, TileRect Got, int Arrange);

    /// <summary>
    /// New tiles. A window whose tile moved or changed size glides there (ADR 0081).
    /// A window that has never landed on a tile lands at once.
    /// </summary>
    /// <param name="tiles">Where each window belongs.</param>
    /// <param name="facts">What the placer knows now.</param>
    /// <param name="frame">Reads a window's visible frame, or null when Windows will not say. Called only for the windows this step needs.</param>
    /// <returns>The moves to perform and the clock's pace.</returns>
    public TileStep Arrange(IReadOnlyList<Placement> tiles, TileFacts facts, Func<long, TileRect?> frame)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(frame);
        _tiles = tiles;
        var (kind, moves, gliding) = Place(facts, frame, repair: true);
        return new TileStep(kind, moves, gliding, Pace(facts));
    }

    /// <summary>One tick of the tile clock. Glides move on, and the clock parks once nothing is gliding or held.</summary>
    /// <param name="facts">What the placer knows now.</param>
    /// <param name="frame">Reads a window's visible frame, or null when Windows will not say. Called only for the windows this step needs.</param>
    /// <returns>The moves to perform and the clock's pace.</returns>
    public TileStep Tick(TileFacts facts, Func<long, TileRect?> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        // One sample. The drag poll, the idle return, and the coast decision must agree.
        var drag = TakeCarry(facts) ?? PollDrag(facts);
        if (drag is null && !_return.Dragging && !_return.Returning && !facts.MouseDown && _trackId == 0 && _carry.Id is null && !facts.Super && _leaving.Count == 0)
        {
            return IdleStep;
        }

        // Super alone, or a resize whose pointer is resting. The clock runs so the right button is seen, and no window is read:
        // a chord holds Super for a few hundred milliseconds, and a run of workspace jumps for seconds.
        // Either reason is a coast: nothing is gliding, carried, or waiting to hide.
        if (drag is null && !_return.Dragging && !_return.Returning && !facts.MouseDown && _carry.Id is null && _leaving.Count == 0)
        {
            return HeldCoast;
        }

        var (kind, moves, gliding) = Place(facts, frame, repair: false);
        Follow(facts, moves);
        return new TileStep(kind, moves, gliding, Pace(facts), drag);
    }

    /// <summary>The window a Carry holds, or null.</summary>
    public long? Carried => _carry.Id;

    /// <summary>New tiles, landed on in one step. A Workspace change uses this in place of a glide.</summary>
    /// <param name="tiles">Where each window belongs.</param>
    /// <param name="facts">What the placer knows now.</param>
    /// <param name="frame">Reads a window's visible frame, or null when Windows will not say. Called only for the windows this step needs.</param>
    /// <returns>The moves to perform and the clock's pace.</returns>
    public TileStep Land(IReadOnlyList<Placement> tiles, TileFacts facts, Func<long, TileRect?> frame)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(frame);
        _return.Cancel();
        _tiles = tiles;
        _landed.Clear();
        foreach (var placement in _tiles)
        {
            _settled.Remove(placement.Id);
        }

        var (kind, moves, gliding) = Place(facts, frame, repair: true);
        var pace = Pace(facts);
        foreach (var placement in _tiles)
        {
            if (Shown(placement))
            {
                Remember(placement.Id);
            }
        }

        return new TileStep(kind, moves, gliding, pace);
    }

    /// <summary>True while <paramref name="id"/> is on a glide. The tile clock moves it, so nothing else should.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>True from the step that starts the glide until the one that lands it.</returns>
    public bool IsGliding(long id) => _return.IsReturning(id);

    private TilePace Pace(TileFacts facts)
    {
        // A glide or a carry is the 8 ms clock. A hide that is only waiting for its cover stays on the 16 ms coast,
        // so the wait does not ask the system for a 1 ms timer.
        if (_return.Returning || _carry.Id is not null)
        {
            return TilePace.Fine;
        }

        return _return.Dragging || facts.MouseDown || _trackId != 0 || facts.Super || _leaving.Count > 0
            ? TilePace.Coast
            : TilePace.Parked;
    }

    /// <summary>
    /// A drag holds its window. A window whose tile moved or resized glides there,
    /// even while the click that opened or closed another window is still held.
    /// With <paramref name="repair"/> every shown tile is read, so one that was knocked off its tile is put back.
    /// A tick passes false: it reads only the tiles that are on their way, and leaves a settled one to the next arrange.
    /// </summary>
    private (TileStepKind Kind, List<TileMove> Moves, List<long> Gliding) Place(TileFacts facts, Func<long, TileRect?> frame, bool repair)
    {
        var now = facts.Now;
        var mouse = facts.MouseDown || _carry.Id is not null;
        if (mouse && !LayoutChanged())
        {
            _return.Pointer(true);
            _moves.Clear();
            _gliding.Clear();
            return (TileStepKind.Held, _moves, _gliding);
        }

        if (mouse)
        {
            _return.YieldPointer();
        }
        else
        {
            _return.Pointer(false);
        }

        var release = _return.PendingRelease;

        // A tile that rests where it was put is not read on a tick. The sample after a release reads them all, since any may have been dragged.
        // A tick that only waits for a group member to hide reads the cover and anything still gliding, not every other tile.
        var resting = !repair && !release && _leaving.Count == 0;
        var waiting = !repair && !release && _leaving.Count > 0;
        _covers.Clear();
        if (waiting)
        {
            foreach (var placement in _tiles)
            {
                if (!Shown(placement) && _leaving.ContainsKey(placement.Id) && Cover(placement) is long cover)
                {
                    _covers.Add(cover);
                }
            }
        }

        _ease.Clear();
        _moves.Clear();
        _gliding.Clear();
        var ease = _ease;
        var moves = _moves;
        List<Placement>? behind = null;
        HashSet<long>? arrivals = null;
        var opening = false;
        var left = repair && Left();
        foreach (var placement in _tiles)
        {
            if (placement.Id == _carry.Id)
            {
                continue;
            }

            if (placement.Id == _rest)
            {
                if (Unchanged(placement))
                {
                    continue;
                }

                _rest = null;
            }

            if (!Shown(placement))
            {
                behind ??= [];
                behind.Add(placement);
                continue;
            }

            if ((resting || waiting) && Unchanged(placement) && !_return.IsReturning(placement.Id)
                && !(waiting && _covers.Contains(placement.Id)))
            {
                continue;
            }

            var read = frame(placement.Id);
            var haveRect = read is not null;
            var current = read ?? default;
            var home = new TileRect(placement.X, placement.Y, placement.Width, placement.Height);
            var same = Unchanged(placement);
            var arrived = haveRect && Near(current, home);
            if (arrived && _leaving.Count + (behind?.Count ?? 0) > 0)
            {
                arrivals ??= [];
                arrivals.Add(placement.Id);
            }

            var settled = _settled.ContainsKey(placement.Id);

            // A window found on the tile it was sent to has that tile, though no step saw it arrive. It glides to the new one.
            // On the same tile it still lands below, since a landing compares exactly and this look does not.
            if (!settled && haveRect && _landed.TryGetValue(placement.Id, out var sent) && !SameTile(sent, placement) && Near(current, sent))
            {
                _settled[placement.Id] = sent;
                _landed.Remove(placement.Id);
                settled = true;
            }

            var dragged = release && haveRect && TileReturn.Dragged(home, current);
            switch (Choose(same, arrived, dragged, settled, _return.IsReturning(placement.Id)))
            {
                case TileMotionKind.Hold:
                    break;
                case TileMotionKind.Snap:
                    opening |= !settled;
                    moves.Add(new TileMove(placement.Id, TileMoveKind.Land, home, placement.Raised));
                    if (arrived)
                    {
                        Remember(placement.Id);
                    }
                    else if (!settled)
                    {
                        _landed[placement.Id] = home;
                    }

                    break;
                case TileMotionKind.Ease:
                    // Put on the tile at once, with nothing to ease: a window that could not be read, a tile a pixel or two from
                    // where the window is, which is too short a way to ease and must still be sent, and every tile a resize changes
                    // while the pointer steers it, so the edge stays under the hand and neighbors keep the edge they share.
                    if (!haveRect || (!same && (arrived || _trackId != 0)))
                    {
                        moves.Add(new TileMove(placement.Id, TileMoveKind.Land, home, placement.Raised));
                        Remember(placement.Id);
                        break;
                    }

                    // A window left the tiles, and this one takes its room. It takes it at once: under a window that fades out,
                    // so the fade uncovers it, and over the wallpaper a window that is already gone left bare.
                    if (left && TileReturn.Resizes(current, home))
                    {
                        moves.Add(new TileMove(placement.Id, TileMoveKind.Land, home, placement.Raised));
                        Remember(placement.Id);
                        break;
                    }

                    ease.Add(new ReturnWindow(placement.Id, current, home));
                    break;
            }
        }

        if (opening)
        {
            // A window just opened, and fades in over the tile it took a part of. That tile gives the part up once the new window
            // is solid above it, so nothing shows through in between.
            for (var i = 0; i < ease.Count; i++)
            {
                ease[i] = ease[i] with { HoldMilliseconds = MotionCurve.OpenFadeMilliseconds };
            }
        }

        if (ease.Count > 0 || _return.Returning)
        {
            foreach (var frameNow in _return.Step(ease, now))
            {
                moves.Add(new TileMove(frameNow.Id, TileMoveKind.Glide, frameNow.Rect, Raised(frameNow.Id)));
                if (frameNow.Settled)
                {
                    Remember(frameNow.Id);
                }
            }

            foreach (var window in ease)
            {
                if (!_return.IsReturning(window.Id))
                {
                    Remember(window.Id);
                }
            }
        }

        foreach (var window in ease)
        {
            _gliding.Add(window.Id);
        }

        // A hidden window goes after the shown ones. One that was on the glass until now waits for the window that takes its tile,
        // so the wallpaper never shows between them.
        if (behind is not null)
        {
            foreach (var placement in behind)
            {
                if (_shown.Contains(placement.Id) && !_leaving.ContainsKey(placement.Id))
                {
                    _leaving[placement.Id] = new Leave(now, -1);
                }

                if (!Waits(placement, now, arrivals))
                {
                    _leaving.Remove(placement.Id);
                    moves.Add(new TileMove(placement.Id, TileMoveKind.Hide, new TileRect(placement.X, placement.Y, placement.Width, placement.Height)));
                }
            }
        }

        DropLeft(behind);
        UpdateKnown();
        DropClosed();
        return (TileStepKind.Placed, moves, _gliding);
    }

    /// <summary>Starts and ends the carry the mouse hook queued. A release past the slop is the drop.</summary>
    private TileDrag? TakeCarry(TileFacts facts)
    {
        if (facts.Presses is null)
        {
            return null;
        }

        TileDrag? drop = null;
        foreach (var press in facts.Presses)
        {
            if (!press.Down)
            {
                if (_carry.Release(press.X, press.Y) is TileDrag let)
                {
                    drop = let;
                    _rest = _carry.Floated ? let.Id : null;
                }

                continue;
            }

            _rest = null;
            _carry.Press(press.Id, press.X, press.Y, press.From, press.Floated);
        }

        return drop;
    }

    /// <summary>Super and the right button resize the tile under the pointer as it moves.</summary>
    private TileDrag? PollDrag(TileFacts facts)
    {
        var super = facts.Super;
        var right = facts.RightDown;
        if (facts.Pointer is not TilePoint point)
        {
            return null;
        }

        if (_trackId == 0)
        {
            if (!super || !right || TileHit.At(_tiles, point.X, point.Y) is not long id)
            {
                return null;
            }

            _trackId = id;
            _lastX = point.X;
            _lastY = point.Y;
            return null;
        }

        if (!super || !right)
        {
            _trackId = 0;
            return null;
        }

        var dx = point.X - _lastX;
        var dy = point.Y - _lastY;
        if (dx == 0 && dy == 0)
        {
            return null;
        }

        _lastX = point.X;
        _lastY = point.Y;
        return new TileDrag(_trackId, false, point.X, point.Y, dx, dy);
    }

    /// <summary>Moves the carried window with the pointer.</summary>
    private void Follow(TileFacts facts, List<TileMove> moves)
    {
        if (_carry.Id is not long id || facts.Pointer is not TilePoint point || _carry.Follow(point.X, point.Y) is not TileRect rect)
        {
            return;
        }

        moves.Add(new TileMove(id, TileMoveKind.Carry, rect));
    }

    private void DropClosed()
    {
        DropClosed(_settled);
        DropClosed(_landed);
    }

    private void DropClosed(Dictionary<long, TileRect> tiles)
    {
        if (tiles.Count == 0)
        {
            return;
        }

        List<long>? gone = null;
        foreach (var id in tiles.Keys)
        {
            if (!_known.Contains(id))
            {
                gone ??= [];
                gone.Add(id);
            }
        }

        if (gone is null)
        {
            return;
        }

        foreach (var id in gone)
        {
            tiles.Remove(id);
        }
    }

    private bool LayoutChanged()
    {
        // Asked on every tick while a button is held.
        var live = _live;
        live.Clear();
        foreach (var placement in _tiles)
        {
            live.Add(placement.Id);

            // A window that landed and has not been seen there yet is on the tile it was sent to, not on a changed one.
            // Counted as a change, it would end the hold on every step, and a window dragged by its title bar would be put back under the pointer.
            if (Shown(placement) && !Unchanged(placement) && !Sent(placement))
            {
                return true;
            }
        }

        foreach (var id in _known)
        {
            if (!live.Contains(id))
            {
                return true;
            }
        }

        return Spawned();
    }

    private bool Spawned()
    {
        foreach (var placement in _tiles)
        {
            if (!_known.Contains(placement.Id))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a window the last arrange named is not among the tiles now: it closed, was sent away, or was floated.</summary>
    private bool Left()
    {
        var kept = 0;
        foreach (var placement in _tiles)
        {
            if (_known.Contains(placement.Id))
            {
                kept++;
            }
        }

        return kept < _known.Count;
    }

    private void UpdateKnown()
    {
        _known.Clear();
        _shown.Clear();
        foreach (var placement in _tiles)
        {
            _known.Add(placement.Id);
            if (Shown(placement))
            {
                _shown.Add(placement.Id);
            }
        }
    }

    /// <summary>
    /// True while <paramref name="hidden"/>, which was on the glass until now, should stay there:
    /// the window shown on its tile is still on its way, or has been there for less than <see cref="CoverMilliseconds"/>.
    /// </summary>
    private bool Waits(Placement hidden, long now, HashSet<long>? arrivals)
    {
        if (!_leaving.TryGetValue(hidden.Id, out var leave))
        {
            return false;
        }

        if (now - leave.Since >= LeaveMilliseconds || Cover(hidden) is not long cover)
        {
            return false;
        }

        if (_return.IsReturning(cover) || arrivals is null || !arrivals.Contains(cover))
        {
            _leaving[hidden.Id] = leave with { Covered = -1 };
            return true;
        }

        if (leave.Covered < 0)
        {
            _leaving[hidden.Id] = leave with { Covered = now };
            return true;
        }

        return now - leave.Covered < CoverMilliseconds;
    }

    /// <summary>The window shown on <paramref name="hidden"/>'s tile, or null.</summary>
    private long? Cover(Placement hidden)
    {
        foreach (var placement in _tiles)
        {
            if (Shown(placement)
                && placement.X == hidden.X
                && placement.Y == hidden.Y
                && placement.Width == hidden.Width
                && placement.Height == hidden.Height)
            {
                return placement.Id;
            }
        }

        return null;
    }

    /// <summary>Forgets a waiting window that is no longer one of the hidden tiles: it is shown again, or it closed.</summary>
    private void DropLeft(List<Placement>? behind)
    {
        if (_leaving.Count == 0)
        {
            return;
        }

        List<long>? gone = null;
        foreach (var id in _leaving.Keys)
        {
            var still = false;
            if (behind is not null)
            {
                foreach (var placement in behind)
                {
                    if (placement.Id == id)
                    {
                        still = true;
                        break;
                    }
                }
            }

            if (!still)
            {
                gone ??= [];
                gone.Add(id);
            }
        }

        if (gone is not null)
        {
            foreach (var id in gone)
            {
                _leaving.Remove(id);
            }
        }
    }

    /// <summary>A window that waits to hide: when it started waiting, and when its cover was first seen on the tile. Negative before that.</summary>
    private readonly record struct Leave(long Since, long Covered);

    private bool Raised(long id)
    {
        foreach (var placement in _tiles)
        {
            if (placement.Id == id)
            {
                return placement.Raised;
            }
        }

        return false;
    }

    private void Remember(long id)
    {
        foreach (var placement in _tiles)
        {
            if (placement.Id != id || !Shown(placement))
            {
                continue;
            }

            _settled[id] = new TileRect(placement.X, placement.Y, placement.Width, placement.Height);
            _landed.Remove(id);
            return;
        }
    }

    private static bool Shown(Placement placement) =>
        placement.Visible && placement.Width > 0 && placement.Height > 0;

    private bool Unchanged(Placement placement) =>
        _settled.TryGetValue(placement.Id, out var tile) && SameTile(tile, placement);

    /// <summary>True when the window was sent to this tile and has not been seen on it yet.</summary>
    private bool Sent(Placement placement) =>
        _landed.TryGetValue(placement.Id, out var tile) && SameTile(tile, placement);

    // Two tiles are Winoma's own numbers, so they are the same only when every one is. The tolerance is for a window against
    // its tile, where a window that rounds its own size shows: with it here, a tile that moved a pixel or two was never sent.
    private static bool SameTile(TileRect tile, Placement placement) =>
        tile.X == placement.X && tile.Y == placement.Y && tile.Width == placement.Width && tile.Height == placement.Height;

    private static bool Near(TileRect current, TileRect target) =>
        Math.Abs(current.X - target.X) <= TileReturn.SettleTolerance
        && Math.Abs(current.Y - target.Y) <= TileReturn.SettleTolerance
        && Math.Abs(current.Width - target.Width) <= TileReturn.SettleTolerance
        && Math.Abs(current.Height - target.Height) <= TileReturn.SettleTolerance;
}
