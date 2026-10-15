using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

public sealed class WindowAdapter : IDisposable
{
    private const int ChildStyle = 0x40000000;
    private const int ToolStyle = 0x00000080;
    private const int DialogStyle = 0x00000001;

    private static readonly Lock ShowGate = new();
    private static volatile bool Releasing;
    private static Action<Outcome>? Applied;
    private static Core? Session;

    // DWMWA_BORDER_COLOR, DWMWA_WINDOW_CORNER_PREFERENCE.
    private const DWMWINDOWATTRIBUTE BorderColorAttribute = (DWMWINDOWATTRIBUTE)34;
    private const DWMWINDOWATTRIBUTE CornerAttribute = (DWMWINDOWATTRIBUTE)33;

    // DWMWA_COLOR_DEFAULT, DWMWA_COLOR_NONE, DWMWCP_DEFAULT, DWMWCP_DONOTROUND, DWMWCP_ROUND.
    private const uint DefaultBorderColor = 0xFFFFFFFFu;
    private const uint NoneBorderColor = 0xFFFFFFFEu;
    private const uint DefaultCorners = 0;
    private const uint SquareCorners = 1;
    private const uint RoundCorners = 2;

    private readonly Core _core;
    private readonly Action<RecoverySnapshot>? _changed;
    private readonly Action<Outcome>? _publish;
    private readonly WINEVENTPROC _callback;
    private readonly Dictionary<WindowId, (Size? Min, Size? Max)> _limits = [];
    private readonly Lock _limitGate = new();
    private HWINEVENTHOOK _objects;
    private HWINEVENTHOOK _minimize;
    private HWINEVENTHOOK _foreground;
    private bool _disposed;

    // Apply is static because the keyboard hook and the command server both call it.
    private static readonly HashSet<WindowId> Painted = [];
    private static readonly OpacityAdapter Opacity = new(new Win32WindowStyles());
    private static readonly BorderlessFrame Borderless = new(new Win32WindowFrame());
    private static readonly Lock PaintGate = new();
    private static readonly Dictionary<WindowId, CachedFrame> Frames = [];
    private static readonly Lock FrameGate = new();
    private static int Applying;

    public WindowAdapter(Core core, Action<RecoverySnapshot>? changed = null)
    {
        ArgumentNullException.ThrowIfNull(core);
        _core = core;
        Session = core;
        _callback = OnEvent;
        _changed = changed;
        if (changed is null)
            return;

        _publish = _ => Publish();
        Applied = _publish;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_objects.IsNull)
            return;

        var flags = PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS;
        _objects = PInvoke.SetWinEventHook(
            PInvoke.EVENT_OBJECT_CREATE,
            PInvoke.EVENT_OBJECT_UNCLOAKED,
            default(HMODULE),
            _callback,
            0,
            0,
            flags);
        _minimize = PInvoke.SetWinEventHook(
            PInvoke.EVENT_SYSTEM_MINIMIZESTART,
            PInvoke.EVENT_SYSTEM_MINIMIZEEND,
            default(HMODULE),
            _callback,
            0,
            0,
            flags);

        _foreground = PInvoke.SetWinEventHook(
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            default(HMODULE),
            _callback,
            0,
            0,
            flags);

