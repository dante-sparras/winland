using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

/// <summary>
/// Draws each Client's border outside its frame, as Winland draws it, on its own thread. A border is four
/// thin click-through layered windows, top, bottom, left and right, kept just above the Client in the z-order. They
/// follow the window wherever it moves, by the frame Windows reports, so a move or an animation carries the border
/// along. The borders go away with this process.
/// </summary>
// Stryker disable once all : draws on the desktop; the ring's pixels are made and tested in BorderPainter.
internal static class BorderHost
{
    private const string ClassName = DecorationStack.BorderClass;
    private const int ClassAlreadyExists = 1410;
    private const uint SyncMessage = 0x8000 + 0x4B;
    private const uint QuitMessage = 0x0012;
    private const uint MouseActivateMessage = 0x0021;
    private const int NoActivate = 3;
    private const int TopmostStyle = 0x00000008;

    private static readonly WNDPROC Proc = OnWindow;
    private static readonly Lock Gate = new();
    private static readonly Dictionary<WindowId, Entry> Entries = [];
    private static readonly HashSet<WindowId> Moved = [];
    private static readonly Dictionary<WindowId, BorderColor?> Tints = [];
    private static Dictionary<WindowId, BorderRing>? _pending;
    private static bool _posted;
    private static uint _thread;
    private static Thread? _worker;

    /// <summary>Replaces every border with <paramref name="rings"/>. Starts the thread for the first one.</summary>
    internal static void Update(IReadOnlyDictionary<WindowId, BorderRing> rings)
    {
        ArgumentNullException.ThrowIfNull(rings);
        lock (Gate)
        {
            if (_worker is null && rings.Count == 0)
                return;

            _pending = new Dictionary<WindowId, BorderRing>(rings);
            Post();
        }
    }

    /// <summary>The window moved, resized, showed or hid, so its border follows.</summary>
    internal static void Follow(WindowId window)
    {
        lock (Gate)
        {
            if (_worker is null || !Entries.ContainsKey(window) && _pending?.ContainsKey(window) != true)
                return;

            Moved.Add(window);
            Post();
        }
    }

    /// <summary>
    /// Draws the border in one color while a border color animation runs, or puts its color back when
    /// <paramref name="color"/> is null.
    /// </summary>
    internal static void Tint(WindowId window, BorderColor? color)
    {
        lock (Gate)
        {
            if (_worker is null)
                return;

            Tints[window] = color;
            Moved.Add(window);
            Post();
        }
    }

    /// <summary>Removes every border and ends the thread.</summary>
    internal static void Stop()
    {
        Thread? worker;
        uint thread;
        lock (Gate)
        {
            worker = _worker;
            thread = _thread;
            _worker = null;
            _thread = 0;
            _pending = null;
            _posted = false;
            Moved.Clear();
            Tints.Clear();
        }

        if (worker is null)
            return;

        if (thread != 0)
            _ = PInvoke.PostThreadMessage(thread, QuitMessage, default, default);

        _ = worker.Join(TimeSpan.FromSeconds(2));
    }

    // Called with the gate held.
    private static void Post()
    {
        if (_worker is null)
        {
            _worker = new Thread(Run) { IsBackground = true, Name = "Winland borders" };
            _worker.Start();
        }

        if (_posted || _thread == 0)
            return;

        _posted = true;
        _ = PInvoke.PostThreadMessage(_thread, SyncMessage, default, default);
    }

    private static void Run()
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

        lock (Gate)
        {
            if (!ReferenceEquals(_worker, Thread.CurrentThread))
                return;

            _thread = PInvoke.GetCurrentThreadId();
            _posted = true;
            _ = PInvoke.PostThreadMessage(_thread, SyncMessage, default, default);
        }

