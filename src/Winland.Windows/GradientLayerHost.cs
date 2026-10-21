using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

/// <summary>Where a gradient layer goes for a window whose visible frame is the rectangle given, or null for nowhere.</summary>
internal delegate LayerLayout? LayerPlacer<in T>(Rectangle frame, T layer);

/// <summary>Paints one piece of a layer's box, as <see cref="ShadowPainter.Paint"/> does.</summary>
internal delegate void LayerPainter<in T>(Span<uint> pixels, T layer, int boxWidth, int boxHeight, Rectangle frame, Rectangle piece, GradientMotion motion);

/// <summary>
/// Draws one kind of gradient layer for each Client, such as its shadow, on its own thread. A layer is up to four
/// click-through layered windows kept next to the Client in the z-order, just below or just above it. They follow the
/// window wherever it moves, by the frame Windows reports, fade to new colors when the layer's colors change and turn
/// while its gradient turns. The layers go away with this process.
/// </summary>
// Stryker disable once all : draws on the desktop; the layers' places and pixels are made and tested in Winland.Core.
internal sealed class GradientLayerHost<T>(
    string className,
    string threadName,
    uint syncMessage,
    bool below,
    LayerPlacer<T> place,
    LayerPainter<T> paint)
    where T : class, IGradientLayer
{
    private const int ClassAlreadyExists = 1410;
    private const uint QuitMessage = 0x0012;
    private const uint MouseActivateMessage = 0x0021;
    private const uint TimerMessage = 0x0113;
    private const uint FrameMilliseconds = 16;
    private const int NoActivate = 3;
    private const int TopmostStyle = 0x00000008;
    private const int TransparentStyle = 0x00000020;

    private static readonly WNDPROC Proc = OnWindow;

    private readonly Lock _gate = new();
    private readonly Dictionary<WindowId, Entry> _entries = [];
    private readonly HashSet<WindowId> _moved = [];
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private Dictionary<WindowId, T>? _pending;
    private bool _posted;
    private uint _thread;
    private Thread? _worker;
    private nuint _timer;

    /// <summary>Replaces every layer with <paramref name="layers"/>. Starts the thread for the first one.</summary>
    internal void Update(IReadOnlyDictionary<WindowId, T> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        lock (_gate)
        {
            if (_worker is null && layers.Count == 0)
                return;

            _pending = new Dictionary<WindowId, T>(layers);
            Post();
        }
    }

    /// <summary>The window moved, resized, showed, hid or changed place in the z-order, so its layer follows.</summary>
    internal void Follow(WindowId window)
    {
        lock (_gate)
        {
            if (_worker is null || !_entries.ContainsKey(window) && _pending?.ContainsKey(window) != true)
                return;

            _moved.Add(window);
            Post();
        }
    }

    /// <summary>Removes every layer and ends the thread.</summary>
    internal void Stop()
    {
        Thread? worker;
        uint thread;
        lock (_gate)
        {
            worker = _worker;
            thread = _thread;
            _worker = null;
            _thread = 0;
            _pending = null;
            _posted = false;
            _moved.Clear();
        }

        if (worker is null)
            return;

        if (thread != 0)
            _ = PInvoke.PostThreadMessage(thread, QuitMessage, default, default);

        _ = worker.Join(TimeSpan.FromSeconds(2));
    }

    // Called with the gate held.
    private void Post()
    {
        if (_worker is null)
        {
            _worker = new Thread(Run) { IsBackground = true, Name = threadName };
            _worker.Start();
        }

        if (_posted || _thread == 0)
            return;

        _posted = true;
        _ = PInvoke.PostThreadMessage(_thread, syncMessage, default, default);
    }

    private void Run()
    {
        _ = PInvoke.PeekMessage(out _, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
        try
        {
            Register();
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return;
        }

        lock (_gate)
        {
            if (!ReferenceEquals(_worker, Thread.CurrentThread))
                return;

            _thread = PInvoke.GetCurrentThreadId();
            _posted = true;
            _ = PInvoke.PostThreadMessage(_thread, syncMessage, default, default);
        }

        while (PInvoke.GetMessage(out var message, default, 0, 0) > 0)
        {
            try
            {
                if (message.hwnd.IsNull && message.message == syncMessage)
                {
                    Sync();
                    continue;
                }

                if (message.hwnd.IsNull && message.message == TimerMessage)
                {
                    Animate();
                    continue;
                }

                PInvoke.TranslateMessage(in message);
                PInvoke.DispatchMessage(in message);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Console.Error.WriteLine(exception.Message);
            }
        }

        if (_timer != 0)
            _ = PInvoke.KillTimer(HWND.Null, _timer);

        _timer = 0;
        lock (_gate)
        {
            foreach (var entry in _entries.Values)
                entry.Dispose();

            _entries.Clear();
        }
    }

    // Each fading or turning layer is drawn again, and the timer stops when none moves.
    private void Animate()
    {
        Entry[] entries;
        lock (_gate)
            entries = [.. _entries.Values];

        var now = _clock.Elapsed.TotalMilliseconds;
        var moving = false;
        foreach (var entry in entries)
            moving |= entry.Animate(now);

        if (!moving && _timer != 0)
        {
            _ = PInvoke.KillTimer(HWND.Null, _timer);
            _timer = 0;
        }
    }

    private void Sync()
    {
        Dictionary<WindowId, T>? wanted;
        WindowId[] moved;
        lock (_gate)
        {
            wanted = _pending;
            _pending = null;
            moved = [.. _moved];
            _moved.Clear();
            _posted = false;
        }

        var now = _clock.Elapsed.TotalMilliseconds;
        var touched = new HashSet<WindowId>(moved);
        if (wanted is not null)
        {
            lock (_gate)
            {
                foreach (var window in _entries.Keys.ToArray())
                {
                    if (wanted.ContainsKey(window))
                        continue;

                    _entries[window].Dispose();
                    _entries.Remove(window);
                }

                foreach (var (window, layer) in wanted)
                {
                    if (!_entries.TryGetValue(window, out var entry))
                    {
                        entry = new Entry(this, window);
                        _entries[window] = entry;
                    }

                    entry.Take(layer, now);
                    touched.Add(window);
                }
            }
        }

        foreach (var window in touched)
        {
            Entry? entry;
            lock (_gate)
                _entries.TryGetValue(window, out entry);

            entry?.Place();
        }

        bool moving;
        lock (_gate)
            moving = _entries.Values.Any(entry => entry.Moving);

        if (moving && _timer == 0)
            _timer = PInvoke.SetTimer(HWND.Null, 0, FrameMilliseconds, null);
    }

    private static LRESULT OnWindow(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam) =>
        message == MouseActivateMessage ? new LRESULT(NoActivate) : PInvoke.DefWindowProc(hwnd, message, wParam, lParam);

    private unsafe void Register()
    {
        var module = PInvoke.GetModuleHandle((string?)null);
        var instance = (HINSTANCE)module.DangerousGetHandle();
        module.SetHandleAsInvalid();
        fixed (char* name = className)
        {
            var info = new WNDCLASSEXW
            {
                cbSize = 80,
                lpfnWndProc = Proc,
                hInstance = instance,
                lpszClassName = name,
            };
            if (PInvoke.RegisterClassEx(in info) != 0)
                return;

            if (Marshal.GetLastPInvokeError() != ClassAlreadyExists)
                throw new InvalidOperationException($"The {className} window class was not registered ({Marshal.GetLastPInvokeError()}).");
        }
    }

    private unsafe HWND Create() =>
        PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE
                | WINDOW_EX_STYLE.WS_EX_TRANSPARENT,
            className,
            "",
            WINDOW_STYLE.WS_POPUP,
            0,
            0,
            0,
            0,
            HWND.Null,
            null,
            null,
            null);

    private sealed class Entry(GradientLayerHost<T> host, WindowId window) : IDisposable
    {
        private readonly HWND[] _pieces = new HWND[4];
        private T? _layer;
        private T? _painted;
        private GradientMotion _paintedMotion = GradientMotion.Still;
        private byte _alpha = 255;
        private byte _paintedAlpha = 255;
        private Rectangle _box;
        private bool _shown;
        private double? _turnStart;
        private double _turn;
        private double? _fadeStart;
        private BorderGradient? _fadeFrom;
        private double _fade = 1;

        public bool Moving => _turnStart is not null || _fadeStart is not null;

        private GradientMotion Motion => new(_turn, _fadeStart is null ? null : _fadeFrom, _fadeStart is null ? 1 : _fade);

        // A new look for the layer. Its colors fade when they changed and a fade is set, and a turn starts when the
        // window opens and each time it is focused, unless one is under way.
        public void Take(T layer, double now)
        {
            var old = _layer;
            _layer = layer;
            if (old is not null && !GradientMotion.Same(old.Gradient, layer.Gradient) && layer.Fade is { Milliseconds: > 0 })
            {
                _fadeFrom = old.Gradient;
                _fadeStart = now;
                _fade = 0;
            }

            var spins = layer.Spin is { Milliseconds: > 0 } && layer.Gradient.Colors.Count > 1;
            if (!spins)
            {
                _turnStart = null;
                _turn = 0;
            }
            else if ((old is null || layer.Focused && !old.Focused) && _turnStart is null)
            {
                _turnStart = now;
            }
        }

        // Moves a fading or turning layer on to now. False once it has stopped.
        public bool Animate(double now)
        {
            if (_layer is not T layer)
                return false;

            if (_fadeStart is double fadeStart)
            {
                if (layer.Fade is Timing fade && now - fadeStart < fade.Milliseconds)
                {
                    _fade = GradientMotion.Eased(fade, now - fadeStart);
                }
                else
                {
                    _fadeStart = null;
                    _fadeFrom = null;
                    _fade = 1;
                }
            }

            if (_turnStart is double turnStart)
            {
                if (layer.Spin is not Timing spin || spin.Milliseconds <= 0)
                {
                    _turnStart = null;
                    _turn = 0;
                }
                else
                {
                    var elapsed = now - turnStart;
                    _turn = GradientMotion.Eased(spin, elapsed) * 360;
                    if (elapsed >= spin.Milliseconds)
                    {
                        _turn = 0;
                        _turnStart = layer.SpinLoops ? now : null;
                    }
                }
            }

            Place();
            return Moving;
        }

        public void Place()
        {
            var target = new HWND((nint)window.Value);
            var frame = default(Rectangle);

            // A window with a stand-in is drawn in the stand-in's rectangle. A layer above stacks on the stand-in,
            // and a layer below stays under the window.
            var standing = StandFrames.TryGet(window, out var shown, out var overlay);
            if (standing)
                frame = shown;
            _alpha = MotionAlpha.Of(window);
            if (_alpha == 0 || _layer is not T layer || !Visible(target) || !(standing ? frame.Width > 0 && frame.Height > 0 : TryFrame(target, out frame)) || host.Place(frame, layer) is not LayerLayout layout)
            {
                Hide();
                return;
            }

            var box = layout.Box;
            var motion = Motion;
            if (box.Width != _box.Width || box.Height != _box.Height || !layer.Equals(_painted) || !_shown || motion != _paintedMotion || _alpha != _paintedAlpha)
            {
                for (var i = 0; i < _pieces.Length; i++)
                    Paint(i, box, layout, i < layout.Pieces.Count ? layout.Pieces[i] : default, layer, motion);

                _painted = layer;
                _paintedMotion = motion;
                _paintedAlpha = _alpha;
            }
            else if (box != _box)
            {
                for (var i = 0; i < _pieces.Length; i++)
                    Move(i, box, i < layout.Pieces.Count ? layout.Pieces[i] : default);
            }

            _box = box;
            _shown = true;
            Stack(standing && !host.Below ? overlay : target);
        }

        public void Dispose()
        {
            for (var i = 0; i < _pieces.Length; i++)
            {
                if (!_pieces[i].IsNull)
                    _ = PInvoke.DestroyWindow(_pieces[i]);

                _pieces[i] = HWND.Null;
            }
        }

        private void Hide()
        {
            if (!_shown)
                return;

            foreach (var piece in _pieces)
            {
                if (!piece.IsNull)
                    _ = PInvoke.ShowWindow(piece, SHOW_WINDOW_CMD.SW_HIDE);
            }

            _shown = false;
        }

        private unsafe void Paint(int index, Rectangle box, LayerLayout layout, Rectangle piece, T layer, GradientMotion motion)
        {
            if (piece.Width <= 0 || piece.Height <= 0)
            {
                if (!_pieces[index].IsNull)
                    _ = PInvoke.ShowWindow(_pieces[index], SHOW_WINDOW_CMD.SW_HIDE);

                return;
            }

            if (_pieces[index].IsNull)
                _pieces[index] = host.Create();

            var hwnd = _pieces[index];
            if (hwnd.IsNull)
                return;

            var screen = PInvoke.GetDC(HWND.Null);
            var memory = PInvoke.CreateCompatibleDC(screen);
            try
            {
                var info = new BITMAPINFO();
                info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
                info.bmiHeader.biWidth = piece.Width;
                info.bmiHeader.biHeight = -piece.Height;
                info.bmiHeader.biPlanes = 1;
                info.bmiHeader.biBitCount = 32;
                void* bits;
                var bitmap = PInvoke.CreateDIBSection(memory, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
                if (bitmap.IsNull || bits is null)
                    return;

                var old = PInvoke.SelectObject(memory, bitmap);
                host.Painter(new Span<uint>(bits, piece.Width * piece.Height), layer, box.Width, box.Height, layout.Frame, piece, motion);
                var size = new SIZE(piece.Width, piece.Height);
                var origin = new System.Drawing.Point(0, 0);
                var position = new System.Drawing.Point(box.X + piece.X, box.Y + piece.Y);
                var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = _alpha, AlphaFormat = 1 };
                _ = PInvoke.UpdateLayeredWindow(hwnd, screen, &position, &size, memory, &origin, new COLORREF(0), &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
                _ = PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
                _ = PInvoke.SelectObject(memory, old);
                _ = PInvoke.DeleteObject(bitmap);
            }
            finally
            {
                _ = PInvoke.DeleteDC(memory);
                _ = PInvoke.ReleaseDC(HWND.Null, screen);
            }
        }

        private void Move(int index, Rectangle box, Rectangle piece)
        {
            if (_pieces[index].IsNull || piece.Width <= 0 || piece.Height <= 0)
                return;

            _ = PInvoke.SetWindowPos(
                _pieces[index],
                HWND.Null,
                box.X + piece.X,
                box.Y + piece.Y,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        }

        // Just below or just above the window, so a window raised over it covers the layer too. Pieces already there,
        // among the other layers Winland keeps next to the window, stay put, so two kinds of layer do not keep trading
        // places.
        private void Stack(HWND target)
        {
            if (host.Below)
            {
                if (Beside(target, GET_WINDOW_CMD.GW_HWNDNEXT))
                    return;

                // Each piece goes under the one before it, the first under the window.
                var after = target;
                foreach (var piece in _pieces)
                {
                    if (piece.IsNull)
                        continue;

                    _ = PInvoke.SetWindowPos(
                        piece,
                        after,
                        0,
                        0,
                        0,
                        0,
                        SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
                    after = piece;
                }

                return;
            }

            if (Beside(target, GET_WINDOW_CMD.GW_HWNDPREV))
                return;

            var above = PInvoke.GetWindow(target, GET_WINDOW_CMD.GW_HWNDPREV);
            var topmost = (PInvoke.GetWindowLong(target, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) & TopmostStyle) != 0;
            var insert = topmost ? new HWND(-1) : above.IsNull ? new HWND(0) : above;
            foreach (var piece in _pieces)
            {
                if (piece.IsNull)
                    continue;

                _ = PInvoke.SetWindowPos(
                    piece,
                    insert,
                    0,
                    0,
                    0,
                    0,
                    SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
            }
        }

        // Whether one of these pieces is among the click-through layers of this process found next to the window, going
        // the one way through the z-order.
        private bool Beside(HWND target, GET_WINDOW_CMD direction)
        {
            var current = PInvoke.GetWindow(target, direction);
            for (var step = 0; step < 16 && !current.IsNull; step++)
            {
                if (Array.IndexOf(_pieces, current) >= 0)
                    return true;

                if (!OwnLayer(current))
                    return false;

                current = PInvoke.GetWindow(current, direction);
            }

            return false;
        }

        private static unsafe bool OwnLayer(HWND hwnd)
        {
            uint process = 0;
            _ = PInvoke.GetWindowThreadProcessId(hwnd, &process);
            return process == (uint)Environment.ProcessId
                && (PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) & TransparentStyle) != 0;
        }

        private static unsafe bool Visible(HWND target)
        {
            if (!PInvoke.IsWindowVisible(target) || PInvoke.IsIconic(target))
                return false;

            uint cloaked = 0;
            return PInvoke.DwmGetWindowAttribute(target, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(uint)).Failed || cloaked == 0;
        }

        private static unsafe bool TryFrame(HWND target, out Rectangle frame)
        {
            frame = default;
            RECT rect;
            if (PInvoke.DwmGetWindowAttribute(target, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &rect, (uint)sizeof(RECT)).Failed)
                return false;

            frame = new Rectangle(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
            return frame.Width > 0 && frame.Height > 0;
        }
    }

    private bool Below => below;

    private LayerLayout? Place(Rectangle frame, T layer) => place(frame, layer);

    private void Painter(Span<uint> pixels, T layer, int boxWidth, int boxHeight, Rectangle frame, Rectangle piece, GradientMotion motion) =>
        paint(pixels, layer, boxWidth, boxHeight, frame, piece, motion);
}
