using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

// Placing a Client by its visible frame, and painting its decoration.
public sealed partial class WindowAdapter
{
    // DWMWA_BORDER_COLOR, DWMWA_WINDOW_CORNER_PREFERENCE.
    private const DWMWINDOWATTRIBUTE BorderColorAttribute = (DWMWINDOWATTRIBUTE)34;
    private const DWMWINDOWATTRIBUTE CornerAttribute = (DWMWINDOWATTRIBUTE)33;

    // DWMWA_COLOR_DEFAULT, DWMWA_COLOR_NONE, DWMWCP_DEFAULT, DWMWCP_DONOTROUND, DWMWCP_ROUND, DWMWCP_ROUNDSMALL.
    private const uint DefaultBorderColor = 0xFFFFFFFFu;
    private const uint NoneBorderColor = 0xFFFFFFFEu;
    private const uint DefaultCorners = 0;
    private const uint SquareCorners = 1;
    private const uint RoundCorners = 2;
    private const uint SmallCorners = 3;

    // The Layout rectangle is the visible frame. The outer rectangle SetWindowPos moves
    // includes DWM's invisible border, which differs per window and per DPI. A window that
    // refuses the size is shifted once and then left at the size it kept.
    private static void Place(WindowId window, Rectangle target)
    {
        var hwnd = new HWND((nint)window.Value);
        var trails = TryOuter(hwnd, out var before) && Quick.Trails(window, before);

        // A window already on its rectangle is left alone: moving it would still make it lay itself out.
        if (!trails && TryVisible(hwnd, out var shown) && shown == target)
            return;

        PlaceDirectly(window, hwnd, target);

        // A frame sent without waiting may still be on its way, and the window would take it after this.
        if (trails && TryOuter(hwnd, out var landed))
            SendQuick(hwnd, landed, keepSize: false);
    }

    private static void PlaceDirectly(WindowId window, HWND hwnd, Rectangle target)
    {
        var dpi = (int)PInvoke.GetDpiForWindow(hwnd);
        var insets = InsetsFor(window, hwnd, dpi);
        var requested = insets.OuterFor(target);
        Move(hwnd, requested);
        if (!TryOuter(hwnd, out var landedOuter) || !TryVisible(hwnd, out var landedVisible))
        {
            Remember(window, dpi, insets);
            return;
        }

        var correction = FrameInsets.Correct(target, requested, landedOuter, landedVisible);
        if (correction.Outer != landedOuter)
            Move(hwnd, correction.Outer);

        if (correction.Clamped)
        {
            Console.Error.WriteLine(
                "Window 0x" + window.Value.ToString("x", System.Globalization.CultureInfo.InvariantCulture)
                + " kept a clamped size "
                + landedVisible.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "x"
                + landedVisible.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ".");
        }

        if (TryOuter(hwnd, out var finalOuter) && TryVisible(hwnd, out var finalVisible))
            Remember(window, dpi, FrameInsets.Between(finalOuter, finalVisible));
        else
            Remember(window, dpi, insets);
    }

    private static FrameInsets InsetsFor(WindowId window, HWND hwnd, int dpi)
    {
        lock (FrameGate)
        {
            if (Frames.TryGetValue(window, out var cached) && cached.Insets.AppliesAt(cached.Dpi, dpi))
                return cached.Insets;
        }

        var insets = TryMeasure(hwnd, out var measured) ? measured : FrameInsets.None;
        Remember(window, dpi, insets);
        return insets;
    }

    private static void Remember(WindowId window, int dpi, FrameInsets insets)
    {
        lock (FrameGate)
            Frames[window] = new CachedFrame(dpi, insets);
    }

    private static void DropClosedFrames(IReadOnlyList<Client> clients)
    {
        lock (FrameGate)
        {
            if (Frames.Count == 0)
                return;

            var live = new HashSet<WindowId>(clients.Count);
            foreach (var client in clients)
                live.Add(client.Window);

            if (live.Count == 0)
            {
                Frames.Clear();
                return;
            }

            List<WindowId>? stale = null;
            foreach (var window in Frames.Keys)
            {
                if (!live.Contains(window))
                    (stale ??= []).Add(window);
            }

            if (stale is null)
                return;

            foreach (var window in stale)
                Frames.Remove(window);
        }
    }

    private static bool TryMeasure(HWND hwnd, out FrameInsets insets)
    {
        insets = FrameInsets.None;
        if (!TryOuter(hwnd, out var outer) || !TryVisible(hwnd, out var visible))
            return false;

        insets = FrameInsets.Between(outer, visible);
        return true;
    }

