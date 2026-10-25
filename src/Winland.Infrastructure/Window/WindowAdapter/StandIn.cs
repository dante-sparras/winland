using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

// A window whose size changes during a motion is given the size it rests at once, at the start, and made
// transparent. A window that shrinks keeps its larger size across or down until the motion ends, so its picture is
// cut off rather than enlarged. A window of Winland's own shows a DWM thumbnail of it, scaled evenly onto each
// frame's rectangle, until the motion ends (StandInMap has the mapping). Resized on every frame instead, a growing
// window shows the area it has not painted yet along its growing edges on almost every frame, because it takes several frames to lay
// itself out and paint a new size, and a shrinking one lays itself out again on every frame.
public sealed partial class WindowAdapter
{
    // DWM_TNP_RECTDESTINATION, DWM_TNP_RECTSOURCE, DWM_TNP_OPACITY, DWM_TNP_VISIBLE, DWM_TNP_SOURCECLIENTAREAONLY.
    private const uint ThumbnailParts = 0x1 | 0x2 | 0x4 | 0x8 | 0x10;
    private const uint ThumbnailOpacity = 0x4;

    private static readonly Lock StandInGate = new();
    private static readonly Dictionary<WindowId, StandIn> StandIns = [];

    // How long a window that grew is given to paint the area it gained, when it does not say. Windows Terminal takes
    // about 25 ms.
    private static readonly TimeSpan RepaintTime = TimeSpan.FromMilliseconds(50);

    private static bool Resizes(Rectangle bounds, Rectangle content) => content.Width != bounds.Width || content.Height != bounds.Height;

