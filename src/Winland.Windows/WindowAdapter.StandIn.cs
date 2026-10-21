using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

// A window whose size changes during a motion is given the size it rests at once, at the start, and made
// transparent. A window of Winland's own shows a DWM thumbnail of it, scaled onto each frame's rectangle, until
// the motion ends (StandInMap has the mapping). Resized on every frame instead, a growing window shows the area
// it has not painted yet along its growing edges on almost every frame, because it takes several frames to lay
// itself out and paint a new size, and a shrinking one lays itself out again on every frame.
public sealed partial class WindowAdapter
{
    // DWM_TNP_RECTDESTINATION, DWM_TNP_RECTSOURCE, DWM_TNP_OPACITY, DWM_TNP_VISIBLE, DWM_TNP_SOURCECLIENTAREAONLY.
    private const uint ThumbnailParts = 0x1 | 0x2 | 0x4 | 0x8 | 0x10;
    private const uint ThumbnailOpacity = 0x4;

    // DWMWA_USE_IMMERSIVE_DARK_MODE, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_NONE.
    private const DWMWINDOWATTRIBUTE DarkAttribute = (DWMWINDOWATTRIBUTE)20;
    private const DWMWINDOWATTRIBUTE BackdropAttribute = (DWMWINDOWATTRIBUTE)38;
    private const uint NoBackdrop = 1;

    private static readonly Lock StandInGate = new();
    private static readonly Dictionary<WindowId, StandIn> StandIns = [];

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

        var insets = InsetsFor(window, hwnd, (int)PInvoke.GetDpiForWindow(hwnd));
        var standIn = BeginThumbnailStandIn(window, hwnd, content, insets, visible);
        if (standIn is null)
            return null;

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

        SendQuick(hwnd, insets.OuterFor(content), keepSize: false);
        return standIn;
    }

    private static StandIn? BeginThumbnailStandIn(WindowId window, HWND hwnd, Rectangle content, FrameInsets insets, Rectangle visible)
    {
        var overlay = StandInHost.Create();
        if (overlay.IsNull)
            return null;

        if (PInvoke.DwmRegisterThumbnail(overlay, hwnd, out var thumbnail).Failed)
        {
            StandInHost.Close(overlay);
            return null;
        }

        var backdrop = MoveBackdrop(overlay, hwnd);
        var standIn = new StandIn(window, overlay, thumbnail, content, insets) { Backdrop = backdrop };
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

        // The window's picture scaled onto the rectangle, the way a window that animates its size is drawn: the
        // window has its final size, and what it has painted of it is stretched to the frame.
        Size? painted = PInvoke.DwmQueryThumbnailSourceSize(standIn.Thumbnail, out var size).Succeeded
            ? new Size(size.Width, size.Height)
            : null;
        var map = StandInMap.For(bounds, new Size(standIn.Content.Width, standIn.Content.Height), standIn.Insets, painted);
        var source = map.Source;
        var destination = map.Destination;
        var properties = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = ThumbnailParts,
            rcDestination = new RECT(destination.X, destination.Y, destination.X + destination.Width, destination.Y + destination.Height),
            rcSource = new RECT(source.X, source.Y, source.X + source.Width, source.Y + source.Height),
            opacity = Opacity.Wanted(standIn.Id),
            fVisible = true,
            fSourceClientAreaOnly = false,
        };
        ShowBackdrop(standIn, properties.opacity);
        _ = PInvoke.DwmUpdateThumbnailProperties(standIn.Thumbnail, in properties);
    }

    // A thumbnail draws the window's backdrop from the wallpaper, not from the windows behind it. The stand-in
    // takes the backdrop over, so it blurs what is really behind it, and the thumbnail draws only the window.
    private static unsafe uint? MoveBackdrop(HWND overlay, HWND window)
    {
        if (!TryAttribute(window, BackdropAttribute, out var backdrop) || backdrop <= NoBackdrop)
            return null;

        if (TryAttribute(window, DarkAttribute, out var dark))
            _ = PInvoke.DwmSetWindowAttribute(overlay, DarkAttribute, &dark, sizeof(uint));

        var margins = new global::Windows.Win32.UI.Controls.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        _ = PInvoke.DwmExtendFrameIntoClientArea(overlay, in margins);
        SetBackdrop(overlay, backdrop);
        SetBackdrop(window, NoBackdrop);
        return backdrop;
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

    private static unsafe void SetBackdrop(HWND window, uint backdrop) =>
        _ = PInvoke.DwmSetWindowAttribute(window, BackdropAttribute, &backdrop, sizeof(uint));

    // The system backdrop is an opaque material that does not fade with the thumbnail, so it shows only while the window
    // is fully opaque. Otherwise a fading window would leave a gray box behind its picture.
    private static unsafe void ShowBackdrop(StandIn standIn, byte alpha)
    {
        if (standIn.Backdrop is not uint backdrop)
            return;

        var shown = alpha == 255;
        if (standIn.BackdropShown == shown)
            return;

        standIn.BackdropShown = shown;
        SetBackdrop(standIn.Overlay, shown ? backdrop : NoBackdrop);
        var margins = shown
            ? new global::Windows.Win32.UI.Controls.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 }
            : default;
        _ = PInvoke.DwmExtendFrameIntoClientArea(standIn.Overlay, in margins);
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
        _ = PInvoke.DwmUpdateThumbnailProperties(standIn.Thumbnail, in properties);
        ShowBackdrop(standIn, alpha);
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
        if (standIn.Backdrop is uint backdrop)
            SetBackdrop(standIn.Window, backdrop);
        if (standIn.Thumbnail != 0)
        {
            _ = PInvoke.DwmUnregisterThumbnail(standIn.Thumbnail);
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

    private sealed class StandIn(WindowId id, HWND overlay, nint thumbnail, Rectangle content, FrameInsets insets)
    {
        public WindowId Id { get; } = id;

        public HWND Window { get; } = new((nint)id.Value);

        // The window that shows the thumbnail.
        public HWND Overlay { get; } = overlay;

        // The DWM thumbnail of the window.
        public nint Thumbnail { get; } = thumbnail;

        // The rectangle the window rests on, which it was given at the start.
        public Rectangle Content { get; } = content;

        // How far the window's outer rectangle extends past its visible frame.
        public FrameInsets Insets { get; } = insets;

        // The backdrop the window had, while the stand-in draws it in its place.
        public uint? Backdrop { get; init; }

        // Whether the overlay shows that backdrop now.
        public bool BackdropShown { get; set; } = true;

        // The border color the window had, while the stand-in hides the window's border.
        public uint? Border { get; set; }
    }
}
