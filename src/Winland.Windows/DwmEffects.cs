using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;

namespace Winland.Windows;

internal interface IDwmSurface
{
    bool TryReadBackdrop(WindowId window, out uint backdrop);

    bool TrySetBackdrop(WindowId window, uint backdrop);

    bool HasRegion(WindowId window);

    /// <summary>The window rectangle and the visible frame DWM draws inside it, in screen pixels. False while the window is minimized.</summary>
    bool TryFrame(WindowId window, out Rectangle outer, out Rectangle visible);

    /// <summary>Sets the window region to this outline, in window coordinates. An empty outline removes the region.</summary>
    bool TrySetRegion(WindowId window, IReadOnlyList<Point> outline);
}

/// <summary>
/// The backdrop and corner region of each Client. Each attribute is changed only when the
/// decoration asks for something other than what the window has, and is put back on reset.
/// A window that already has a region of its own keeps it, and its corners are not cut.
/// DWM's own shadow is left as Windows draws it.
/// </summary>
internal sealed class DwmEffects
{
    // DWMSBT_AUTO, DWMSBT_NONE, DWMSBT_TRANSIENTWINDOW (acrylic).
    internal const uint AutoBackdrop = 0;
    internal const uint NoBackdrop = 1;
    internal const uint BlurBackdrop = 3;

    private readonly IDwmSurface _surface;
    private readonly Dictionary<WindowId, Backdrop> _backdrops = [];
    private readonly Dictionary<WindowId, Cut> _regions = [];
    private readonly HashSet<WindowId> _foreignRegions = [];
    private readonly Lock _gate = new();
    private bool _closed;

    internal DwmEffects(IDwmSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _surface = surface;
    }

    internal bool CutsCorners(WindowId window)
    {
        lock (_gate)
            return _regions.ContainsKey(window);
    }

    internal void Apply(WindowId window, Decoration decoration)
    {
        lock (_gate)
        {
            if (_closed)
                return;

            ApplyBackdrop(window, decoration.Blur ? BlurBackdrop : NoBackdrop);
            if (decoration.Region is CornerRegion region && !decoration.Borderless)
                ApplyRegion(window, region);
            else
                ClearRegion(window);
        }
    }

    /// <summary>Cuts the corners again after the window changed size by itself.</summary>
    internal void Refit(WindowId window)
    {
        lock (_gate)
        {
            if (_regions.TryGetValue(window, out var cut))
                Fit(window, cut.Region, cut);
        }
    }

    internal void Reset(WindowId window)
    {
        lock (_gate)
        {
            if (_backdrops.Remove(window, out var backdrop))
                _surface.TrySetBackdrop(window, backdrop.Original);
            ClearRegion(window);
            _foreignRegions.Remove(window);
        }
    }

    /// <summary>
    /// Puts back the backdrop and region of every window this changed, for the exit cleanup. Nothing
    /// is changed after that until <see cref="Reopen"/>: an outcome that arrives late would stay for good.
    /// </summary>
    internal void ResetAll()
    {
        HashSet<WindowId> windows;
        lock (_gate)
        {
            _closed = true;
            windows = [.. _backdrops.Keys];
            windows.UnionWith(_regions.Keys);
            windows.UnionWith(_foreignRegions);
        }

        foreach (var window in windows)
            Reset(window);
    }

    /// <summary>Applies effects again, for a later run in the same process.</summary>
    internal void Reopen()
    {
        lock (_gate)
            _closed = false;
    }