    // A frame of a motion whose window rests on content. Returns false when the caller moves the window itself.
    private static bool StandFor(WindowId window, Rectangle bounds, Rectangle content)
    {
        StandIn? standIn;
        lock (StandInGate)
            StandIns.TryGetValue(window, out standIn);

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

    private static StandIn? BeginStandIn(WindowId window, Rectangle content)
    {
        var hwnd = new HWND((nint)window.Value);
        if (!PInvoke.IsWindowVisible(hwnd) || !TryVisible(hwnd, out var visible))
            return null;

        // A window that was transparent, such as one that is opening, showed no picture to keep.
        var showed = Opacity.Wanted(window) > 0;
        var given = showed
            ? content with { Width = Math.Max(content.Width, visible.Width), Height = Math.Max(content.Height, visible.Height) }
            : content;

        var insets = InsetsFor(window, hwnd, (int)PInvoke.GetDpiForWindow(hwnd));
        var standIn = BeginThumbnailStandIn(window, hwnd, content, given, insets, visible);
        if (standIn is null)
            return null;

        if (showed && TryOuter(hwnd, out var before))
            standIn.Before = new Size(before.Width, before.Height);

        // The stand-in shows before the window is veiled, so no frame shows neither. A window its program made
        // layered cannot be veiled, and is resized on every frame as before.
        if (!Opacity.Veil(window))
        {
            EndStandIn(window, null);
            return null;
        }

        // A veiled window still has the border DWM draws around its frame, which would outline the goal size around
        // a smaller stand-in. It is taken off until the stand-in ends.
        standIn.Border = TryAttribute(hwnd, BorderColorAttribute, out var border) ? border : DefaultBorderColor;
        SetAttribute(hwnd, BorderColorAttribute, NoneBorderColor);

        SendQuick(hwnd, insets.OuterFor(given), keepSize: false);
        return standIn;
    }

    private static StandIn? BeginThumbnailStandIn(WindowId window, HWND hwnd, Rectangle content, Rectangle given, FrameInsets insets, Rectangle visible)
    {
        var overlay = StandInHost.Create();
        if (overlay.IsNull)
            return null;

        if (PInvoke.DwmRegisterThumbnail(overlay, hwnd, out var thumbnail).Failed)
        {
            StandInHost.Close(overlay);
            return null;
        }

        var standIn = new StandIn(window, overlay, thumbnail, content, new Size(given.Width, given.Height), insets);
        lock (StandInGate)
            StandIns[window] = standIn;

        ShowStandIn(standIn, visible);
        return standIn;
    }

    private static void ShowStandIn(StandIn standIn, Rectangle bounds)
    {
        // Just above the window, so whatever covers the window covers its stand-in too. A null handle is the top.
        // The border stacks on the stand-in, so a stand-in that is already next to the window stays put.
        var above = PInvoke.GetWindow(standIn.Window, GET_WINDOW_CMD.GW_HWNDPREV);
        var placed = above == standIn.Overlay ? SET_WINDOW_POS_FLAGS.SWP_NOZORDER : 0;

        // The move is waited for, so the stand-in has its rectangle before the thumbnail is fitted to it below. Posted, it
        // would land a frame after the thumbnail and show it cut off or out of place.
        _ = PInvoke.SetWindowPos(
            standIn.Overlay,
            above,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW | placed);

        // The border follows what the stand-in shows.
        StandFrames.Set(standIn.Id, bounds, standIn.Overlay);
        StandFrames.Follow(standIn.Id);

        // The window's picture scaled onto the rectangle: the window has its size for the motion, and what it has
        // painted of it is scaled evenly and cut off to fit the frame.
        Size? painted = PInvoke.DwmQueryThumbnailSourceSize(standIn.Thumbnail, out var size).Succeeded
            ? new Size(size.Width, size.Height)
            : null;

        // A window takes its new size at once and paints it a few frames later. Until it has, the area it gained is
        // unpainted, so only what it had before is its picture and its edges fill the rest. A window that paints with
        // GDI says when it has. One that draws with DirectX, as Windows Terminal does, does not, and is given a few
        // frames to.
        if (painted is Size surface && standIn.Before is Size before && (surface.Width > before.Width || surface.Height > before.Height))
        {
            standIn.Grew ??= System.Diagnostics.Stopwatch.GetTimestamp();
            if (System.Diagnostics.Stopwatch.GetElapsedTime(standIn.Grew.Value) < RepaintTime || PInvoke.GetUpdateRect(standIn.Window, out _, false))
                painted = new Size(Math.Min(surface.Width, before.Width), Math.Min(surface.Height, before.Height));
        }

        var map = StandInMap.For(bounds, standIn.Given, standIn.Insets, painted);
        var alpha = Opacity.Wanted(standIn.Id);
        ShowPiece(standIn.Thumbnail, map.Picture, alpha);

        // The edges show only while the window has not painted the area it gained, and their thumbnails are made the
        // first time they are needed.
        if (map.Right is StandInPiece right && standIn.Right == 0)
            standIn.Right = EdgeThumbnail(standIn);
        if (map.Bottom is StandInPiece bottom && standIn.Bottom == 0)
            standIn.Bottom = EdgeThumbnail(standIn);
        ShowPiece(standIn.Right, map.Right, alpha);
        ShowPiece(standIn.Bottom, map.Bottom, alpha);
    }

    private static nint EdgeThumbnail(StandIn standIn) =>
        PInvoke.DwmRegisterThumbnail(standIn.Overlay, standIn.Window, out var thumbnail).Succeeded ? thumbnail : 0;

    private static void ShowPiece(nint thumbnail, StandInPiece? piece, byte alpha)
    {
        if (thumbnail == 0)
            return;

        var source = piece?.Source ?? default;
        var destination = piece?.Destination ?? default;
        var properties = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = ThumbnailParts,
            rcDestination = new RECT(destination.X, destination.Y, destination.X + destination.Width, destination.Y + destination.Height),
            rcSource = new RECT(source.X, source.Y, source.X + source.Width, source.Y + source.Height),
            opacity = alpha,
            fVisible = piece is not null,
            fSourceClientAreaOnly = false,
        };
        _ = PInvoke.DwmUpdateThumbnailProperties(thumbnail, in properties);
    }

    private static unsafe bool TryAttribute(HWND window, DWMWINDOWATTRIBUTE attribute, out uint value)
    {
        value = 0;
        Span<byte> read = stackalloc byte[sizeof(uint)];
        if (PInvoke.DwmGetWindowAttribute(window, attribute, read).Failed)
            return false;

        value = System.Runtime.InteropServices.MemoryMarshal.Read<uint>(read);
        return true;
    }

