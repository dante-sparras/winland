using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

/// <summary>
/// Draws the blur of the windows behind each translucent Client, and one blur per monitor while its special workspace is
/// open. Each layer is a click-through window just below its Client. The pictures come from a capture of each Client behind
/// it, laid over the wallpaper and blurred on the GPU. A machine with no hardware adapter draws none of these layers. The
/// layers go away with this process.
/// </summary>
// Stryker disable once all : draws on the desktop and captures real windows; the blur, the layout and the composite are tested in Winland.Core.
internal static class BehindHost
{
    private const string ClassName = "WinlandBehind";
    private const uint SyncMessage = 0x8000 + 0x51;
    private const uint QuitMessage = 0x0012;
    private const uint MouseActivateMessage = 0x0021;
    private const uint SettingChangeMessage = 0x001A;
    private const uint DisplayChangeMessage = 0x007E;
    private const uint TimerMessage = 0x0113;
    private const nuint SetWallpaperAction = 0x0014;
    private const uint FrameMilliseconds = 33;
    private const uint SettleMilliseconds = 500;
    private const int ClassAlreadyExists = 1410;
    private const int NoActivate = 3;
    private const int TransparentStyle = 0x00000020;
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(12);

    private static readonly WNDPROC Proc = OnWindow;
    private static readonly Lock Gate = new();
    private static readonly Dictionary<WindowId, Entry> Entries = [];
    private static readonly Dictionary<WindowId, WindowCapture> Captures = [];
    private static readonly Dictionary<WindowId, int> Refused = [];
    private static readonly HashSet<WindowId> Moved = [];

    private static Dictionary<WindowId, Wanted>? _pending;
    private static List<BackdropImage> _wallpapers = [];
    private static bool _wallpaperDirty = true;
    private static GpuKawase? _kawase;
    private static GpuDevice? _device;
    private static bool _posted;
    private static uint _thread;
    private static Thread? _worker;
    private static HWND _watcher;
    private static nuint _timer;
    private static nuint _settle;
    private static int _ticks;

    /// <summary>Replaces every behind-blur. <paramref name="specials"/> are the monitor layers. An empty call before the thread starts does nothing.</summary>
    internal static void Update(IReadOnlyDictionary<WindowId, BehindBlur> layers, IReadOnlyList<SpecialBlur> specials)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(specials);
        lock (Gate)
        {
            if (_worker is null && layers.Count == 0 && specials.Count == 0)
                return;
        }

        var wanted = new Dictionary<WindowId, Wanted>(layers.Count + specials.Count);
        foreach (var (window, blur) in layers)
            wanted[window] = new Wanted(blur, false, default, window);

        foreach (var special in specials)
            wanted[new WindowId(-((long)special.Monitor.Value + 1))] = new Wanted(special.Blur, true, special.Area, special.Anchor);

