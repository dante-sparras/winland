namespace Winland.Core.Tiling;

/// <summary>A window the pointer may have moved, and the tile it belongs on.</summary>
/// <param name="Id">Window identity.</param>
/// <param name="Current">Where it is now, in pixels.</param>
/// <param name="Home">Where the tile wants it.</param>
/// <param name="HoldMilliseconds">How long the window stays where it is before it sets off. Zero sets off at once.</param>
public readonly record struct ReturnWindow(long Id, TileRect Current, TileRect Home, int HoldMilliseconds = 0);

/// <summary>One sample of a window on its way home.</summary>
/// <param name="Id">Window identity.</param>
/// <param name="Rect">Where to draw it for this sample.</param>
/// <param name="Settled">True on the sample that lands on the tile.</param>
public readonly record struct ReturnFrame(long Id, TileRect Rect, bool Settled = false);

/// <summary>
/// Eases a window onto its tile. The pointer holds a drag still, and a release settles back.
/// A new layout, such as a window opening or closing, eases every tile that changed size or place.
/// Both finish over <see cref="DurationMilliseconds"/>.
/// </summary>
public sealed class TileReturn
{
    /// <summary>How long a return takes, in milliseconds.</summary>
    public const int DurationMilliseconds = MotionCurve.GlideMilliseconds;

    /// <summary>Mismatch, in pixels, that still counts as already on the tile.</summary>
    public const int SettleTolerance = 2;

    /// <summary>How far, in pixels, the pointer must carry a window before a release eases it home.</summary>
    public const int DragSlop = 16;

    private readonly Dictionary<long, Motion> _motions = [];
    private readonly List<ReturnFrame> _frames = [];
    private readonly HashSet<long> _seen = [];
    private readonly List<long> _gone = [];
    private bool _down;
    private bool _arm;

    /// <summary>True while the pointer button is held.</summary>
    public bool Dragging => _down;

    /// <summary>True after the button comes up, until the next <see cref="Step"/>.</summary>
    public bool PendingRelease => _arm;

    /// <summary>True after a release until every returned window has settled.</summary>
    public bool Returning => _arm || _motions.Count > 0;

    /// <summary>True when the pointer carried a window from <paramref name="placed"/> to <paramref name="now"/>.</summary>
    /// <param name="placed">The rect Winoma last gave the window.</param>
    /// <param name="now">Where the window is.</param>
    /// <returns>True when the change is larger than <see cref="DragSlop"/>.</returns>
    public static bool Dragged(TileRect placed, TileRect now) =>
        Math.Abs(placed.X - now.X) > DragSlop
        || Math.Abs(placed.Y - now.Y) > DragSlop
        || Math.Abs(placed.Width - now.Width) > DragSlop
        || Math.Abs(placed.Height - now.Height) > DragSlop;

    /// <summary>True when <paramref name="id"/> is still easing home.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>True after the release has been sampled and until that window settles.</returns>
    public bool IsReturning(long id) => _motions.ContainsKey(id);

    /// <summary>The pointer button went down or up.</summary>
    /// <param name="down">True while the button is held.</param>
    public void Pointer(bool down)
    {
        if (down)
        {
            _down = true;
            _arm = false;
            _motions.Clear();
            return;
        }

        if (_down)
        {
            _down = false;
            _arm = true;
        }
    }

    /// <summary>Lets a layout change run while the button is still down, without treating it as a drag.</summary>
    public void YieldPointer()
    {
        _down = false;
        _arm = false;
    }

    /// <summary>Drops every ease so the next place can jump straight to the tiles.</summary>
    public void Cancel()
    {
        _down = false;
        _arm = false;
        _motions.Clear();
    }

    /// <summary>
    /// Advances every ease to <paramref name="timeMilliseconds"/>.
    /// Empty while the pointer is down, and empty on the sample that only records the start.
    /// A tile whose home changed since the last sample eases from where the window is now.
    /// </summary>
    /// <param name="windows">Windows as they are now, with the tiles they belong on.</param>
    /// <param name="timeMilliseconds">Clock reading, in the same unit as <see cref="DurationMilliseconds"/>.</param>
    /// <returns>Positions to draw for this sample.</returns>
    public IReadOnlyList<ReturnFrame> Step(IReadOnlyList<ReturnWindow> windows, long timeMilliseconds)
    {
        _frames.Clear();
        if (_down)
        {
            return _frames;
        }

        Track(windows, timeMilliseconds);
        Advance(timeMilliseconds, _frames);
        return _frames;
    }

