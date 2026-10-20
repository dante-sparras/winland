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
//
// A window with wobble or motion blur is stood in for by every motion that moves it, not only the ones that
// resize it, because a thumbnail can only be scaled and faded. Its stand-in is an EffectStand: the window is captured
// and drawn with Direct3D, deformed and blurred, in a layered window of Winland's own. The wobble goes on after the
// motion ends until the mesh is at rest. When the effects cannot be drawn, a thumbnail stands in without them.
public sealed partial class WindowAdapter
{
    // DWM_TNP_RECTDESTINATION, DWM_TNP_RECTSOURCE, DWM_TNP_OPACITY, DWM_TNP_VISIBLE, DWM_TNP_SOURCECLIENTAREAONLY.
    private const uint ThumbnailParts = 0x1 | 0x2 | 0x4 | 0x8 | 0x10;
    private const uint ThumbnailOpacity = 0x4;

    // DWMWA_USE_IMMERSIVE_DARK_MODE, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_NONE.
    private const DWMWINDOWATTRIBUTE DarkAttribute = (DWMWINDOWATTRIBUTE)20;
    private const DWMWINDOWATTRIBUTE BackdropAttribute = (DWMWINDOWATTRIBUTE)38;
    private const uint NoBackdrop = 1;

    // DWMWA_CLOAKED.
    private const DWMWINDOWATTRIBUTE CloakedAttribute = (DWMWINDOWATTRIBUTE)14;

    private static readonly Lock StandInGate = new();
    private static readonly Dictionary<WindowId, StandIn> StandIns = [];
    private static readonly MotionEffects NoEffects = new(null, 0);

    private static bool Resizes(Rectangle bounds, Rectangle content) => content.Width != bounds.Width || content.Height != bounds.Height;

    // A frame of a motion whose window rests on content. Returns false when the caller moves the window itself.
    private static bool StandFor(WindowId window, Rectangle bounds, Rectangle content, (double X, double Y)? grab = null)
    {
        var effects = MotionFx.Of(window);
        StandIn? standIn;
        lock (StandInGate)
            StandIns.TryGetValue(window, out standIn);

        // A motion that replaced the one the stand-in was made for carries on from this frame. A stand-in with effects keeps
        // its mesh moving and goes to the new rectangle the window rests on.
        if (standIn is not null && standIn.Content != content)
        {
            if (standIn.Effect is not null && effects is not null)
            {
                Retarget(standIn, content);
            }
            else
            {
                EndStandIn(window, bounds);
                standIn = null;
            }
        }

        if (standIn is null)
        {
            var resizes = Resizes(bounds, content);
            if (!resizes && effects is null)
                return false;

            standIn = BeginStandIn(window, content, effects, resizes);
            if (standIn is null)
                return false;
        }
        else
        {
            Resume(standIn);
            standIn.Effect?.Use(effects ?? NoEffects);
        }

        ShowStandIn(standIn, bounds, grab);
        return true;
    }

