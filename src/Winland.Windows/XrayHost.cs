using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

/// <summary>
/// Draws the blurred wallpaper under each translucent Client when <c>decoration:blur:xray</c> is on, as the pinned release
/// draws its precomputed blur: one click-through layered window per Client, just below it in the z-order and cut to its
/// frame and corners. The wallpaper of each monitor is read, fitted as Windows shows it and blurred once on a worker
/// thread, and again when the wallpaper, the monitor layout or the blur options change. The layers go away with this
/// process.
/// </summary>
// Stryker disable once all : draws on the desktop and reads the real wallpaper; the blur and the cut are made and tested in Winland.Core.
internal static class XrayHost
{
    private const string ClassName = "WinlandXray";
    private const uint SyncMessage = 0x8000 + 0x4D;
    private const uint ReadyMessage = 0x8000 + 0x4E;
    private const uint QuitMessage = 0x0012;
    private const uint MouseActivateMessage = 0x0021;
    private const uint SettingChangeMessage = 0x001A;
    private const uint DisplayChangeMessage = 0x007E;
    private const uint TimerMessage = 0x0113;
    private const nuint SetWallpaperAction = 0x0014;
    private const uint SettleMilliseconds = 500;
    private const int ClassAlreadyExists = 1410;
    private const int NoActivate = 3;
    private const int TransparentStyle = 0x00000020;

    private static readonly WNDPROC Proc = OnWindow;
    private static readonly Lock Gate = new();
    private static readonly Dictionary<WindowId, Entry> Entries = [];
    private static readonly HashSet<WindowId> Moved = [];

    private static Dictionary<WindowId, XrayBackdrop>? _pending;
    private static IReadOnlyList<BackdropImage> _ready = [];
    private static IReadOnlyList<BackdropImage> _images = [];
    private static BlurTuning? _made;
    private static BlurTuning? _wanted;
    private static bool _computing;
    private static bool _again;
    private static int _version;
    private static bool _posted;
    private static uint _thread;
    private static Thread? _worker;
    private static HWND _watcher;
    private static nuint _settleTimer;

    /// <summary>Replaces every backdrop with <paramref name="backdrops"/>. Starts the thread for the first one.</summary>
    internal static void Update(IReadOnlyDictionary<WindowId, XrayBackdrop> backdrops)
    {
        ArgumentNullException.ThrowIfNull(backdrops);
        lock (Gate)
        {
            if (_worker is null && backdrops.Count == 0)
                return;

            _pending = new Dictionary<WindowId, XrayBackdrop>(backdrops);
            Post();
        }
    }

    /// <summary>The window moved, resized, showed, hid or changed place in the z-order, so its backdrop follows.</summary>
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

