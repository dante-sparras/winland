using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

// Placing a Client by its visible frame, and painting its decoration.
public sealed partial class WindowAdapter
{
    // DWMWA_BORDER_COLOR, DWMWA_WINDOW_CORNER_PREFERENCE.
    private const DWMWINDOWATTRIBUTE BorderColorAttribute = (DWMWINDOWATTRIBUTE)34;
    private const DWMWINDOWATTRIBUTE CornerAttribute = (DWMWINDOWATTRIBUTE)33;
    private const DWMWINDOWATTRIBUTE TransitionsOffAttribute = (DWMWINDOWATTRIBUTE)3;

    // The corners each painted window was last given, with its border taken off and DWM's transitions turned off.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<WindowId, uint> DwmPainted = new();

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

        // A frame sent without waiting may still be on its way, and the window would take it after this.
        if (PlaceDirectly(window, hwnd, target) && trails && TryOuter(hwnd, out var landed))
            SendQuick(hwnd, landed, keepSize: false);
    }

    // Returns false when the window was only sent its frame, because its program has stopped answering.
    private static bool PlaceDirectly(WindowId window, HWND hwnd, Rectangle target)
    {
        var dpi = (int)PInvoke.GetDpiForWindow(hwnd);
        var insets = InsetsFor(window, hwnd, dpi);
        var requested = insets.OuterFor(target);
        if (!Move(hwnd, requested))
            return false;

        if (!TryOuter(hwnd, out var landedOuter) || !TryVisible(hwnd, out var landedVisible))
        {
            Remember(window, dpi, insets);
            return true;
        }

        var correction = FrameInsets.Correct(target, requested, landedOuter, landedVisible);
        if (correction.Outer != landedOuter && !Move(hwnd, correction.Outer))
            return false;

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

        return true;
    }

    private static FrameInsets InsetsFor(WindowId window, HWND hwnd, int dpi)
    {
        lock (FrameGate)
        {
            if (Frames.TryGetValue(window, out var cached) && FrameInsets.AppliesAt(cached.Dpi, dpi))
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

    // A call that waits for the window waits as long as its program does not answer. The frame of a program that has
    // stopped answering is sent without waiting, so it lands when the program wakes. Returns whether the window took it.
    private static bool Move(HWND hwnd, Rectangle bounds)
    {
        if (PInvoke.IsHungAppWindow(hwnd))
        {
            SendQuick(hwnd, bounds, keepSize: false);
            return false;
        }

        _ = PInvoke.SetWindowPos(
            hwnd,
            HWND.Null,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        Quick.Note(new WindowId(((IntPtr)hwnd).ToInt64()), bounds, waiting: false);
        return true;
    }

    // A frame of a motion or a step of a drag. The window takes it when its thread gets to it, and a later
    // placement corrects whatever it does with it.
    internal static void QuickPlace(WindowId window, Rectangle target, bool borderless)
    {
        var hwnd = new HWND((nint)window.Value);
        if (!TryOuter(hwnd, out var current))
            return;

        _ = Quick.Request(window, OuterOf(window, hwnd, target, borderless), current, (bounds, keepSize) => SendQuick(hwnd, bounds, keepSize));
    }

    // The Layout rectangle of a window whose program has stopped answering, sent without waiting. Unlike a frame of a
    // motion it is never skipped, because nothing after it puts the window there.
    internal static void PlaceAsync(WindowId window, Rectangle target, bool borderless)
    {
        var hwnd = new HWND((nint)window.Value);
        SendQuick(hwnd, OuterOf(window, hwnd, target, borderless), keepSize: false);
    }

    private static Rectangle OuterOf(WindowId window, HWND hwnd, Rectangle target, bool borderless) =>
        borderless ? target : InsetsFor(window, hwnd, (int)PInvoke.GetDpiForWindow(hwnd)).OuterFor(target);

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

    internal static void ResetDecorations() => ClearDecorations();

    private static void ClearDecorations()
    {
        GroupBarHost.Stop();
        TrayHost.Stop();
        BorderHost.Stop();
        EndStandIns();
        StandInHost.Stop();
        PutBackFrames();
    }

    // Every window whose frame, border, corners, backdrop, region or alpha this changed, painted or not.
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
            if (!held.Border)
                BorderHost.Tint(window, null);
            var corners = decoration.Corners switch
            {
                CornerPreference.Round => RoundCorners,
                CornerPreference.RoundSmall => SmallCorners,
                _ => SquareCorners,
            };

            // Every outcome paints every Client, many times a second as windows show and move. Each attribute is a
            // call into DWM, so a window that has them already is left alone.
            if (!DwmPainted.TryGetValue(window, out var given) || given != corners)
            {
                // Winland draws the border itself, outside the window, so DWM draws none.
                SetAttribute(hwnd, BorderColorAttribute, NoneBorderColor);
                SetAttribute(hwnd, CornerAttribute, corners);
                // Winland animates a Client's showing and hiding itself. DWM's own fade would play on top, and a Client
                // hidden as its Workspace slides away would fade out on its tile, over the Workspace shown in its place.
                SetAttribute(hwnd, TransitionsOffAttribute, 1);
                DwmPainted[window] = corners;
            }

            Effects.Apply(window, decoration);
            if (!held.Alpha)
                Opacity.Apply(window, decoration.Alpha);
        }
    }

    private static uint ColorRef(BorderColor color) => (uint)(color.R | (color.G << 8) | (color.B << 16));

    private static void ForgetDwmPainted(IEnumerable<WindowId> clients)
    {
        if (DwmPainted.IsEmpty)
            return;

        var live = new HashSet<WindowId>(clients);
        foreach (var window in DwmPainted.Keys)
        {
            if (!live.Contains(window))
                DwmPainted.TryRemove(window, out _);
        }
    }

    private static void ResetDecoration(WindowId window)
    {
        DwmPainted.TryRemove(window, out _);
        var hwnd = new HWND((nint)window.Value);
        SetAttribute(hwnd, BorderColorAttribute, DefaultBorderColor);
        SetAttribute(hwnd, CornerAttribute, DefaultCorners);
        SetAttribute(hwnd, TransitionsOffAttribute, 0);
        Effects.Reset(window);
        Opacity.Reset(window);
    }

    private static unsafe void SetAttribute(HWND hwnd, DWMWINDOWATTRIBUTE attribute, uint value) =>
        _ = PInvoke.DwmSetWindowAttribute(hwnd, attribute, &value, sizeof(uint));
}