    private void Track(IReadOnlyList<ReturnWindow> windows, long timeMilliseconds)
    {
        var released = _arm;
        _arm = false;
        _seen.Clear();
        foreach (var window in windows)
        {
            _seen.Add(window.Id);
            if (!released && _motions.TryGetValue(window.Id, out var motion))
            {
                if (NeedsReturn(motion.Home, window.Home))
                {
                    _motions[window.Id] = new Motion(window.Current, window.Home, timeMilliseconds, window.HoldMilliseconds);
                }
                else if (motion.Home != window.Home)
                {
                    // A home that moved a pixel or two is where this ease ends. It does not start over for that.
                    _motions[window.Id] = motion with { Home = window.Home };
                }

                continue;
            }

            if (NeedsReturn(window.Current, window.Home))
            {
                _motions[window.Id] = new Motion(window.Current, window.Home, timeMilliseconds, window.HoldMilliseconds);
            }
            else
            {
                _motions.Remove(window.Id);
            }
        }

        _gone.Clear();
        foreach (var id in _motions.Keys)
        {
            if (!_seen.Contains(id))
            {
                _gone.Add(id);
            }
        }

        foreach (var id in _gone)
        {
            _motions.Remove(id);
        }
    }

    private void Advance(long timeMilliseconds, List<ReturnFrame> frames)
    {
        if (_motions.Count == 0)
        {
            return;
        }

        List<long>? done = null;
        foreach (var (id, motion) in _motions)
        {
            // A window that is held starts its way late, and takes the whole time from then.
            var elapsed = timeMilliseconds - motion.StartedAt - motion.Hold;
            if (elapsed < 0)
            {
                elapsed = 0;
            }

            var settled = elapsed >= DurationMilliseconds;
            var rect = Sample(motion.From, motion.Home, elapsed);
            if (rect != motion.From || settled)
            {
                frames.Add(new ReturnFrame(id, rect, settled));
            }

            if (settled)
            {
                done ??= [];
                done.Add(id);
            }
        }

        if (done is null)
        {
            return;
        }

        foreach (var id in done)
        {
            _motions.Remove(id);
        }
    }

    /// <summary>True when the way from <paramref name="from"/> to <paramref name="to"/> changes the window's size, and not only its place.</summary>
    /// <param name="from">Where the window is.</param>
    /// <param name="to">Where its tile wants it.</param>
    public static bool Resizes(TileRect from, TileRect to) =>
        Math.Abs(from.Width - to.Width) > SettleTolerance || Math.Abs(from.Height - to.Height) > SettleTolerance;

    private static bool NeedsReturn(TileRect from, TileRect to) =>
        Math.Abs(from.X - to.X) > SettleTolerance
        || Math.Abs(from.Y - to.Y) > SettleTolerance
        || Math.Abs(from.Width - to.Width) > SettleTolerance
        || Math.Abs(from.Height - to.Height) > SettleTolerance;

    /// <summary>Eased on <see cref="MotionCurve.EaseOutQuint"/>. The window is still at <paramref name="from"/> when the clock starts, and exactly at <paramref name="to"/> when the duration elapses.</summary>
    private static TileRect Sample(TileRect from, TileRect to, long elapsedMilliseconds)
    {
        if (elapsedMilliseconds <= 0)
        {
            return from;
        }

        if (elapsedMilliseconds >= DurationMilliseconds)
        {
            return to;
        }

        var t = elapsedMilliseconds / (double)DurationMilliseconds;
        var progress = MotionCurve.EaseOutQuint(t);

        // The four edges are eased, and the size is what lies between them. A width rounded apart from its x puts the far edge
        // a pixel off the neighbor's near edge on some samples, so the gap between two gliding tiles flickers.
        var left = Lerp(from.X, to.X, progress);
        var top = Lerp(from.Y, to.Y, progress);
        var right = Lerp(from.X + from.Width, to.X + to.Width, progress);
        var bottom = Lerp(from.Y + from.Height, to.Y + to.Height, progress);
        return new TileRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Only the travel is rounded, so two edges that travel the same distance round the same way wherever they start:
    /// a window that only moves keeps its size, and an edge that does not travel stays where it is.
    /// </summary>
    private static int Lerp(int from, int to, double progress) =>
        from + (int)Math.Round((to - from) * progress, MidpointRounding.AwayFromZero);

    private readonly record struct Motion(TileRect From, TileRect Home, long StartedAt, int Hold = 0);
}
