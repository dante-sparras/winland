namespace Winland.Infrastructure;

// Placing a Client by its visible frame, and painting its decoration.
public sealed partial class WindowAdapter
{
    // DWMWA_BORDER_COLOR, DWMWA_WINDOW_CORNER_PREFERENCE.
    private const int BorderColorAttribute = 34;
    private const int CornerAttribute = 33;
    private const int TransitionsOffAttribute = 3;

    // The corners each painted window was last given, with its border taken off and DWM's transitions turned off.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<WindowId, uint> _dwmPainted = new();

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
    private void Place(WindowId window, Rectangle target)
    {
        var trails = TryOuter(window, out var before) && _quick.Trails(window, before);

        // A window already on its rectangle is left alone: moving it would still make it lay itself out.
        if (!trails && TryVisible(window, out var shown) && shown == target)
            return;

        // A frame sent without waiting may still be on its way, and the window would take it after this.
        if (PlaceDirectly(window, target) && trails && TryOuter(window, out var landed))
            SendQuick(window, landed, keepSize: false);
    }

    // Returns false when the window was only sent its frame, because its program has stopped answering.
    private bool PlaceDirectly(WindowId window, Rectangle target)
    {
        var dpi = _desktop.Dpi(window);
        var insets = InsetsFor(window, dpi);
        var requested = insets.OuterFor(target);
        if (!Move(window, requested))
            return false;

        // A window that moved onto a Monitor with another scale resized itself for that scale as it arrived, and its
        // frame changed with it. It is measured again and placed once more, so its own resize is not taken for a
        // size it refuses.
        var arrived = _desktop.Dpi(window);
        if (arrived != dpi)
        {
            dpi = arrived;
            insets = InsetsFor(window, dpi);
            requested = insets.OuterFor(target);
            if (!Move(window, requested))
                return false;
        }

        if (!TryOuter(window, out var landedOuter) || !TryVisible(window, out var landedVisible))
        {
            Remember(window, dpi, insets);
            return true;
        }

        var correction = FrameInsets.Correct(target, requested, landedOuter, landedVisible);
        if (correction.Outer != landedOuter && !Move(window, correction.Outer))
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

        if (TryOuter(window, out var finalOuter) && TryVisible(window, out var finalVisible))
            Remember(window, dpi, FrameInsets.Between(finalOuter, finalVisible));
        else
            Remember(window, dpi, insets);

        return true;
    }

    private FrameInsets InsetsFor(WindowId window, int dpi)
    {
        lock (_frameGate)
        {
            if (_frames.TryGetValue(window, out var cached) && FrameInsets.AppliesAt(cached.Dpi, dpi))
                return cached.Insets;
        }

        var insets = TryMeasure(window, out var measured) ? measured : FrameInsets.None;
        Remember(window, dpi, insets);
        return insets;
    }

    private void Remember(WindowId window, int dpi, FrameInsets insets)
    {
        lock (_frameGate)
            _frames[window] = new CachedFrame(dpi, insets);
    }

    private void DropClosedFrames(IReadOnlyList<Client> clients)
    {
        lock (_frameGate)
        {
            if (_frames.Count == 0)
                return;

            var live = new HashSet<WindowId>(clients.Count);
            foreach (var client in clients)
                live.Add(client.Window);

            if (live.Count == 0)
            {
                _frames.Clear();
                return;
            }

            List<WindowId>? stale = null;
            foreach (var window in _frames.Keys)
            {
                if (!live.Contains(window))
                    (stale ??= []).Add(window);
            }

            if (stale is null)
                return;

            foreach (var window in stale)
                _frames.Remove(window);
        }
    }

    private bool TryMeasure(WindowId window, out FrameInsets insets)
    {
        insets = FrameInsets.None;
        if (!TryOuter(window, out var outer) || !TryVisible(window, out var visible))
            return false;

        insets = FrameInsets.Between(outer, visible);
        return true;
    }

    private bool TryOuter(WindowId window, out Rectangle outer) => _desktop.TryOuter(window, out outer);

    private bool TryVisible(WindowId window, out Rectangle visible) => _desktop.TryVisible(window, out visible);

    // A call that waits for the window waits as long as its program does not answer. The frame of a program that has
    // stopped answering is sent without waiting, so it lands when the program wakes. Returns whether the window took it.
    private bool Move(WindowId window, Rectangle bounds)
    {
        if (_desktop.Hung(window))
        {
            SendQuick(window, bounds, keepSize: false);
            return false;
        }

        _desktop.SetBounds(window, bounds, Delivery.Wait);
        _quick.Note(window, bounds, waiting: false);
        return true;
    }