        while (PInvoke.GetMessage(out var message, default, 0, 0) > 0)
        {
            try
            {
                if (message.hwnd.IsNull && message.message == SyncMessage)
                {
                    Sync();
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

        lock (Gate)
        {
            foreach (var entry in Entries.Values)
                entry.Dispose();

            Entries.Clear();
        }
    }

    private static void Sync()
    {
        Dictionary<WindowId, BorderRing>? wanted;
        WindowId[] moved;
        Dictionary<WindowId, BorderColor?> tints;
        lock (Gate)
        {
            wanted = _pending;
            _pending = null;
            moved = [.. Moved];
            Moved.Clear();
            tints = new Dictionary<WindowId, BorderColor?>(Tints);
            _posted = false;
        }

        var touched = new HashSet<WindowId>(moved);
        List<Entry> gone = [];
        if (wanted is not null)
        {
            lock (Gate)
            {
                foreach (var window in Entries.Keys.ToArray())
                {
                    if (wanted.ContainsKey(window))
                        continue;

                    gone.Add(Entries[window]);
                    Entries.Remove(window);
                    Tints.Remove(window);
                }

                foreach (var (window, ring) in wanted)
                {
                    if (!Entries.TryGetValue(window, out var entry))
                    {
                        entry = new Entry(window);
                        Entries[window] = entry;
                    }

                    entry.Ring = ring;
                    touched.Add(window);
                }
            }
        }

        // Destroying a border window takes a while. A motion tinting another border waits for the gate meanwhile.
        foreach (var entry in gone)
            entry.Dispose();

        foreach (var window in touched)
        {
            Entry? entry;
            lock (Gate)
                Entries.TryGetValue(window, out entry);

            if (entry is null)
                continue;

            entry.Tint = tints.GetValueOrDefault(window);
            entry.Place();
        }
    }

    private static LRESULT OnWindow(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam) =>
        message == MouseActivateMessage ? new LRESULT(NoActivate) : PInvoke.DefWindowProc(hwnd, message, wParam, lParam);

    private static unsafe void Register()
    {
        var module = PInvoke.GetModuleHandle((string?)null);
        var instance = (HINSTANCE)module.DangerousGetHandle();
        module.SetHandleAsInvalid();
        fixed (char* name = ClassName)
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
                throw new InvalidOperationException($"The border window class was not registered ({Marshal.GetLastPInvokeError()}).");
        }
    }

    private static unsafe HWND Create() =>
        PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE
                | WINDOW_EX_STYLE.WS_EX_TRANSPARENT,
            ClassName,
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

    private sealed class Entry(WindowId window) : IDisposable
    {
        private readonly HWND[] _pieces = new HWND[4];
        private Rectangle _box;
        private BorderRing? _painted;
        private bool _shown;
        private byte _alpha = 255;
        private byte _paintedAlpha = 255;

        public BorderRing Ring { get; set; } = new(default, 0, 0, 0);

        public BorderColor? Tint { get; set; }

        private BorderRing Drawn => Tint is BorderColor tint ? Ring with { Color = tint } : Ring;

        public void Place()
        {
            var target = new HWND((nint)window.Value);
            var frame = default(Rectangle);

            // A window with a stand-in is drawn in the stand-in's rectangle, and the border stacks on the stand-in.
            var standing = StandFrames.TryGet(window, out var shown, out var overlay);
            if (standing)
                frame = shown;
            _alpha = MotionAlpha.Of(window);
            if (_alpha == 0 || !Visible(target) || !(standing ? frame.Width > 0 && frame.Height > 0 : TryFrame(target, out frame)) || Ring.Thickness <= 0)
            {
                Hide();
                return;
            }

            var thickness = Ring.Thickness;
            var box = new Rectangle(frame.X - thickness, frame.Y - thickness, frame.Width + (2 * thickness), frame.Height + (2 * thickness));
            var drawn = Drawn;
            var pieces = BorderPainter.Pieces(box.Width, box.Height, drawn);
            if (box.Width != _box.Width || box.Height != _box.Height || !drawn.Equals(_painted) || !_shown)
            {
                for (var i = 0; i < pieces.Length; i++)
                    Paint(i, box, pieces[i], drawn);

                _painted = drawn;
                _paintedAlpha = _alpha;
            }
            else
            {
                // A fade or a move keeps the pictures the pieces already have.
                if (_alpha != _paintedAlpha)
                {
                    for (var i = 0; i < pieces.Length; i++)
                        Fade(i, pieces[i]);

                    _paintedAlpha = _alpha;
                }

                if (box != _box)
                {
                    for (var i = 0; i < pieces.Length; i++)
                        Move(i, box, pieces[i]);
                }
            }

            _box = box;
            _shown = true;
            Stack(standing ? overlay : target);
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

        private unsafe void Paint(int index, Rectangle box, Rectangle piece, BorderRing ring)
        {
            if (piece.Width <= 0 || piece.Height <= 0)
            {
                if (!_pieces[index].IsNull)
                    _ = PInvoke.ShowWindow(_pieces[index], SHOW_WINDOW_CMD.SW_HIDE);

                return;
            }

            if (_pieces[index].IsNull)
                _pieces[index] = Create();

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
                BorderPainter.Paint(new Span<uint>(bits, piece.Width * piece.Height), ring, box.Width, box.Height, piece);
                var size = new SIZE(piece.Width, piece.Height);
                var origin = new System.Drawing.Point(0, 0);
                var position = new System.Drawing.Point(box.X + piece.X, box.Y + piece.Y);
                var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = _alpha, AlphaFormat = 1 };
                _ = PInvoke.UpdateLayeredWindow(hwnd, screen, &position, &size, memory, &origin, new COLORREF(0), &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
                if (!PInvoke.IsWindowVisible(hwnd))
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

        private unsafe void Fade(int index, Rectangle piece)
        {
            if (_pieces[index].IsNull || piece.Width <= 0 || piece.Height <= 0)
                return;

            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = _alpha, AlphaFormat = 1 };
            _ = PInvoke.UpdateLayeredWindow(_pieces[index], HDC.Null, null, null, HDC.Null, null, new COLORREF(0), &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
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

        // Just above the window, so a window raised over it covers its border too. Pieces already among the
        // decorations right above it stay put, whatever order a tab bar took among them.
        private void Stack(HWND target)
        {
            var chain = new List<HWND>();
            var above = DecorationStack.Above(target, chain);
            if (Array.TrueForAll(_pieces, piece => piece.IsNull || chain.Contains(piece)))
                return;

            var topmost = (PInvoke.GetWindowLong(target, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) & TopmostStyle) != 0;
            var after = !above.IsNull ? above : topmost ? new HWND(-1) : new HWND(0);
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
            }
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
}
