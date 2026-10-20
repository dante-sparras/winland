using System.Diagnostics;
using Windows.Win32.Foundation;

namespace Winland.Windows;

/// <summary>
/// Stands in for a window while its wobble and motion blur are drawn. It captures the window with Windows.Graphics.Capture and draws
/// each frame with Direct3D into a layered window of Winland's own, in the rectangle the motion shows.
/// </summary>
internal sealed unsafe class EffectStand : IDisposable
{
    private static readonly TimeSpan FirstPicture = TimeSpan.FromMilliseconds(250);

    private readonly Lock _gate = new();
    private readonly GpuDevice _gpu;
    private readonly WindowCapture _capture;
    private readonly EffectRenderer _renderer;
    private readonly EffectLayer _layer;
    private readonly EffectTrack _track;
    private EffectBox _rest;
    private StandInMapping _mapping;
    private byte _alpha = 255;
    private bool _disposed;

    private EffectStand(GpuDevice gpu, WindowCapture capture, EffectRenderer renderer, EffectLayer layer, EffectTrack track)
    {
        _gpu = gpu;
        _capture = capture;
        _renderer = renderer;
        _layer = layer;
        _track = track;
    }

    /// <summary>The window that shows the frames.</summary>
    internal HWND Layer => _layer.Window;

    /// <summary>The size of the window's picture as it was last captured.</summary>
    internal Size Painted
    {
        get
        {
            lock (_gate)
                return _capture.Size;
        }
    }

    /// <summary>Whether the mesh is still moving after the last frame.</summary>
    internal bool Wobbling
    {
        get
        {
            lock (_gate)
                return _track.Wobbling;
        }
    }

    /// <summary>
    /// Starts capturing <paramref name="window"/> and waits for its first picture. Returns null when the machine cannot draw the
    /// effects, Windows will not capture the window, or no picture comes.
    /// </summary>
    internal static EffectStand? TryStart(nint window, MotionEffects effects)
    {
        var gpu = Gpu.Acquire();
        if (gpu is null)
            return null;

        WindowCapture? capture = null;
        EffectRenderer? renderer = null;
        EffectLayer? layer = null;
        try
        {
            lock (gpu.Gate)
                capture = WindowCapture.TryStart(gpu, window);

            if (capture is null)
                return null;

            var deadline = Stopwatch.GetTimestamp() + (long)(FirstPicture.TotalSeconds * Stopwatch.Frequency);
            var ready = false;
            while (!ready && Stopwatch.GetTimestamp() < deadline)
            {
                lock (gpu.Gate)
                    ready = capture.Refresh() && capture.Picture is not null;

                if (!ready)
                    Thread.Sleep(5);
            }

            if (!ready)
                return null;

            layer = EffectLayer.Create();
            if (layer is null)
                return null;

            renderer = new EffectRenderer(gpu);
            var stand = new EffectStand(gpu, capture, renderer, layer, new EffectTrack(effects, Now()));
            capture = null;
            renderer = null;
            layer = null;
            return stand;
        }
        finally
        {
            capture?.Dispose();
            renderer?.Dispose();
            layer?.Dispose();
        }
    }

    /// <summary>The effects the window has now, which a reload may have changed.</summary>
    internal void Use(MotionEffects effects)
    {
        lock (_gate)
            _track.Use(effects);
    }

    /// <summary>
    /// Draws the frame of a motion that shows the window's picture in <paramref name="bounds"/>, laid out by <paramref name="mapping"/>.
    /// <paramref name="grab"/> is where a pointer holds the window, from 0 to 1 on each axis, during a drag. Returns false when the frame
    /// could not be drawn, and the caller shows the window another way.
    /// </summary>
    internal bool Show(Rectangle bounds, StandInMapping mapping, byte alpha, HWND above, (double X, double Y)? grab = null)
    {
        lock (_gate)
        {
            if (_disposed)
                return false;

            var destination = mapping.Destination;
            _rest = new EffectBox(bounds.X + destination.X, bounds.Y + destination.Y, destination.Width, destination.Height);
            _mapping = mapping;
            _alpha = alpha;
            var now = Now();
            _track.Step(_rest, now, grab);
            _ = _track.Tick(now);
            return Draw(now, above);
        }
    }

    /// <summary>
    /// The motion ended with the window at <paramref name="bounds"/>, laid out by <paramref name="mapping"/>. The mesh goes on
    /// springing from there.
    /// </summary>
    internal bool Settle(Rectangle bounds, StandInMapping mapping, HWND above)
    {
        lock (_gate)
        {
            if (_disposed)
                return false;

            var destination = mapping.Destination;
            _rest = new EffectBox(bounds.X + destination.X, bounds.Y + destination.Y, destination.Width, destination.Height);
            _mapping = mapping;
            var now = Now();
            _track.Step(_rest, now);
            _track.EndBlur();
            _ = _track.Tick(now);
            return Draw(now, above);
        }
    }

    /// <summary>
    /// Lets the mesh spring on and draws it, with the window at rest on <paramref name="bounds"/>. Returns false when it has come to
    /// rest or could not be drawn.
    /// </summary>
    internal bool Spring(Rectangle bounds, StandInMapping mapping, HWND above)
    {
        lock (_gate)
        {
            if (_disposed)
                return false;

            var destination = mapping.Destination;
            _rest = new EffectBox(bounds.X + destination.X, bounds.Y + destination.Y, destination.Width, destination.Height);
            _mapping = mapping;
            var now = Now();
            var moving = _track.Tick(now);
            return Draw(now, above) && moving;
        }
    }

    /// <summary>Changes how opaque the frame shown is.</summary>
    internal void Fade(byte alpha)
    {
        lock (_gate)
        {
            _alpha = alpha;
            if (!_disposed)
                _layer.Fade(alpha);
        }
    }

    internal void Hide()
    {
        lock (_gate)
        {
            if (!_disposed)
                _layer.Hide();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            lock (_gpu.Gate)
            {
                _renderer.Dispose();
                _capture.Dispose();
            }

            _layer.Dispose();
        }
    }

    private static double Now() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    // The caller holds the stand's gate.
    private bool Draw(double now, HWND above)
    {
        lock (_gpu.Gate)
        {
            if (_gpu.Lost() || !_capture.Refresh() || _capture.Picture is not { } picture)
                return false;

            return _renderer.Render(
                picture,
                _mapping.Source,
                _track,
                _rest,
                now,
                (rows, pitch, canvas) => _layer.Present(rows, pitch, canvas.Width, canvas.Height, new System.Drawing.Point(canvas.X, canvas.Y), _alpha, above));
        }
    }
}