    // A frame of a motion or a step of a drag. The window takes it when its thread gets to it, and a later
    // placement corrects whatever it does with it.
    internal void QuickPlace(WindowId window, Rectangle target, bool borderless)
    {
        if (!TryOuter(window, out var current))
            return;

        _ = _quick.Request(window, OuterOf(window, target, borderless), current, (bounds, keepSize) => SendQuick(window, bounds, keepSize));
    }

    // The Layout rectangle of a window whose program has stopped answering, sent without waiting. Unlike a frame of a
    // motion it is never skipped, because nothing after it puts the window there.
    internal void PlaceAsync(WindowId window, Rectangle target, bool borderless) =>
        SendQuick(window, OuterOf(window, target, borderless), keepSize: false);

    private Rectangle OuterOf(WindowId window, Rectangle target, bool borderless) =>
        borderless ? target : InsetsFor(window, _desktop.Dpi(window)).OuterFor(target);

    private void SendQuick(WindowId window, Rectangle bounds, bool keepSize)
    {
        _desktop.SetBounds(window, bounds, keepSize ? Delivery.PostKeepingSize : Delivery.Post);
        _quick.Note(window, bounds, waiting: true);
    }

    private readonly record struct CachedFrame(int Dpi, FrameInsets Insets);

    internal void ResetDecorations() => ClearDecorations();

    private void ClearDecorations()
    {
        GroupBarHost.Stop();
        TrayHost.Stop();
        BorderHost.Stop();
        EndStandIns();
        _desktop.StopStandIns();
        PutBackFrames();
    }

    // Every window whose frame, border, corners, backdrop, region or alpha this changed, painted or not.
    private void PutBackFrames()
    {
        WindowId[] painted;
        lock (_paintGate)
        {
            painted = [.. _painted];
            _painted.Clear();
        }

        foreach (var window in painted)
            ResetDecoration(window);

        _borderless.ReleaseAll();
        _effects.ResetAll();
        _opacity.ResetAll();
    }

    // DWMWA_BORDER_COLOR is a COLORREF, 0x00BBGGRR. The alpha byte is not used.
    // DWMWA_COLOR_DEFAULT and DWMWCP_DEFAULT put the frame back to the system choice.
    // A running motion owns the alpha and border it animates, and moves them toward this decoration.
    private void Paint(WindowId window, Decoration decoration)
    {
        lock (_motions.For(window))
        {
            var held = _motions.Paint(window, decoration.Alpha, decoration.Border);
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
            if (!_dwmPainted.TryGetValue(window, out var given) || given != corners)
            {
                // Winland draws the border itself, outside the window, so DWM draws none.
                _desktop.SetAttribute(window, BorderColorAttribute, NoneBorderColor);
                _desktop.SetAttribute(window, CornerAttribute, corners);
                // Winland animates a Client's showing and hiding itself. DWM's own fade would play on top, and a Client
                // hidden as its Workspace slides away would fade out on its tile, over the Workspace shown in its place.
                _desktop.SetAttribute(window, TransitionsOffAttribute, 1);
                _dwmPainted[window] = corners;
            }

            _effects.Apply(window, decoration);
            if (!held.Alpha)
                _opacity.Apply(window, decoration.Alpha);
        }
    }

    private static uint ColorRef(BorderColor color) => (uint)(color.R | (color.G << 8) | (color.B << 16));

    private void ForgetDwmPainted(IEnumerable<WindowId> clients)
    {
        if (_dwmPainted.IsEmpty)
            return;

        var live = new HashSet<WindowId>(clients);
        foreach (var window in _dwmPainted.Keys)
        {
            if (!live.Contains(window))
                _dwmPainted.TryRemove(window, out _);
        }
    }

    private void ResetDecoration(WindowId window)
    {
        _dwmPainted.TryRemove(window, out _);
        _desktop.SetAttribute(window, BorderColorAttribute, DefaultBorderColor);
        _desktop.SetAttribute(window, CornerAttribute, DefaultCorners);
        _desktop.SetAttribute(window, TransitionsOffAttribute, 0);
        _effects.Reset(window);
        _opacity.Reset(window);
    }
}