    private static bool TryOuter(HWND hwnd, out Rectangle outer)
    {
        outer = default;
        if (!PInvoke.GetWindowRect(hwnd, out var rect))
            return false;

        outer = From(rect);
        return true;
    }

    private static unsafe bool TryVisible(HWND hwnd, out Rectangle visible)
    {
        visible = default;
        Span<byte> value = stackalloc byte[sizeof(RECT)];
        var result = PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, value);
        if (result.Failed)
            return false;

        visible = From(System.Runtime.InteropServices.MemoryMarshal.Read<RECT>(value));
        return true;
    }

    private static void Move(HWND hwnd, Rectangle bounds)
    {
        _ = PInvoke.SetWindowPos(
            hwnd,
            HWND.Null,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        Quick.Note(new WindowId(((IntPtr)hwnd).ToInt64()), bounds, waiting: false);
    }

    // A frame of a motion or a step of a drag. The window takes it when its thread gets to it, and a later
    // placement corrects whatever it does with it.
    internal static void QuickPlace(WindowId window, Rectangle target, bool borderless)
    {
        var hwnd = new HWND((nint)window.Value);
        if (!TryOuter(hwnd, out var current))
            return;

        var outer = borderless ? target : InsetsFor(window, hwnd, (int)PInvoke.GetDpiForWindow(hwnd)).OuterFor(target);
        _ = Quick.Request(window, outer, current, (bounds, keepSize) => SendQuick(hwnd, bounds, keepSize));
    }

    private static void SendQuick(HWND hwnd, Rectangle bounds, bool keepSize)
    {
        var flags = SET_WINDOW_POS_FLAGS.SWP_NOZORDER
            | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER
            | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
            | SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS;
        if (keepSize)
            flags |= SET_WINDOW_POS_FLAGS.SWP_NOSIZE;

        _ = PInvoke.SetWindowPos(hwnd, HWND.Null, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags);
        Quick.Note(new WindowId(((IntPtr)hwnd).ToInt64()), bounds, waiting: true);
    }

    private readonly record struct CachedFrame(int Dpi, FrameInsets Insets);

    internal void ResetDecorations() => ClearDecorations();

    private static void ClearDecorations()
    {
        GroupBarHost.Stop();
        ErrorBarHost.Stop();
        EndStandIns();
        StandInHost.Stop();
        PutBackFrames();
    }

    // Every window whose frame, border, corners, backdrop, shadow, region or alpha this changed, painted or not.
    private static void PutBackFrames()
    {
        WindowId[] painted;
        lock (PaintGate)
        {
            painted = [.. Painted];
            Painted.Clear();
        }

        foreach (var window in painted)
            ResetDecoration(window);

        Borderless.ReleaseAll();
        Effects.ResetAll();
        Opacity.ResetAll();
    }

    // DWMWA_BORDER_COLOR is a COLORREF, 0x00BBGGRR. The alpha byte is not used.
    // DWMWA_COLOR_DEFAULT and DWMWCP_DEFAULT put the frame back to the system choice.
    // A running motion owns the alpha and border it animates, and moves them toward this decoration.
    private static void Paint(WindowId window, Decoration decoration)
    {
        var hwnd = new HWND((nint)window.Value);
        lock (Motions.For(window))
        {
            var held = Motions.Paint(window, decoration.Alpha, decoration.Border);
            if (decoration.Borderless || !held.Border)
                SetAttribute(hwnd, BorderColorAttribute, decoration.Borderless ? NoneBorderColor : ColorRef(decoration.Border));
            var corners = decoration.Corners switch
            {
                CornerPreference.Round => RoundCorners,
                CornerPreference.RoundSmall => SmallCorners,
                _ => SquareCorners,
            };
            SetAttribute(hwnd, CornerAttribute, corners);
            Effects.Apply(window, decoration);
            if (!held.Alpha)
                Opacity.Apply(window, decoration.Alpha);
        }
    }

    private static uint ColorRef(BorderColor color) => (uint)(color.R | (color.G << 8) | (color.B << 16));

    private static void ResetDecoration(WindowId window)
    {
        var hwnd = new HWND((nint)window.Value);
        SetAttribute(hwnd, BorderColorAttribute, DefaultBorderColor);
        SetAttribute(hwnd, CornerAttribute, DefaultCorners);
        Effects.Reset(window);
        Opacity.Reset(window);
    }

    private static unsafe void SetAttribute(HWND hwnd, DWMWINDOWATTRIBUTE attribute, uint value) =>
        _ = PInvoke.DwmSetWindowAttribute(hwnd, attribute, &value, sizeof(uint));
}
