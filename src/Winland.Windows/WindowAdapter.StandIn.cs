using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

// A window that grows during a motion is given its goal size at the start and made transparent, and a window of
// Winland's own shows a DWM thumbnail of it in each frame's rectangle until the motion ends. Resized on every
// frame instead, the window shows the area it has not painted yet along its growing edges on almost every frame,
// because it takes several frames to lay itself out and paint a new size.
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

    private static bool Grows(Rectangle bounds, Rectangle goal) => goal.Width > bounds.Width || goal.Height > bounds.Height;

    // A frame of a motion heading for goal. Returns false when the caller moves the window itself.
    private static bool StandFor(WindowId window, Rectangle bounds, Rectangle goal)
    {
        StandIn? standIn;
        lock (StandInGate)
            StandIns.TryGetValue(window, out standIn);

        // A motion that replaced the one the stand-in was made for carries on from this frame.
        if (standIn is not null && standIn.Goal != goal)
        {
            EndStandIn(window, bounds);
            standIn = null;
        }

        if (standIn is null)
        {
            if (!Grows(bounds, goal))
                return false;

            standIn = BeginStandIn(window, goal);
            if (standIn is null)
                return false;
        }

        ShowStandIn(standIn, bounds);
        return true;
    }

    private static StandIn? BeginStandIn(WindowId window, Rectangle goal)
    {
        var hwnd = new HWND((nint)window.Value);
        if (!PInvoke.IsWindowVisible(hwnd) || !TryVisible(hwnd, out var visible))
            return null;

        var overlay = StandInHost.Create();
        if (overlay.IsNull)
            return null;

        if (PInvoke.DwmRegisterThumbnail(overlay, hwnd, out var thumbnail).Failed)
        {
            StandInHost.Close(overlay);
            return null;
        }

        var backdrop = MoveBackdrop(overlay, hwnd);
        var insets = InsetsFor(window, hwnd, (int)PInvoke.GetDpiForWindow(hwnd));
        var standIn = new StandIn(window, overlay, thumbnail, goal, new Point(insets.Left, insets.Top)) { Backdrop = backdrop };
        lock (StandInGate)
            StandIns[window] = standIn;

        // The stand-in shows before the window is veiled, so no frame shows neither. A window its program made
        // layered cannot be veiled, and is resized on every frame as before.
        ShowStandIn(standIn, visible);
        if (!Opacity.Veil(window))
        {
            EndStandIn(window, null);
            return null;
        }

        SendQuick(hwnd, insets.OuterFor(goal), keepSize: false);
        return standIn;
    }

    private static void ShowStandIn(StandIn standIn, Rectangle bounds)
    {
        // Just above the window, so whatever covers the window covers its stand-in too. A null handle is the top.
        var above = PInvoke.GetWindow(standIn.Window, GET_WINDOW_CMD.GW_HWNDPREV);
        if (above == standIn.Overlay)
            above = PInvoke.GetWindow(standIn.Overlay, GET_WINDOW_CMD.GW_HWNDPREV);

        _ = PInvoke.SetWindowPos(
            standIn.Overlay,
            above,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);

        // Unscaled and from the top left, the way a window shows while it shrinks, so the part the window has
        // not painted at its new size yet shows the stand-in's backdrop, and nothing changes size when it has.
        var width = Math.Min(bounds.Width, standIn.Goal.Width);
        var height = Math.Min(bounds.Height, standIn.Goal.Height);
        var origin = standIn.Origin;
        var properties = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = ThumbnailParts,
            rcDestination = new RECT(0, 0, width, height),
            rcSource = new RECT(origin.X, origin.Y, origin.X + width, origin.Y + height),
            opacity = Opacity.Wanted(standIn.Id),
            fVisible = true,
            fSourceClientAreaOnly = false,
        };
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

        Opacity.Unveil(window);
        if (standIn.Backdrop is uint backdrop)
            SetBackdrop(standIn.Window, backdrop);
        _ = PInvoke.DwmUnregisterThumbnail(standIn.Thumbnail);
        StandInHost.Close(standIn.Overlay);
    }

    private static void EndStandIns()
    {
        WindowId[] windows;
        lock (StandInGate)
            windows = [.. StandIns.Keys];

        foreach (var window in windows)
            EndStandIn(window, null);
    }

    private sealed class StandIn(WindowId id, HWND overlay, nint thumbnail, Rectangle goal, Point origin)
    {
        public WindowId Id { get; } = id;

        public HWND Window { get; } = new((nint)id.Value);

        public HWND Overlay { get; } = overlay;

        public nint Thumbnail { get; } = thumbnail;

        public Rectangle Goal { get; } = goal;

        // The top left of the visible frame, in the window's own coordinates.
        public Point Origin { get; } = origin;

        // The backdrop the window had, while the stand-in draws it in its place.
        public uint? Backdrop { get; init; }
    }
}