        if (_objects.IsNull || _minimize.IsNull || _foreground.IsNull)
        {
            Release();
            throw new InvalidOperationException("Window events were not hooked.");
        }
    }

    public void Run()
    {
        Start();
        Pump(null);
    }

    internal void Pump(Func<MSG, bool>? intercept)
    {
        while (PInvoke.GetMessage(out var message, default, 0, 0) > 0)
        {
            if (intercept is not null && intercept(message))
                continue;

            PInvoke.TranslateMessage(in message);
            PInvoke.DispatchMessage(in message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_publish is not null && ReferenceEquals(Applied, _publish))
            Applied = null;
        if (ReferenceEquals(Session, _core))
            Session = null;

        Release();
        ClearDecorations();
    }

    public void Recover(RecoverySnapshot? snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (snapshot is null || snapshot.Clients.Count == 0)
            return;

        var kept = new List<RecoveredClient>(snapshot.Clients.Count);
        foreach (var client in snapshot.Clients)
        {
            if (!WindowIdentity.Matches(client))
                continue;

            if (client.Layered)
                Opacity.ReleaseAdded(client.Window);

            kept.Add(client);
        }

        if (kept.Count == 0)
            return;

        _core.Restore(new RecoverySnapshot(kept));
        foreach (var client in kept)
        {
            if (!client.Hidden)
                continue;

            var hwnd = new HWND((nint)client.Window.Value);
            if (PInvoke.IsWindow(hwnd))
                Reveal(client.Window);
        }

        foreach (var client in kept)
        {
            var hwnd = new HWND((nint)client.Window.Value);
            if (TryDescribe(hwnd, client.Window, out var appeared))
            {
                var outcome = _core.Handle(appeared);
                NoteManaged(client.Window, outcome, appeared);
                Apply(outcome);
            }
        }
    }

    public void RevealHidden()
    {
        var hidden = new List<WindowId>();
        foreach (var client in _core.Recovery().Clients)
        {
            if (client.Hidden)
                hidden.Add(client.Window);
        }

        lock (ShowGate)
        {
            Releasing = true;
            foreach (var window in hidden)
            {
                var hwnd = new HWND((nint)window.Value);
                if (PInvoke.IsWindow(hwnd))
                    ShowConcealed(hwnd);
            }
        }
    }

    internal void Publish()
    {
        if (Releasing || _changed is null)
            return;

        _changed(Opacity.Stamp(_core.Recovery()));
    }

    private void OnEvent(
        HWINEVENTHOOK hook,
        uint @event,
        HWND hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime)
    {
        if (idObject != 0 || idChild != 0 || hwnd.IsNull)
            return;

        if (IsShellTaskbar(hwnd))
        {
            Taskbar.Conceal();
            return;
        }

        try
        {
            var window = new WindowId(((IntPtr)hwnd).ToInt64());
            switch (@event)
            {
                case PInvoke.EVENT_SYSTEM_FOREGROUND:
                    Apply(_core.Handle(new WindowFocused(window, Foreground.Classify(window, dwmsEventTime))));
                    return;
                case PInvoke.EVENT_OBJECT_DESTROY:
                    ForgetLimits(window);
                    Apply(_core.Handle(new WindowClosed(window)));
                    return;
                case PInvoke.EVENT_OBJECT_LOCATIONCHANGE:
                    if (Volatile.Read(ref Applying) == 0)
                        RereadLimits(hwnd, window);
                    return;
                case PInvoke.EVENT_SYSTEM_MINIMIZESTART:
                    Apply(_core.Handle(new WindowMinimized(window)));
                    return;
                case PInvoke.EVENT_SYSTEM_MINIMIZEEND:
                    Apply(_core.Handle(new WindowRestored(window)));
                    return;
                case PInvoke.EVENT_OBJECT_HIDE:
                    if (PInvoke.IsIconic(hwnd))
                        Apply(_core.Handle(new WindowMinimized(window)));
                    return;
                case PInvoke.EVENT_OBJECT_CLOAKED:
                    ForgetLimits(window);
                    Apply(_core.Handle(new WindowClosed(window)));
                    return;
                case PInvoke.EVENT_OBJECT_SHOW:
                case PInvoke.EVENT_OBJECT_UNCLOAKED:
                case PInvoke.EVENT_OBJECT_NAMECHANGE:
                case PInvoke.EVENT_OBJECT_CREATE:
                    if (PInvoke.IsIconic(hwnd))
                        return;

                    Apply(_core.Handle(new WindowRestored(window)));
                    if (TryDescribe(hwnd, window, out var appeared))
                    {
                        var outcome = _core.Handle(appeared);
                        NoteManaged(window, outcome, appeared);
                        Apply(outcome);
                    }
                    return;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
    }

    private static bool TryDescribe(HWND hwnd, WindowId window, out WindowAppeared appeared)
    {
        appeared = null!;
        var monitor = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (monitor.IsNull || !PInvoke.GetMonitorInfo(monitor, ref info))
            return false;

        var style = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        var extended = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var className = ClassName(hwnd);
        var title = WindowTitle(hwnd);
        var identity = PackagedIdentity.Resolve(hwnd, className, ExeName(hwnd), title);
        var topLevel = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOT) == hwnd && (style & ChildStyle) == 0;
        Point? pointer = PInvoke.GetCursorPos(out var cursor) ? new Point(cursor.X, cursor.Y) : null;
        var bounds = PInvoke.GetWindowRect(hwnd, out var rect) ? From(rect) : default;
        var monitorRect = From(info.rcMonitor);
        long processId = 0;
        IReadOnlyList<int> ancestors = [];
        if (PInvoke.GetWindowThreadProcessId(hwnd, out var pid) != 0)
        {
            processId = pid;
            if (pid is > 0 and <= int.MaxValue)
                ancestors = ProcessLauncher.Ancestors((int)pid);
        }

        var dpi = (int)PInvoke.GetDpiForWindow(hwnd);
        var insets = InsetsFor(window, hwnd, dpi);
        Size? min = null;
        Size? max = null;
        if (WindowLimits.TryRead(hwnd, insets, out var readMin, out var readMax))
        {
            min = readMin;
            max = readMax;
        }

        appeared = new WindowAppeared(window, From(info.rcWork))
        {
            Pointer = pointer,
            Bounds = bounds,
            ProcessId = processId,
            AncestorProcessIds = ancestors,
            Traits = new WindowTraits(
                Visible: PInvoke.IsWindowVisible(hwnd) && !identity.UnresolvedFrame,
                Titled: title.Length > 0,
                TopLevel: topLevel,
                Tool: (extended & ToolStyle) != 0,
                Tooltip: className == "tooltips_class32",
                Taskbar: className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd",
                Cloaked: IsCloaked(hwnd),
                Dialog: className == "#32770" || (extended & DialogStyle) != 0,
                Elevated: false,
                Class: identity.Class,
                Title: title,
                Exe: identity.Exe,
                Aumid: identity.Aumid,
                MinSize: min,
                MaxSize: max,
                ExclusiveFullscreen: ExclusiveFullscreenWindow.Is((uint)style, bounds, monitorRect)),
        };
        return true;
    }

    private void NoteManaged(WindowId window, Outcome outcome, WindowAppeared appeared)
    {
        var adopted = false;
        foreach (var client in outcome.State.Clients)
        {
            if (client.Window != window)
                continue;

            adopted = true;
            break;
        }

        if (!adopted)
        {
            ForgetLimits(window);
            return;
        }

        var min = appeared.Traits.MinSize;
        var max = appeared.Traits.MaxSize;
        var changed = false;
        lock (_limitGate)
        {
            if (_limits.TryGetValue(window, out var prior))
                changed = prior.Min != min || prior.Max != max;

            _limits[window] = (min, max);
        }

        if (changed)
            Apply(_core.Handle(new WindowLimitsChanged(window, min, max)));
    }

    private void RereadLimits(HWND hwnd, WindowId window)
    {
        lock (_limitGate)
        {
            if (!_limits.ContainsKey(window))
                return;
        }

        if (!PInvoke.IsWindow(hwnd))
            return;

        var insets = InsetsFor(window, hwnd, (int)PInvoke.GetDpiForWindow(hwnd));
        if (!WindowLimits.TryRead(hwnd, insets, out var min, out var max))
            return;

        lock (_limitGate)
        {
            if (!_limits.TryGetValue(window, out var prior) || (prior.Min == min && prior.Max == max))
                return;

            _limits[window] = (min, max);
        }

        Apply(_core.Handle(new WindowLimitsChanged(window, min, max)));
    }

    private void RememberLimits(WindowId window, Size? min, Size? max)
    {
        lock (_limitGate)
            _limits[window] = (min, max);
    }

    private void ForgetLimits(WindowId window)
    {
        lock (_limitGate)
            _limits.Remove(window);
    }

    internal static void Apply(Outcome outcome)
    {
        Interlocked.Increment(ref Applying);
        try
        {
            ApplyCore(outcome);
        }
        finally
        {
            Interlocked.Decrement(ref Applying);
        }
    }

    private static void ApplyCore(Outcome outcome)
    {
        var windows = new Dictionary<ClientId, WindowId>(outcome.State.Clients.Count);
        foreach (var client in outcome.State.Clients)
            windows[client.Id] = client.Window;

        foreach (var window in outcome.Hide)
            Conceal(window);

        DropClosedFrames(outcome.State.Clients);
        var borderless = new HashSet<WindowId>();
        foreach (var client in outcome.State.Clients)
        {
            if (client.Fullscreen == FullscreenMode.Fullscreen)
                borderless.Add(client.Window);
        }

        foreach (var placement in outcome.Placements)
        {
            if (!windows.TryGetValue(placement.Client, out var window))
                continue;

            if (borderless.Contains(window))
            {
                Borderless.Apply(window, true);
                Move(new HWND((nint)window.Value), placement.Bounds);
            }
            else
            {
                Borderless.Apply(window, false);
                Place(window, placement.Bounds);
            }
        }

        foreach (var window in outcome.Show)
            Reveal(window);

        foreach (var window in outcome.ShowMinimized)
            Reveal(window);

        if (outcome.Focus is WindowId focus)
            _ = Foreground.MoveTo(focus);

        if (outcome.Launch is Launch launch)
        {
            var pid = ProcessLauncher.TryLaunch(launch);
            if (pid is int id)
                Session?.NoteLaunched(id, launch);
        }

        lock (PaintGate)
        {
            if (!Releasing)
            {
                foreach (var window in outcome.ResetDecorations)
                {
                    Borderless.Release(window);
                    ResetDecoration(window);
                    Painted.Remove(window);
                }

                foreach (var decoration in outcome.Decorations)
                {
                    if (!windows.TryGetValue(decoration.Client, out var window))
                        continue;

                    Paint(window, decoration);
                    Painted.Add(window);
                }
            }
        }

        SessionEffects.Apply(outcome);
        Applied?.Invoke(outcome);

        if (outcome.Kill is KillRequest kill)
            ProcessTerminator.TryTerminateOnSystem(kill.ProcessId);

        foreach (var power in outcome.DisplayPower)
            DisplayPowerAdapter.TryApplyToSystem(power);

        if (outcome.Exit)
            SessionHalt.Request();
    }

    // The Layout rectangle is the visible frame. The outer rectangle SetWindowPos moves
    // includes DWM's invisible border, which differs per window and per DPI. A window that
    // refuses the size is shifted once and then left at the size it kept.
    private static void Place(WindowId window, Rectangle target)
    {
        var hwnd = new HWND((nint)window.Value);
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

    private static void Move(HWND hwnd, Rectangle bounds) =>
        _ = PInvoke.SetWindowPos(
            hwnd,
            HWND.Null,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    private readonly record struct CachedFrame(int Dpi, FrameInsets Insets);

    internal void ResetDecorations() => ClearDecorations();

    private static void ClearDecorations()
    {
        WindowId[] painted;
        lock (PaintGate)
        {
            painted = [.. Painted];
            Painted.Clear();
        }

        foreach (var window in painted)
            ResetDecoration(window);
    }

    // DWMWA_BORDER_COLOR is a COLORREF, 0x00BBGGRR. The alpha byte is not used.
    // DWMWA_COLOR_DEFAULT and DWMWCP_DEFAULT put the frame back to the system choice.
    private static void Paint(WindowId window, Decoration decoration)
    {
        var hwnd = new HWND((nint)window.Value);
        var color = decoration.Borderless
            ? NoneBorderColor
            : (uint)(decoration.Border.R | (decoration.Border.G << 8) | (decoration.Border.B << 16));
        SetAttribute(hwnd, BorderColorAttribute, color);
        var corners = decoration.Corners == CornerPreference.Round ? RoundCorners : SquareCorners;
        SetAttribute(hwnd, CornerAttribute, corners);
        Opacity.Apply(window, decoration.Alpha);
    }

    private static void ResetDecoration(WindowId window)
    {
        var hwnd = new HWND((nint)window.Value);
        SetAttribute(hwnd, BorderColorAttribute, DefaultBorderColor);
        SetAttribute(hwnd, CornerAttribute, DefaultCorners);
        Opacity.Reset(window);
    }

    private static unsafe void SetAttribute(HWND hwnd, DWMWINDOWATTRIBUTE attribute, uint value) =>
        _ = PInvoke.DwmSetWindowAttribute(hwnd, attribute, &value, sizeof(uint));

    // SW_HIDE removes the window from the switcher: a hidden window is not visible,
    // and the switcher lists visible, uncloaked, ownerless top-level windows.
    // A minimized window stays visible, so it is hidden too. Showing it again uses
    // SW_SHOWMINNOACTIVE while it is iconic, which puts it back without restoring it.
    private static void Conceal(WindowId window)
    {
        lock (ShowGate)
        {
            if (Releasing)
                return;

            PInvoke.ShowWindow(new HWND((nint)window.Value), SHOW_WINDOW_CMD.SW_HIDE);
        }
    }

    private static void Reveal(WindowId window)
    {
        lock (ShowGate)
        {
            if (Releasing)
                return;

            ShowConcealed(new HWND((nint)window.Value));
        }
    }

    private static void ShowConcealed(HWND hwnd)
    {
        var command = PInvoke.IsIconic(hwnd)
            ? SHOW_WINDOW_CMD.SW_SHOWMINNOACTIVE
            : SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE;
        PInvoke.ShowWindow(hwnd, command);
    }

    internal static string WindowTitle(HWND hwnd)
    {
        var length = PInvoke.GetWindowTextLength(hwnd);
        if (length <= 0)
            return "";

        Span<char> buffer = length <= 512 ? stackalloc char[length + 1] : new char[length + 1];
        var written = PInvoke.GetWindowText(hwnd, buffer);
        return written <= 0 ? "" : new string(buffer[..written]);
    }

    private static string ExeName(HWND hwnd)
    {
        if (PInvoke.GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid == 0)
            return "";

        try
        {
            using var process = Process.GetProcessById((int)pid);
            var path = process.MainModule?.FileName;
            return string.IsNullOrEmpty(path) ? "" : Path.GetFileName(path);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return "";
        }
    }

    private static bool IsShellTaskbar(HWND hwnd)
    {
        var name = ClassName(hwnd);
        return name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    internal static string ClassName(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        var length = PInvoke.GetClassName(hwnd, buffer);
        return length <= 0 ? "" : new string(buffer[..length]);
    }

    private static bool IsCloaked(HWND hwnd)
    {
        Span<byte> value = stackalloc byte[sizeof(int)];
        var result = PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, value);
        return result.Succeeded && System.Runtime.InteropServices.MemoryMarshal.Read<int>(value) != 0;
    }

    private static Rectangle From(RECT rect) =>
        new(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);

    private void Release()
    {
        if (!_objects.IsNull)
            PInvoke.UnhookWinEvent(_objects);
        if (!_minimize.IsNull)
            PInvoke.UnhookWinEvent(_minimize);
        if (!_foreground.IsNull)
            PInvoke.UnhookWinEvent(_foreground);

        _objects = default;
        _minimize = default;
        _foreground = default;
    }
}