        lock (Gate)
        {
            if (_worker is null && wanted.Count == 0)
                return;

            _pending = wanted;
            Post();
        }
    }

    /// <summary>The window moved, resized, showed, hid or changed place in the z-order, so a blur that uses it follows.</summary>
    internal static void Follow(WindowId window)
    {
        lock (Gate)
        {
            if (_worker is null)
                return;

            Moved.Add(window);
            Post();
        }
    }

    /// <summary>Removes every blur and ends the thread. It returns before the shared device is shut down.</summary>
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
            _wallpaperDirty = true;
            _wallpapers = [];
        }

        if (worker is null)
            return;

        if (thread != 0)
            _ = PInvoke.PostThreadMessage(thread, QuitMessage, default, default);

        _ = worker.Join(TimeSpan.FromSeconds(2));
        WallpaperReader.Release();
    }

    private static void Post()
    {
        if (_worker is null)
        {
            _worker = new Thread(Run) { IsBackground = true, Name = "Winland behind blur" };
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
            _watcher = PInvoke.CreateWindowEx(0, ClassName, "", WINDOW_STYLE.WS_POPUP, 0, 0, 0, 0, HWND.Null, null, null, null);
            lock (Gate)
            {
                if (!ReferenceEquals(_worker, Thread.CurrentThread))
                    return;

                _thread = PInvoke.GetCurrentThreadId();
                _posted = true;
                _timer = PInvoke.SetTimer(HWND.Null, 0, FrameMilliseconds, null);
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

                    if (message.hwnd.IsNull && message.message == TimerMessage && message.wParam.Value == _timer)
                    {
                        Tick();
                        continue;
                    }

                    if (message.hwnd.IsNull && message.message == TimerMessage && message.wParam.Value == _settle)
                    {
                        _ = PInvoke.KillTimer(HWND.Null, _settle);
                        _settle = 0;
                        _wallpaperDirty = true;
                        Tick();
                        continue;
                    }

                    PInvoke.TranslateMessage(in message);
                    PInvoke.DispatchMessage(in message);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    var frame = exception.StackTrace?.Split('\n', 2)[0].Trim();
                    Console.Error.WriteLine($"The behind blur failed: {exception.GetType().Name}: {exception.Message} {frame}");
                }
            }
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
        }
        finally
        {
            if (_timer != 0)
                _ = PInvoke.KillTimer(HWND.Null, _timer);

            if (_settle != 0)
                _ = PInvoke.KillTimer(HWND.Null, _settle);

            _timer = 0;
            _settle = 0;
            lock (Gate)
            {
                foreach (var entry in Entries.Values)
                    entry.Dispose();

                Entries.Clear();
            }

            DropCaptures();
            _kawase?.Dispose();
            _kawase = null;
            _device = null;
            if (!_watcher.IsNull)
                _ = PInvoke.DestroyWindow(_watcher);

            _watcher = HWND.Null;
        }
    }

    private static void Sync()
    {
        Dictionary<WindowId, Wanted>? wanted;
        WindowId[] moved;
        lock (Gate)
        {
            wanted = _pending;
            _pending = null;
            moved = [.. Moved];
            Moved.Clear();
            _posted = false;
        }

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
                }
            }
        }

        if (moved.Length > 0)
        {
            lock (Gate)
            {
                foreach (var entry in Entries.Values)
                {
                    if (moved.Contains(entry.Window) || (entry.Anchor is WindowId anchor && moved.Contains(anchor)) || SourcesMoved(entry.Blur, moved))
                        entry.Dirty = true;
                }
            }
        }

        if (_wallpaperDirty)
            RebuildWallpaper();

        Tick();
    }

    private static bool SourcesMoved(BehindBlur blur, WindowId[] moved)
    {
        foreach (var window in moved)
        {
            if (blur.Sources.Contains(window))
                return true;
        }

        return false;
    }

    private static void Tick()
    {
        _ticks++;
        if (!Device())
        {
            lock (Gate)
            {
                foreach (var entry in Entries.Values)
                    entry.Hide();
            }

            return;
        }

        var sources = new HashSet<WindowId>();
        lock (Gate)
        {
            foreach (var entry in Entries.Values)
            {
                foreach (var source in entry.Blur.Sources)
                    sources.Add(source);
            }
        }

        EnsureCaptures(sources);
        RefreshCaptures();
        Entry[] entries;
        lock (Gate)
            entries = [.. Entries.Values];

        var start = Stopwatch.GetTimestamp();
        var painted = false;
        foreach (var entry in entries)
        {
            var stamp = Stamp(entry.Blur);
            if (!entry.Dirty && stamp == entry.Stamp && entry.Shown)
            {
                entry.Restack();
                continue;
            }

            if (painted && Stopwatch.GetElapsedTime(start) > Budget)
                break;

            entry.Place(stamp);
            painted = true;
        }
    }

    private static int Stamp(BehindBlur blur)
    {
        var stamp = 17;
        foreach (var source in blur.Sources)
            stamp = unchecked(stamp * 31 + (Captures.TryGetValue(source, out var capture) ? capture.Generation : -1));

        return stamp;
    }

    private static bool Device()
    {
        var gpu = Gpu.Acquire();
        if (ReferenceEquals(gpu, _device))
            return gpu is not null && _kawase is { Ready: true };

        DropCaptures();
        _kawase?.Dispose();
        _kawase = null;
        _device = gpu;
        if (gpu is null)
            return false;

        _kawase = new GpuKawase(gpu);
        return _kawase.Ready;
    }

    private static void EnsureCaptures(HashSet<WindowId> needed)
    {
        foreach (var window in Captures.Keys.ToArray())
        {
            if (needed.Contains(window))
                continue;

            Captures[window].Dispose();
            Captures.Remove(window);
            Refused.Remove(window);
        }

        if (_device is null)
            return;

        foreach (var window in needed)
        {
            if (Captures.ContainsKey(window))
                continue;

            if (Refused.TryGetValue(window, out var until) && _ticks < until)
                continue;

            WindowCapture? capture;
            lock (_device.Gate)
                capture = WindowCapture.TryStart(_device, (nint)window.Value);

            if (capture is null)
            {
                Refused[window] = _ticks + 30;
                continue;
            }

            Refused.Remove(window);
            Captures[window] = capture;
        }
    }

    private static void RefreshCaptures()
    {
        if (_device is null)
            return;

        lock (_device.Gate)
        {
            foreach (var capture in Captures.Values)
                _ = capture.Refresh();
        }
    }

    private static void DropCaptures()
    {
        foreach (var capture in Captures.Values)
            capture.Dispose();

        Captures.Clear();
        Refused.Clear();
    }

    private static void RebuildWallpaper()
    {
        _wallpaperDirty = false;
        var screens = WallpaperReader.Screens();
        var images = new List<BackdropImage>(screens.Count);
        if (screens.Count > 0)
        {
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
                images.Add(new BackdropImage(screen.Area, fitted));
            }
        }

        _wallpapers = images;
        lock (Gate)
        {
            foreach (var entry in Entries.Values)
                entry.Dirty = true;
        }
    }

    private static uint[]? BlurOf(Rectangle frame, BehindBlur layer)
    {
        if (_device is null || _kawase is not { Ready: true })
            return null;

        var cover = BehindBlurPlan.Cover(frame, BlurShaderParameters.Spread(layer.Tuning));
        if (cover.Width <= 0 || cover.Height <= 0 || cover.Width > BehindBlurPlan.MaxSide || cover.Height > BehindBlurPlan.MaxSide)
            return null;

        var frames = new List<(GpuTexture Texture, Rectangle Bounds)>();
        foreach (var source in layer.Sources)
        {
            if (!Captures.TryGetValue(source, out var capture) || capture.Picture is not { } picture)
                continue;

            if (!TryWindowFrame(new HWND((nint)source.Value), out var bounds))
                continue;

            frames.Add((picture, bounds));
        }

        uint[] blurred;
        lock (_device.Gate)
        {
            using var scene = _device.Create(cover.Width, cover.Height, GpuUse.Target);
            if (scene is null || !Compose(_device, scene, cover, frames))
                return null;

            blurred = new uint[cover.Width * cover.Height];
            if (!_kawase.BlurTexture(scene, cover.Width, cover.Height, layer.Tuning, blurred))
            {
                var scenePixels = new uint[blurred.Length];
                if (!GpuKawase.ReadInto(_device, scene, cover.Width, cover.Height, scenePixels)
                    || !new CpuPictureBlur().TryApply(scenePixels, cover.Width, cover.Height, layer.Tuning, blurred))
                {
                    return null;
                }
            }
        }

        var shown = new uint[frame.Width * frame.Height];
        XrayPainter.Paint(shown, frame, [new BackdropImage(cover, blurred)], new XrayBackdrop(layer.Tuning, layer.Rounding, layer.RoundingPower, layer.Alpha));
        return shown;
    }

    private static bool Compose(GpuDevice gpu, GpuTexture scene, Rectangle cover, List<(GpuTexture Texture, Rectangle Bounds)> frames)
    {
        gpu.ClearTarget(scene);
        var constants = new float[16];
        constants[0] = cover.Width;
        constants[1] = cover.Height;
        constants[4] = 1;
        constants[5] = 1;
        var drew = false;
        var uploads = new List<GpuTexture>();
        try
        {
            foreach (var image in _wallpapers)
            {
                if (!Crop(image, cover, out var placed, out var cropped))
                    continue;

                var texture = gpu.Create(placed.Width, placed.Height, GpuUse.Source);
                if (texture is null || !gpu.Upload(texture, cropped, placed.Width, placed.Height))
                    return false;

                uploads.Add(texture);
                if (!BehindBlurPlan.TryQuad(cover, placed, out var quad))
                    continue;

                if (!gpu.Draw(scene, cover.Width, cover.Height, texture, constants, Quad(quad), blur: false, clear: false))
                    return false;

                drew = true;
            }

            foreach (var (texture, bounds) in frames)
            {
                if (!BehindBlurPlan.TryQuad(cover, bounds, out var quad))
                    continue;

                if (!gpu.Draw(scene, cover.Width, cover.Height, texture, constants, Quad(quad), blur: false, clear: false))
                    return false;

                drew = true;
            }

            return drew;
        }
        finally
        {
            foreach (var texture in uploads)
                texture.Dispose();
        }
    }

    private static bool Crop(BackdropImage image, Rectangle box, out Rectangle placed, out uint[] pixels)
    {
        placed = default;
        pixels = [];
        var left = Math.Max(box.X, image.Area.X);
        var top = Math.Max(box.Y, image.Area.Y);
        var right = Math.Min(box.X + box.Width, image.Area.X + image.Area.Width);
        var bottom = Math.Min(box.Y + box.Height, image.Area.Y + image.Area.Height);
        if (right <= left || bottom <= top || image.Pixels.Length < (long)image.Area.Width * image.Area.Height)
            return false;

        placed = new Rectangle(left, top, right - left, bottom - top);
        pixels = new uint[placed.Width * placed.Height];
        for (var y = 0; y < placed.Height; y++)
        {
            var from = ((top - image.Area.Y + y) * image.Area.Width) + (left - image.Area.X);
            image.Pixels.AsSpan(from, placed.Width).CopyTo(pixels.AsSpan(y * placed.Width, placed.Width));
        }

        return true;
    }

    private static MeshVertex[] Quad(BlurQuad quad) =>
    [
        new(quad.X, quad.Y, quad.U0, quad.V0),
        new(quad.X + quad.Width, quad.Y, quad.U1, quad.V0),
        new(quad.X, quad.Y + quad.Height, quad.U0, quad.V1),
        new(quad.X + quad.Width, quad.Y, quad.U1, quad.V0),
        new(quad.X + quad.Width, quad.Y + quad.Height, quad.U1, quad.V1),
        new(quad.X, quad.Y + quad.Height, quad.U0, quad.V1),
    ];

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

    private static void Settle()
    {
        if (_settle != 0)
            _ = PInvoke.KillTimer(HWND.Null, _settle);

        _settle = PInvoke.SetTimer(HWND.Null, 0, SettleMilliseconds, null);
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

    private static unsafe HWND CreatePiece() =>
        PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TRANSPARENT,
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

    private sealed record Wanted(BehindBlur Blur, bool Monitor, Rectangle Area, WindowId? Anchor);

    private sealed class Entry(WindowId window) : IDisposable
    {
        private HWND _piece;
        private BehindBlur _blur = new(BlurTuning.Documented, 0, 2f, 255, []);
        private bool _monitor;
        private Rectangle _area;
        private WindowId? _anchor = window;
        private Rectangle _box;
        private byte _alpha = 255;
        private byte _paintedAlpha = 255;
        private bool _shown;

        public BehindBlur Blur => _blur;

        public WindowId Window => window;

        public WindowId? Anchor => _anchor;

        public bool Shown => _shown;

        public bool Dirty { get; set; } = true;

        public int Stamp { get; private set; } = -1;

        public void Take(Wanted wanted)
        {
            if (_monitor != wanted.Monitor || _area != wanted.Area || _anchor != wanted.Anchor || !Same(_blur, wanted.Blur))
                Dirty = true;

            _blur = wanted.Blur;
            _monitor = wanted.Monitor;
            _area = wanted.Area;
            _anchor = wanted.Anchor;
        }

        public void Place(int stamp)
        {
            if (!TryFrame(out var frame))
            {
                Hide();
                return;
            }

            _alpha = _monitor ? (byte)255 : MotionAlpha.Of(window);
            if (_alpha == 0)
            {
                Hide();
                return;
            }

            if (Dirty || frame != _box || _alpha != _paintedAlpha || stamp != Stamp)
            {
                if (!Paint(frame))
                {
                    Hide();
                    return;
                }

                Stamp = stamp;
                _paintedAlpha = _alpha;
                Dirty = false;
            }

            _box = frame;
            _shown = true;
            Restack();
        }

        public void Restack()
        {
            if (_piece.IsNull)
                return;

            var target = _anchor is WindowId anchor ? new HWND((nint)anchor.Value) : HWND.Null;
            if (target.IsNull || !PInvoke.IsWindow(target))
            {
                PlaceAtTop();
                return;
            }

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

        public void Hide()
        {
            if (!_shown)
                return;

            if (!_piece.IsNull)
                _ = PInvoke.ShowWindow(_piece, SHOW_WINDOW_CMD.SW_HIDE);

            _shown = false;
            Dirty = true;
        }

        public void Dispose()
        {
            if (!_piece.IsNull)
                _ = PInvoke.DestroyWindow(_piece);

            _piece = HWND.Null;
        }

        private bool TryFrame(out Rectangle frame)
        {
            if (_monitor)
            {
                frame = _area;
                return frame.Width > 0 && frame.Height > 0;
            }

            var target = new HWND((nint)window.Value);
            if (StandFrames.TryGet(window, out var shown, out _))
            {
                frame = shown;
                return frame.Width > 0 && frame.Height > 0 && Visible(target);
            }

            if (!Visible(target))
            {
                frame = default;
                return false;
            }

            return TryWindowFrame(target, out frame);
        }

        private unsafe bool Paint(Rectangle box)
        {
            var pixels = BlurOf(box, _blur);
            if (pixels is null)
                return false;

            if (_piece.IsNull)
                _piece = CreatePiece();

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
                pixels.AsSpan().CopyTo(new Span<uint>(bits, pixels.Length));
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

        private void PlaceAtTop() =>
            _ = PInvoke.SetWindowPos(
                _piece,
                HWND.Null,
                0,
                0,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

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

        private static bool Same(BehindBlur one, BehindBlur other) =>
            one.Tuning == other.Tuning
            && one.Rounding == other.Rounding
            && one.RoundingPower == other.RoundingPower
            && one.Alpha == other.Alpha
            && one.Sources.SequenceEqual(other.Sources);
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

    private static unsafe bool TryWindowFrame(HWND target, out Rectangle frame)
    {
        frame = default;
        RECT rect;
        if (PInvoke.DwmGetWindowAttribute(target, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &rect, (uint)sizeof(RECT)).Failed)
            return false;

        frame = new Rectangle(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
        return frame.Width > 0 && frame.Height > 0;
    }
}