    /// <summary>Removes every backdrop and ends the thread.</summary>
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
            _version++;
            _images = [];
            _ready = [];
            _made = null;
            _wanted = null;
            _computing = false;
            _again = false;
        }

        if (worker is null)
            return;

        if (thread != 0)
            _ = PInvoke.PostThreadMessage(thread, QuitMessage, default, default);

        _ = worker.Join(TimeSpan.FromSeconds(2));
        WallpaperReader.Release();
    }

    // Called with the gate held.
    private static void Post()
    {
        if (_worker is null)
        {
            _worker = new Thread(Run) { IsBackground = true, Name = "Winland xray backdrops" };
            _worker.Start();
        }

        if (_posted || _thread == 0)
            return;

        _posted = true;
        _ = PInvoke.PostThreadMessage(_thread, SyncMessage, default, default);
    }

    private static unsafe void Run()
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

        // A top-level window of its own, which Windows sends the wallpaper and display changes to.
        _watcher = PInvoke.CreateWindowEx(0, ClassName, "", WINDOW_STYLE.WS_POPUP, 0, 0, 0, 0, HWND.Null, null, null, null);
        lock (Gate)
        {
            if (!ReferenceEquals(_worker, Thread.CurrentThread))
            {
                DestroyWatcher();
                return;
            }

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

                if (message.hwnd.IsNull && message.message == ReadyMessage)
                {
                    Ready();
                    continue;
                }

                if (message.hwnd.IsNull && message.message == TimerMessage && message.wParam.Value == _settleTimer)
                {
                    _ = PInvoke.KillTimer(HWND.Null, _settleTimer);
                    _settleTimer = 0;
                    Refresh();
                    continue;
                }

                PInvoke.TranslateMessage(in message);
                PInvoke.DispatchMessage(in message);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Report("The xray backdrops failed", exception);
            }
        }

        if (_settleTimer != 0)
            _ = PInvoke.KillTimer(HWND.Null, _settleTimer);

        _settleTimer = 0;
        lock (Gate)
        {
            foreach (var entry in Entries.Values)
                entry.Dispose();

            Entries.Clear();
        }

        DestroyWatcher();
    }

    // The exception's type and the first frame of its stack, so a failure can be found.
    private static void Report(string what, Exception exception)
    {
        var frame = exception.StackTrace?.Split('\n', 2)[0].Trim();
        Console.Error.WriteLine($"{what}: {exception.GetType().Name}: {exception.Message} {frame}");
    }

    private static void DestroyWatcher()
    {
        if (!_watcher.IsNull)
            _ = PInvoke.DestroyWindow(_watcher);

        _watcher = HWND.Null;
    }

    private static void Sync()
    {
        Dictionary<WindowId, XrayBackdrop>? wanted;
        WindowId[] moved;
        lock (Gate)
        {
            wanted = _pending;
            _pending = null;
            moved = [.. Moved];
            Moved.Clear();
            _posted = false;
        }

        var touched = new HashSet<WindowId>(moved);
        if (wanted is not null)
        {
            lock (Gate)
            {
                foreach (var window in Entries.Keys.ToArray())
                {
                    if (wanted.ContainsKey(window))
                        continue;

                    Entries[window].Dispose();
                    Entries.Remove(window);
                }

                foreach (var (window, layer) in wanted)
                {
                    if (!Entries.TryGetValue(window, out var entry))
                    {
                        entry = new Entry(window);
                        Entries[window] = entry;
                    }

                    entry.Take(layer);
                    touched.Add(window);
                }
            }

            Prepare(wanted.Count == 0 ? null : wanted.Values.First().Tuning);
        }

        foreach (var window in touched)
        {
            Entry? entry;
            lock (Gate)
                Entries.TryGetValue(window, out entry);

            entry?.Place(_version);
        }
    }

    // Makes the blurred wallpaper for these options when it is not made, or lets it go when no window needs it.
    private static void Prepare(BlurTuning? tuning)
    {
        if (tuning is null)
        {
            lock (Gate)
            {
                _version++;
                _images = [];
                _ready = [];
                _made = null;
                _wanted = null;
                _again = false;
            }

            WallpaperReader.Release();
            return;
        }

        bool start;
        lock (Gate)
        {
            _wanted = tuning;
            if (_computing)
            {
                _again = _again || !tuning.Equals(_made);
                return;
            }

            start = !tuning.Equals(_made);
        }

        if (start)
            Compute(tuning);
    }

    // The wallpaper or the monitors changed: blur it again for the options in use.
    private static void Refresh()
    {
        BlurTuning? tuning;
        lock (Gate)
        {
            tuning = _wanted;
            if (tuning is null)
                return;

            if (_computing)
            {
                _again = true;
                _made = null;
                return;
            }

            _made = null;
        }

        Compute(tuning);
    }

    private static void Compute(BlurTuning tuning)
    {
        uint thread;
        lock (Gate)
        {
            _computing = true;
            _made = tuning;
            thread = _thread;
        }

        _ = Task.Run(() =>
        {
            IReadOnlyList<BackdropImage> images = [];
            try
            {
                images = Build(tuning);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Report("The blurred wallpaper was not made", exception);
            }

            lock (Gate)
                _ready = images;

            if (thread != 0)
                _ = PInvoke.PostThreadMessage(thread, ReadyMessage, default, default);
        });
    }

    private static void Ready()
    {
        BlurTuning? again = null;
        lock (Gate)
        {
            if (!_computing)
                return;

            _computing = false;
            _images = _ready;
            _ready = [];
            _version++;
            if (_again && _wanted is BlurTuning wanted)
            {
                again = wanted;
                _made = null;
            }

            _again = false;
        }

        Entry[] entries;
        int version;
        lock (Gate)
        {
            entries = [.. Entries.Values];
            version = _version;
        }

        foreach (var entry in entries)
            entry.Place(version);

        if (again is not null)
            Compute(again);
    }

    // The blurred wallpaper of each monitor, fitted to it as Windows shows it.
    private static List<BackdropImage> Build(BlurTuning tuning)
    {
        var screens = WallpaperReader.Screens();
        var images = new List<BackdropImage>(screens.Count);
        if (screens.Count == 0)
            return images;

        var left = screens.Min(screen => screen.Area.X);
        var top = screens.Min(screen => screen.Area.Y);
        var right = screens.Max(screen => screen.Area.X + screen.Area.Width);
        var bottom = screens.Max(screen => screen.Area.Y + screen.Area.Height);
        foreach (var screen in screens)
        {
            var span = screen.Position == WallpaperPosition.Span;
            var picture = screen.Path is null ? null : WallpaperReader.Load(screen.Path);
            var fitted = WallpaperFit.Render(
                picture is null ? [] : picture.Pixels,
                picture?.Width ?? 0,
                picture?.Height ?? 0,
                screen.Position,
                screen.Background,
                span ? right - left : screen.Area.Width,
                span ? bottom - top : screen.Area.Height,
                span ? screen.Area.X - left : 0,
                span ? screen.Area.Y - top : 0,
                screen.Area.Width,
                screen.Area.Height);
            images.Add(new BackdropImage(screen.Area, BlurPipeline.Blur(fitted, screen.Area.Width, screen.Area.Height, tuning)));
        }

        return images;
    }

    private static LRESULT OnWindow(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case MouseActivateMessage:
                return new LRESULT(NoActivate);
            case SettingChangeMessage when wParam.Value == SetWallpaperAction:
            case DisplayChangeMessage:
                Settle();
                break;
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    // A change comes as several messages, so the blur starts once they stop.
    private static void Settle()
    {
        if (_settleTimer != 0)
            _ = PInvoke.KillTimer(HWND.Null, _settleTimer);

        _settleTimer = PInvoke.SetTimer(HWND.Null, 0, SettleMilliseconds, null);
    }

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
                throw new InvalidOperationException($"The {ClassName} window class was not registered ({Marshal.GetLastPInvokeError()}).");
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
        private HWND _piece;
        private XrayBackdrop? _layer;
        private XrayBackdrop? _painted;
        private Rectangle _box;
        private byte _alpha = 255;
        private byte _paintedAlpha = 255;
        private int _paintedVersion = -1;
        private bool _shown;

        public void Take(XrayBackdrop layer) => _layer = layer;

        public void Place(int version)
        {
            var target = new HWND((nint)window.Value);
            var frame = default(Rectangle);

            // A window with a stand-in is drawn in the stand-in's rectangle, and the backdrop stays under the window.
            var standing = StandFrames.TryGet(window, out var shown, out _);
            if (standing)
                frame = shown;

            _alpha = MotionAlpha.Of(window);
            IReadOnlyList<BackdropImage> images;
            lock (Gate)
                images = _images;

            if (_alpha == 0 || _layer is not XrayBackdrop layer || images.Count == 0 || !Visible(target)
                || !(standing ? frame.Width > 0 && frame.Height > 0 : TryFrame(target, out frame)))
            {
                Hide();
                return;
            }

            if (!_shown || frame != _box || !layer.Equals(_painted) || _alpha != _paintedAlpha || version != _paintedVersion)
            {
                if (!Paint(frame, layer, images))
                {
                    Hide();
                    return;
                }

                _painted = layer;
                _paintedAlpha = _alpha;
                _paintedVersion = version;
            }

            _box = frame;
            _shown = true;
            Stack(target);
        }

        public void Dispose()
        {
            if (!_piece.IsNull)
                _ = PInvoke.DestroyWindow(_piece);

            _piece = HWND.Null;
        }

        private void Hide()
        {
            if (!_shown)
                return;

            if (!_piece.IsNull)
                _ = PInvoke.ShowWindow(_piece, SHOW_WINDOW_CMD.SW_HIDE);

            _shown = false;
        }

        private unsafe bool Paint(Rectangle box, XrayBackdrop layer, IReadOnlyList<BackdropImage> images)
        {
            if (_piece.IsNull)
                _piece = Create();

            if (_piece.IsNull)
                return false;

            var screen = PInvoke.GetDC(HWND.Null);
            var memory = PInvoke.CreateCompatibleDC(screen);
            try
            {
                var info = new BITMAPINFO();
                info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
                info.bmiHeader.biWidth = box.Width;
                info.bmiHeader.biHeight = -box.Height;
                info.bmiHeader.biPlanes = 1;
                info.bmiHeader.biBitCount = 32;
                void* bits;
                var bitmap = PInvoke.CreateDIBSection(memory, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
                if (bitmap.IsNull || bits is null)
                    return false;

                var old = PInvoke.SelectObject(memory, bitmap);
                XrayPainter.Paint(new Span<uint>(bits, box.Width * box.Height), box, images, layer);
                var size = new SIZE(box.Width, box.Height);
                var origin = new System.Drawing.Point(0, 0);
                var position = new System.Drawing.Point(box.X, box.Y);
                var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = _alpha, AlphaFormat = 1 };
                _ = PInvoke.UpdateLayeredWindow(_piece, screen, &position, &size, memory, &origin, new COLORREF(0), &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
                _ = PInvoke.ShowWindow(_piece, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
                _ = PInvoke.SelectObject(memory, old);
                _ = PInvoke.DeleteObject(bitmap);
                return true;
            }
            finally
            {
                _ = PInvoke.DeleteDC(memory);
                _ = PInvoke.ReleaseDC(HWND.Null, screen);
            }
        }

        // Just below the window, so a window raised over it covers the backdrop too. Already there, among the other
        // layers Winland keeps next to the window, it stays put, so two kinds of layer do not keep trading places.
        private void Stack(HWND target)
        {
            if (Beside(target))
                return;

            _ = PInvoke.SetWindowPos(
                _piece,
                target,
                0,
                0,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        }

        private bool Beside(HWND target)
        {
            var current = PInvoke.GetWindow(target, GET_WINDOW_CMD.GW_HWNDNEXT);
            for (var step = 0; step < 16 && !current.IsNull; step++)
            {
                if (current == _piece)
                    return true;

                if (!OwnLayer(current))
                    return false;

                current = PInvoke.GetWindow(current, GET_WINDOW_CMD.GW_HWNDNEXT);
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
}