    /// <summary>Marks each Client whose region is the corner outline this cut, so a crash does not leave it for good.</summary>
    internal RecoverySnapshot Stamp(RecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            var clients = new RecoveredClient[snapshot.Clients.Count];
            var changed = false;
            for (var i = 0; i < snapshot.Clients.Count; i++)
            {
                var client = snapshot.Clients[i];
                var cut = _regions.ContainsKey(client.Window);
                clients[i] = client.Region == cut ? client : client with { Region = cut };
                changed |= client.Region != cut;
            }

            return changed ? snapshot with { Clients = clients } : snapshot;
        }
    }

    /// <summary>Removes the corner region a previous run cut and left behind, so it is not taken as the window's own.</summary>
    internal void DropLeftoverRegion(WindowId window)
    {
        lock (_gate)
        {
            _regions.Remove(window);
            _foreignRegions.Remove(window);
            _surface.TrySetRegion(window, []);
        }
    }

    private void ApplyBackdrop(WindowId window, uint wanted)
    {
        if (_backdrops.TryGetValue(window, out var backdrop))
        {
            if (backdrop.Current != wanted && _surface.TrySetBackdrop(window, wanted))
                _backdrops[window] = backdrop with { Current = wanted };
            return;
        }

        var original = _surface.TryReadBackdrop(window, out var read) ? read : AutoBackdrop;
        if (_surface.TrySetBackdrop(window, wanted))
            _backdrops[window] = new Backdrop(original, wanted);
    }

    private void ApplyRegion(WindowId window, CornerRegion region)
    {
        if (_foreignRegions.Contains(window))
            return;

        if (_regions.TryGetValue(window, out var cut))
        {
            Fit(window, region, cut);
            return;
        }

        if (_surface.HasRegion(window))
        {
            _foreignRegions.Add(window);
            return;
        }

        Fit(window, region, null);
    }

    private void Fit(WindowId window, CornerRegion region, Cut? previous)
    {
        if (!_surface.TryFrame(window, out var outer, out var visible) || visible.Width <= 0 || visible.Height <= 0)
            return;

        var cut = new Cut(region, outer.Width, outer.Height, visible.X - outer.X, visible.Y - outer.Y, visible.Width, visible.Height);
        if (cut == previous)
            return;

        var outline = CornerShape.Outline(visible.Width, visible.Height, region);
        var shifted = new Point[outline.Count];
        for (var i = 0; i < outline.Count; i++)
            shifted[i] = new Point(outline[i].X + cut.Left, outline[i].Y + cut.Top);

        if (_surface.TrySetRegion(window, shifted))
            _regions[window] = cut;
    }

    private void ClearRegion(WindowId window)
    {
        if (_regions.Remove(window))
            _surface.TrySetRegion(window, []);
    }

    private readonly record struct Backdrop(uint Original, uint Current);

    private readonly record struct Cut(CornerRegion Region, int OuterWidth, int OuterHeight, int Left, int Top, int Width, int Height);
}

// Stryker disable once all : each member reads or sets a real window's DWM attributes or region.
internal sealed class Win32DwmSurface : IDwmSurface
{
    // DWMWA_SYSTEMBACKDROP_TYPE.
    private const DWMWINDOWATTRIBUTE BackdropAttribute = (DWMWINDOWATTRIBUTE)38;

    public unsafe bool TryReadBackdrop(WindowId window, out uint backdrop)
    {
        backdrop = 0;
        if (!TryWindow(window, out var hwnd))
            return false;

        Span<byte> value = stackalloc byte[sizeof(uint)];
        if (PInvoke.DwmGetWindowAttribute(hwnd, BackdropAttribute, value).Failed)
            return false;

        backdrop = MemoryMarshal.Read<uint>(value);
        return true;
    }

    public bool TrySetBackdrop(WindowId window, uint backdrop) => TrySet(window, BackdropAttribute, backdrop);

    public bool HasRegion(WindowId window)
    {
        if (!TryWindow(window, out var hwnd))
            return false;

        var probe = PInvoke.CreateRectRgn(0, 0, 0, 0);
        if (probe.IsNull)
            return false;

        try
        {
            return PInvoke.GetWindowRgn(hwnd, probe) != GDI_REGION_TYPE.RGN_ERROR;
        }
        finally
        {
            PInvoke.DeleteObject(probe);
        }
    }

    public unsafe bool TryFrame(WindowId window, out Rectangle outer, out Rectangle visible)
    {
        outer = default;
        visible = default;
        if (!TryWindow(window, out var hwnd) || PInvoke.IsIconic(hwnd) || !PInvoke.GetWindowRect(hwnd, out var rect))
            return false;

        outer = From(rect);
        Span<byte> value = stackalloc byte[sizeof(RECT)];
        visible = PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, value).Succeeded
            ? From(MemoryMarshal.Read<RECT>(value))
            : outer;
        return true;
    }

    public bool TrySetRegion(WindowId window, IReadOnlyList<Point> outline)
    {
        if (!TryWindow(window, out var hwnd))
            return false;

        if (outline.Count == 0)
            return PInvoke.SetWindowRgn(hwnd, HRGN.Null, true) != 0;

        var points = new System.Drawing.Point[outline.Count];
        for (var i = 0; i < outline.Count; i++)
            points[i] = new System.Drawing.Point(outline[i].X, outline[i].Y);

        using var region = PInvoke.CreatePolygonRgn(points, CREATE_POLYGON_RGN_MODE.WINDING);
        if (region.IsInvalid || PInvoke.SetWindowRgn(hwnd, (HRGN)region.DangerousGetHandle(), true) == 0)
            return false;

        // The system owns the region once SetWindowRgn succeeds.
        region.SetHandleAsInvalid();
        return true;
    }

    private static unsafe bool TrySet(WindowId window, DWMWINDOWATTRIBUTE attribute, uint value) =>
        TryWindow(window, out var hwnd) && PInvoke.DwmSetWindowAttribute(hwnd, attribute, &value, sizeof(uint)).Succeeded;

    private static bool TryWindow(WindowId window, out HWND hwnd)
    {
        hwnd = new HWND((nint)window.Value);
        return !hwnd.IsNull && PInvoke.IsWindow(hwnd);
    }

    private static Rectangle From(RECT rect) =>
        new(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
}
