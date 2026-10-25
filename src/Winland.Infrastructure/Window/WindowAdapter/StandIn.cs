namespace Winland.Infrastructure;

// A window whose size changes during a motion is given the size it rests at once, at the start, and made
// transparent. A window that shrinks keeps its larger size across or down until the motion ends, so its picture is
// cut off rather than enlarged. A window of Winland's own shows a DWM thumbnail of it, scaled evenly onto each
// frame's rectangle, until the motion ends (StandInMap has the mapping). Resized on every frame instead, a growing
// window shows the area it has not painted yet along its growing edges on almost every frame, because it takes several frames to lay
// itself out and paint a new size, and a shrinking one lays itself out again on every frame.
public sealed partial class WindowAdapter
{
    private readonly Lock _standInGate = new();
    private readonly Dictionary<WindowId, StandIn> _standIns = [];

    // How long a window that grew is given to paint the area it gained, when it does not say. Windows Terminal takes
    // about 25 ms.
    private const double RepaintMilliseconds = 50;

    private static bool Resizes(Rectangle bounds, Rectangle content) => content.Width != bounds.Width || content.Height != bounds.Height;

    // A frame of a motion whose window rests on content. Returns false when the caller moves the window itself.
    private bool StandFor(WindowId window, Rectangle bounds, Rectangle content)
    {
        StandIn? standIn;
        lock (_standInGate)
            _standIns.TryGetValue(window, out standIn);

        // A motion that replaced the one the stand-in was made for starts again from this frame.
        if (standIn is not null && standIn.Content != content)
        {
            EndStandIn(window, bounds);
            standIn = null;
        }

        if (standIn is null)
        {
            if (!Resizes(bounds, content))
                return false;

            standIn = BeginStandIn(window, content);
            if (standIn is null)
                return false;
        }

        ShowStandIn(standIn, bounds);
        return true;
    }

    private StandIn? BeginStandIn(WindowId window, Rectangle content)
    {
        if (!_desktop.Shown(window) || !TryVisible(window, out var visible))
            return null;

        // A window that was transparent, such as one that is opening, showed no picture to keep.
        var showed = _opacity.Wanted(window) > 0;
        var given = showed
            ? content with { Width = Math.Max(content.Width, visible.Width), Height = Math.Max(content.Height, visible.Height) }
            : content;

        var insets = InsetsFor(window, _desktop.Dpi(window));
        var standIn = BeginThumbnailStandIn(window, content, given, insets, visible);
        if (standIn is null)
            return null;

        if (showed && TryOuter(window, out var before))
            standIn.Before = new Size(before.Width, before.Height);

        // The stand-in shows before the window is veiled, so no frame shows neither. A window its program made
        // layered cannot be veiled, and is resized on every frame as before.
        if (!_opacity.Veil(window))
        {
            EndStandIn(window, null);
            return null;
        }

        // A veiled window still has the border DWM draws around its frame, which would outline the goal size around
        // a smaller stand-in. It is taken off until the stand-in ends.
        standIn.BorderOff = true;
        _desktop.SetAttribute(window, BorderColorAttribute, NoneBorderColor);

        SendQuick(window, insets.OuterFor(given), keepSize: false);
        return standIn;
    }

    private StandIn? BeginThumbnailStandIn(WindowId window, Rectangle content, Rectangle given, FrameInsets insets, Rectangle visible)
    {
        var surface = _desktop.OpenStandIn(window);
        if (surface is null)
            return null;

        var standIn = new StandIn(window, surface, content, new Size(given.Width, given.Height), insets);
        lock (_standInGate)
            _standIns[window] = standIn;

        ShowStandIn(standIn, visible);
        return standIn;
    }