    private static StandIn? BeginStandIn(WindowId window, Rectangle content, MotionEffects? effects, bool resizes)
    {
        var hwnd = new HWND((nint)window.Value);
        if (!PInvoke.IsWindowVisible(hwnd) || !TryVisible(hwnd, out var visible))
            return null;

        var insets = InsetsFor(window, hwnd, (int)PInvoke.GetDpiForWindow(hwnd));
        // A window that cannot be veiled cannot be stood in for, and capturing it again on every frame would only slow it.
        var standIn = effects is null || Opacity.Refuses(window) ? null : BeginEffectStandIn(window, hwnd, content, effects, insets, visible);
        if (standIn is null)
        {
            if (!resizes)
                return null;

            standIn = BeginThumbnailStandIn(window, hwnd, content, insets, visible);
            if (standIn is null)
                return null;
        }

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

    // Null when the effects cannot be drawn: the machine has no adapter for them, Windows will not capture the window, or the
    // first frame could not be drawn.
    private static StandIn? BeginEffectStandIn(WindowId window, HWND hwnd, Rectangle content, MotionEffects effects, FrameInsets insets, Rectangle visible)
    {
        // A window that keeps itself out of captures would show black, and a cloaked one gives no frames.
        if (!PInvoke.GetWindowDisplayAffinity(hwnd, out var affinity) || affinity != 0 || (TryAttribute(hwnd, CloakedAttribute, out var cloaked) && cloaked != 0))
            return null;

        var effect = EffectStand.TryStart((nint)hwnd, effects);
        if (effect is null)
            return null;

        var standIn = new StandIn(window, effect.Layer, 0, content, insets)
        {
            Effect = effect,
            SourceInsets = SourceInsetsFor(hwnd, effect.Painted, visible, insets),
        };
        lock (StandInGate)
            StandIns[window] = standIn;

        if (ShowEffect(standIn, visible))
            return standIn;

        lock (StandInGate)
            StandIns.Remove(window);

        effect.Dispose();
        StandFrames.Remove(window);
        StandFrames.Follow(window);
        return null;
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

    // Windows may capture a window with or without the invisible border it is sized with, so the picture says which.
    private static FrameInsets SourceInsetsFor(HWND hwnd, Size picture, Rectangle visible, FrameInsets insets)
    {
        if (!TryOuter(hwnd, out var outer))
            return insets;

        var visibleOnly = Math.Abs(picture.Width - visible.Width) <= 1 && Math.Abs(picture.Height - visible.Height) <= 1;
        var whole = Math.Abs(picture.Width - outer.Width) <= 1 && Math.Abs(picture.Height - outer.Height) <= 1;
        return visibleOnly && !whole ? FrameInsets.None : insets;
    }

    // The window is given the rectangle it rests on now, and its stand-in goes on from where it is.
    private static void Retarget(StandIn standIn, Rectangle content)
    {
        standIn.Content = content;
        SendQuick(standIn.Window, standIn.Insets.OuterFor(content), keepSize: false);
    }

    // A step of a drag that holds a stand-in with effects takes it out of the table while the player settles the window, because
    // settling a window with no motion ends its stand-in. The step puts it back.
    private static StandIn? Park(WindowId window)
    {
        lock (StandInGate)
            return StandIns.Remove(window, out var standIn) ? standIn : null;
    }

    private static void Unpark(WindowId window, StandIn? standIn)
    {
        if (standIn is null)
            return;

        lock (StandInGate)
            StandIns[window] = standIn;
    }

    // Where a pointer holds a window, from 0 to 1 on each axis.
    private static (double X, double Y)? GripOn(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || !PInvoke.GetCursorPos(out var cursor))
            return null;

        return (Math.Clamp((double)(cursor.X - bounds.X) / bounds.Width, 0, 1), Math.Clamp((double)(cursor.Y - bounds.Y) / bounds.Height, 0, 1));
    }

    // A stand-in that lingered for its wobble has a motion again.
    private static void Resume(StandIn standIn)
    {
        if (!standIn.Lingering)
            return;

        standIn.Lingering = false;
        EffectPump.Remove(standIn);
    }

    private static void ShowStandIn(StandIn standIn, Rectangle bounds, (double X, double Y)? grab = null)
    {
        if (standIn.Effect is not null)
        {
            if (ShowEffect(standIn, bounds, grab))
                return;

            // The effects cannot be drawn any more. The window stays veiled, so a thumbnail takes over.
            if (!ToThumbnail(standIn))
            {
                EndStandIn(standIn.Id, bounds);
                return;
            }
        }

        // Just above the window, so whatever covers the window covers its stand-in too. A null handle is the top.
        // The border and glow stack on the stand-in, so a stand-in that is already next to the window stays put.
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

        // The decorations follow what the stand-in shows.
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

    // One frame of a stand-in that draws the window's effects. Returns false when it could not be drawn.
    private static bool ShowEffect(StandIn standIn, Rectangle bounds, (double X, double Y)? grab = null)
    {
        var effect = standIn.Effect!;
        var painted = effect.Painted;
        var map = StandInMap.For(bounds, new Size(standIn.Content.Width, standIn.Content.Height), standIn.SourceInsets, painted.Width > 0 ? painted : null);
        var above = PInvoke.GetWindow(standIn.Window, GET_WINDOW_CMD.GW_HWNDPREV);
        Decorate(effect, standIn.Id);
        if (!effect.Show(bounds, map, Opacity.Wanted(standIn.Id), above, grab))
            return false;

        standIn.Bounds = bounds;
        FrameDecorations(standIn, effect, bounds);
        return true;
    }

    private static void Decorate(EffectStand effect, WindowId window)
    {
        var (ring, shadow, glow) = EffectLayers.Of(window);
        effect.Decorate(ring, shadow, glow);
    }

    // The frames draw the window's border, shadow and glow, bent and smeared with it, so their own windows hide. When they
    // could not, those follow the rectangle of the frame instead.
    private static void FrameDecorations(StandIn standIn, EffectStand effect, Rectangle bounds)
    {
        StandFrames.Set(standIn.Id, effect.Decorated ? default : bounds, standIn.Overlay);
        StandFrames.Follow(standIn.Id);
    }

    // Swaps the effects for a thumbnail of the window, on a window of Winland's own.
    private static bool ToThumbnail(StandIn standIn)
    {
        var overlay = StandInHost.Create();
        if (overlay.IsNull)
            return false;

        if (PInvoke.DwmRegisterThumbnail(overlay, standIn.Window, out var thumbnail).Failed)
        {
            StandInHost.Close(overlay);
            return false;
        }

        StopEffect(standIn);
        standIn.Overlay = overlay;
        standIn.Thumbnail = thumbnail;
        standIn.SourceInsets = standIn.Insets;
        return true;
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

        if (standIn.Effect is { } effect)
        {
            effect.Fade(alpha);
            return;
        }

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
        StopEffect(standIn);
        if (standIn.Thumbnail != 0)
        {
            _ = PInvoke.DwmUnregisterThumbnail(standIn.Thumbnail);
            StandInHost.Close(standIn.Overlay);
        }
    }

    // Ends the stand-in's effects and the layer they were drawn on.
    private static void StopEffect(StandIn standIn)
    {
        standIn.Lingering = false;
        EffectPump.Remove(standIn);
        var effect = standIn.Effect;
        standIn.Effect = null;
        effect?.Dispose();
    }

    /// <summary>
    /// The motion landed with the window on <paramref name="bounds"/>. A stand-in whose wobble is still going keeps the window
    /// veiled and springs on until the mesh is at rest. Any other is taken away.
    /// </summary>
    private static void SettleStandIn(WindowId window, Rectangle bounds)
    {
        StandIn? standIn;
        lock (StandInGate)
            StandIns.TryGetValue(window, out standIn);

        if (standIn?.Effect is not { } effect)
        {
            EndStandIn(window, null);
            return;
        }

        var map = StandInMap.For(bounds, new Size(bounds.Width, bounds.Height), standIn.SourceInsets, effect.Painted.Width > 0 ? effect.Painted : null);
        var above = PInvoke.GetWindow(standIn.Window, GET_WINDOW_CMD.GW_HWNDPREV);
        Decorate(effect, window);
        if (!effect.Settle(bounds, map, above) || !effect.Wobbling)
        {
            EndStandIn(window, null);
            return;
        }

        standIn.Bounds = bounds;
        standIn.Content = bounds;
        standIn.Lingering = true;
        FrameDecorations(standIn, effect, bounds);
        EffectPump.Add(standIn, () => Linger(standIn));
    }

    // One frame of a wobble that goes on after its motion. Returns whether it is to be called again.
    private static bool Linger(StandIn standIn)
    {
        lock (Motions.For(standIn.Id))
        {
            lock (StandInGate)
            {
                if (!StandIns.TryGetValue(standIn.Id, out var current) || current != standIn || !standIn.Lingering)
                    return false;
            }

            if (standIn.Effect is { } effect)
            {
                var bounds = standIn.Bounds;
                var map = StandInMap.For(bounds, new Size(bounds.Width, bounds.Height), standIn.SourceInsets, effect.Painted.Width > 0 ? effect.Painted : null);
                var above = PInvoke.GetWindow(standIn.Window, GET_WINDOW_CMD.GW_HWNDPREV);
                Decorate(effect, standIn.Id);
                if (effect.Spring(bounds, map, above))
                    return true;
            }

            // The mesh is at rest, or it could not be drawn: the window is shown again where it rests.
            EndStandIn(standIn.Id, null);
            return false;
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
        }
        else
        {
            Resume(standIn);
            if (standIn.Effect is { } effect)
                effect.Hide();
            else
                _ = PInvoke.ShowWindow(standIn.Overlay, SHOW_WINDOW_CMD.SW_HIDE);

            // The window goes on closing, so its decorations hide with the stand-in.
            StandFrames.Set(window, default, standIn.Overlay);
            StandFrames.Follow(window);
        }
    }

    private static void EndStandIns()
    {
        WindowId[] windows;
        lock (StandInGate)
            windows = [.. StandIns.Keys];

        foreach (var window in windows)
            EndStandIn(window, null);

        EffectPump.Stop();
    }

    private sealed class StandIn(WindowId id, HWND overlay, nint thumbnail, Rectangle content, FrameInsets insets)
    {
        public WindowId Id { get; } = id;

        public HWND Window { get; } = new((nint)id.Value);

        // The window that shows the stand-in: one with a thumbnail on it, or the layer an effect is drawn on.
        public HWND Overlay { get; set; } = overlay;

        // The DWM thumbnail of the window, or 0 for a stand-in that draws effects.
        public nint Thumbnail { get; set; } = thumbnail;

        // What draws the window with its wobble and motion blur, or null for a thumbnail.
        public EffectStand? Effect { get; set; }

        // Whether the motion is over and the wobble goes on by itself.
        public bool Lingering { get; set; }

        // The rectangle the stand-in showed last.
        public Rectangle Bounds { get; set; }

        // The rectangle the window rests on, which it was given at the start or by the motion that replaced the first.
        public Rectangle Content { get; set; } = content;

        // How far the window's outer rectangle extends past its visible frame.
        public FrameInsets Insets { get; } = insets;

        // How far the window's picture extends past its visible frame. It is the same, or none when only the visible frame is captured.
        public FrameInsets SourceInsets { get; set; } = insets;

        // The backdrop the window had, while the stand-in draws it in its place.
        public uint? Backdrop { get; init; }

        // Whether the overlay shows that backdrop now.
        public bool BackdropShown { get; set; } = true;

        // The border color the window had, while the stand-in hides the window's border.
        public uint? Border { get; set; }
    }
}