    // A veiled window keeps the alpha it is given for later, so the thumbnail shows it meanwhile.
    private static void StandInAlpha(WindowId window, byte alpha)
    {
        StandIn? standIn;
        lock (StandInGate)
            StandIns.TryGetValue(window, out standIn);

        if (standIn is null)
            return;

        var properties = new DWM_THUMBNAIL_PROPERTIES { dwFlags = ThumbnailOpacity, opacity = alpha };
        foreach (var thumbnail in (ReadOnlySpan<nint>)[standIn.Thumbnail, standIn.Right, standIn.Bottom])
        {
            if (thumbnail != 0)
                _ = PInvoke.DwmUpdateThumbnailProperties(thumbnail, in properties);
        }
    }

    /// <summary>
    /// Shows the window again and takes its stand-in away. With <paramref name="at"/>, the window is put there
    /// first, so it does not show for a frame on the goal it was given.
    /// </summary>
    private static void EndStandIn(WindowId window, Rectangle? at)
    {
        StandIn? standIn;
        lock (StandInGate)
        {
            if (!StandIns.Remove(window, out standIn))
                return;
        }

        if (at is Rectangle bounds)
        {
            var hwnd = standIn.Window;
            Move(hwnd, InsetsFor(window, hwnd, (int)PInvoke.GetDpiForWindow(hwnd)).OuterFor(bounds));
        }

        StandFrames.Remove(window);
        StandFrames.Follow(window);
        Opacity.Unveil(window);
        if (standIn.Border is uint border)
            SetAttribute(standIn.Window, BorderColorAttribute, border);
        if (standIn.Thumbnail != 0)
        {
            foreach (var thumbnail in (ReadOnlySpan<nint>)[standIn.Thumbnail, standIn.Right, standIn.Bottom])
            {
                if (thumbnail != 0)
                    _ = PInvoke.DwmUnregisterThumbnail(thumbnail);
            }

            StandInHost.Close(standIn.Overlay);
        }
    }

    // The motion is over but the window may still go: its stand-in stops showing and the window stays veiled.
    private static void HideStandIn(WindowId window)
    {
        StandIn? standIn;
        lock (StandInGate)
            StandIns.TryGetValue(window, out standIn);

        if (standIn is null)
        {
            EndStandIn(window, null);
            return;
        }

        _ = PInvoke.ShowWindow(standIn.Overlay, SHOW_WINDOW_CMD.SW_HIDE);

        // The window goes on closing, so its border hides with the stand-in.
        StandFrames.Set(window, default, standIn.Overlay);
        StandFrames.Follow(window);
    }

    private static void EndStandIns()
    {
        WindowId[] windows;
        lock (StandInGate)
            windows = [.. StandIns.Keys];

        foreach (var window in windows)
            EndStandIn(window, null);
    }

    private sealed class StandIn(WindowId id, HWND overlay, nint thumbnail, Rectangle content, Size given, FrameInsets insets)
    {
        public WindowId Id { get; } = id;

        public HWND Window { get; } = new((nint)id.Value);

        // The window that shows the thumbnail.
        public HWND Overlay { get; } = overlay;

        // The DWM thumbnail of the window.
        public nint Thumbnail { get; } = thumbnail;

        // The thumbnails that fill the area the window gained along its right and bottom edges before it has painted
        // it, or 0 before one was needed.
        public nint Right { get; set; }

        public nint Bottom { get; set; }

        // The rectangle the window rests on.
        public Rectangle Content { get; } = content;

        // The visible size the window has during the motion: the size it rests at, or larger where it shrinks.
        public Size Given { get; } = given;

        // How far the window's outer rectangle extends past its visible frame.
        public FrameInsets Insets { get; } = insets;

        // The border color the window had, while the stand-in hides the window's border.
        public uint? Border { get; set; }

        // The window's outer size before it was given its size for the motion.
        public Size? Before { get; set; }

        // When the window first had more picture than before, as a Stopwatch timestamp.
        public long? Grew { get; set; }
    }
}