    private void ShowStandIn(StandIn standIn, Rectangle bounds)
    {
        var surface = standIn.Surface;

        // The move is waited for, so the stand-in has its rectangle before the thumbnail is fitted to it below. Posted, it
        // would land a frame after the thumbnail and show it cut off or out of place.
        surface.Place(bounds);

        // The border follows what the stand-in shows.
        StandFrames.Set(standIn.Id, bounds, surface.Handle);
        StandFrames.Follow(standIn.Id);

        // The window's picture scaled onto the rectangle: the window has its size for the motion, and what it has
        // painted of it is scaled evenly and cut off to fit the frame.
        var painted = surface.PictureSize();

        // A window takes its new size at once and paints it a few frames later. Until it has, the area it gained is
        // unpainted, so only what it had before is its picture and its edges fill the rest. A window that paints with
        // GDI says when it has. One that draws with DirectX, as Windows Terminal does, does not, and is given a few
        // frames to.
        if (painted is Size grown && standIn.Before is Size before && (grown.Width > before.Width || grown.Height > before.Height))
        {
            var now = _clock();
            standIn.Grew ??= now;
            if (now - standIn.Grew.Value < RepaintMilliseconds || _desktop.Repainting(standIn.Id))
                painted = new Size(Math.Min(grown.Width, before.Width), Math.Min(grown.Height, before.Height));
        }

        var map = StandInMap.For(bounds, standIn.Given, standIn.Insets, painted);
        var alpha = _opacity.Wanted(standIn.Id);

        // The edges show only while the window has not painted the area it gained.
        surface.Show(StandInSlot.Picture, map.Picture, alpha);
        surface.Show(StandInSlot.Right, map.Right, alpha);
        surface.Show(StandInSlot.Bottom, map.Bottom, alpha);
    }

    // A veiled window keeps the alpha it is given for later, so the thumbnail shows it meanwhile.
    private void StandInAlpha(WindowId window, byte alpha)
    {
        StandIn? standIn;
        lock (_standInGate)
            _standIns.TryGetValue(window, out standIn);

        standIn?.Surface.SetAlpha(alpha);
    }

    /// <summary>
    /// Shows the window again and takes its stand-in away. With <paramref name="at"/>, the window is put there
    /// first, so it does not show for a frame on the goal it was given.
    /// </summary>
    private void EndStandIn(WindowId window, Rectangle? at)
    {
        StandIn? standIn;
        lock (_standInGate)
        {
            if (!_standIns.Remove(window, out standIn))
                return;
        }

        if (at is Rectangle bounds)
            Move(window, InsetsFor(window, _desktop.Dpi(window)).OuterFor(bounds));

        StandFrames.Remove(window);
        StandFrames.Follow(window);
        _opacity.Unveil(window);
        // DWM does not say what border a window has, so it gets the one Winland gives it: none once it is painted.
        if (standIn.BorderOff)
            _desktop.SetAttribute(window, BorderColorAttribute, _dwmPainted.ContainsKey(window) ? NoneBorderColor : DefaultBorderColor);

        standIn.Surface.Close();
    }

    // The motion is over but the window may still go: its stand-in stops showing and the window stays veiled.
    private void HideStandIn(WindowId window)
    {
        StandIn? standIn;
        lock (_standInGate)
            _standIns.TryGetValue(window, out standIn);

        if (standIn is null)
        {
            EndStandIn(window, null);
            return;
        }

        standIn.Surface.Hide();

        // The window goes on closing, so its border hides with the stand-in.
        StandFrames.Set(window, default, standIn.Surface.Handle);
        StandFrames.Follow(window);
    }

    private void EndStandIns()
    {
        WindowId[] windows;
        lock (_standInGate)
            windows = [.. _standIns.Keys];

        foreach (var window in windows)
            EndStandIn(window, null);
    }

    private sealed class StandIn(WindowId id, IStandInSurface surface, Rectangle content, Size given, FrameInsets insets)
    {
        public WindowId Id { get; } = id;

        // Winland's window that shows the thumbnails of the window.
        public IStandInSurface Surface { get; } = surface;

        // The rectangle the window rests on.
        public Rectangle Content { get; } = content;

        // The visible size the window has during the motion: the size it rests at, or larger where it shrinks.
        public Size Given { get; } = given;

        // How far the window's outer rectangle extends past its visible frame.
        public FrameInsets Insets { get; } = insets;

        // Whether the stand-in took the window's border off.
        public bool BorderOff { get; set; }

        // The window's outer size before it was given its size for the motion.
        public Size? Before { get; set; }

        // When the window first had more picture than before, in the clock's milliseconds.
        public double? Grew { get; set; }
    }
}
