using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

public sealed partial class WindowAdapter : IDisposable
{
    private const int ChildStyle = 0x40000000;
    private const int ToolStyle = 0x00000080;
    private const int DialogStyle = 0x00000001;
    private const int LayeredStyle = 0x00080000;
    private const int ClickThroughStyle = 0x00000020;

    private static readonly Lock ShowGate = new();
    private static volatile bool Releasing;

    // The windows Winland hid, so an application hiding its own window can be told from it.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<WindowId, byte> Concealed = new();
    private static Action<Outcome>? Applied;
    private static Core? Session;

    private readonly Core _core;
    private readonly Action<RecoverySnapshot>? _changed;
    private readonly Action<Outcome>? _publish;
    private readonly WINEVENTPROC _callback;
    private const long ProbeMilliseconds = 2000;
    private const int ProbeLimit = 1024;

    private readonly Dictionary<WindowId, (Size? Min, Size? Max)> _limits = [];
    private readonly Dictionary<WindowId, (Size? Min, Size? Max, long At)> _probed = [];
    private readonly Lock _limitGate = new();
    private HWINEVENTHOOK _objects;
    private HWINEVENTHOOK _minimize;
    private HWINEVENTHOOK _foreground;
    private HWINEVENTHOOK _moveSize;
    private volatile RecoverySnapshot? _published;
    private bool _disposed;

    // Apply is static because the keyboard hook and the command server both call it.
    private static readonly HashSet<WindowId> Painted = [];
    private static readonly OpacityAdapter Opacity = new(new Win32WindowStyles());
    private static readonly BorderlessFrame Borderless = new(new Win32WindowFrame());
    private static readonly DwmEffects Effects = new(new Win32DwmSurface());
    private static readonly Lock PaintGate = new();
    private static readonly ApplyOrder Order = new();
    private static readonly Dictionary<WindowId, CachedFrame> Frames = [];
    private static readonly Lock FrameGate = new();

    // Where Winland last asked each window to be, and the frames sent without waiting.
    private static readonly QuickMove Quick = new();

    private static int Applying;

    /// <summary>Commits a display rotation. Tests replace it so no real display turns.</summary>
    internal static Func<DisplayOrientationRequest, bool> Rotate = DisplayOrientationAdapter.TryApplyToSystem;

    private const long SettleMilliseconds = 1500;

    // Windows whose maximize request was just answered, and when they stop being watched for drifting off their tile.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<WindowId, long> Settling = new();

    public WindowAdapter(Core core, Action<RecoverySnapshot>? changed = null)
    {
        ArgumentNullException.ThrowIfNull(core);
        _core = core;
        _core.ReducedMotion = ReducedMotion;
        Session = core;
        GroupBarHost.Dispatch = (name, arguments) =>
        {
            if (Session is Core session)
                Apply(session.Dispatch(name, arguments, allowsElevation: false));
        };
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

        _moveSize = PInvoke.SetWinEventHook(
            PInvoke.EVENT_SYSTEM_MOVESIZESTART,
            PInvoke.EVENT_SYSTEM_MOVESIZEEND,
            default(HMODULE),
            _callback,
            0,
            0,
            flags);

        if (_objects.IsNull || _minimize.IsNull || _foreground.IsNull || _moveSize.IsNull)
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
        LandMotions();
        ClearDecorations();

        // Nothing hooks window events any more, so a later adapter in this process starts unreleased.
        Releasing = false;
        Borderless.Reopen();
        Effects.Reopen();
        Opacity.Reopen();
    }

    public void Recover(RecoverySnapshot? snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (snapshot is null || snapshot.Clients.Count == 0)
            return;

        var kept = new List<RecoveredClient>(snapshot.Clients.Count);
        foreach (var client in snapshot.Clients)
        {
            var recognized = WindowIdentity.Recognize(client);
            if (recognized == WindowIdentity.Recognition.Other)
                continue;

            Repair(client);
            if (recognized != WindowIdentity.Recognition.SameClient)
                continue;

            // Nothing minimizes under Winland, so a window that is minimized now was minimized while it was away.
            // It does not rejoin, but one that was hidden is still shown again, minimized.
            if (PInvoke.IsIconic(new HWND((nint)client.Window.Value)))
            {
                if (client.Hidden)
                    Reveal(client.Window);

                continue;
            }

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

    // A previous run that died left its changes on the window. They are put back before anything reads the
    // window, so a stripped frame or a cut region is not taken as the window's own.
    private static void Repair(RecoveredClient client)
    {
        if (client.FrameStyle is uint style)
            Borderless.PutBack(client.Window, style);

        if (client.Region)
            Effects.DropLeftoverRegion(client.Window);

        if (client.Layered)
            Opacity.ReleaseAdded(client.Window);
    }

    public void RevealHidden()
    {
        LandMotions();
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

        var snapshot = Stamp(_core.Recovery());
        _published = snapshot;
        _changed(snapshot);
    }

    /// <summary>
    /// Shows every hidden Client and puts back every frame, effect and alpha, for a crash. It reads the last
    /// published snapshot instead of the core, so a thread that died holding the core lock cannot block it.
    /// </summary>
    internal void Salvage()
    {
        Releasing = true;
        if (_published is RecoverySnapshot snapshot)
        {
            foreach (var client in snapshot.Clients)
            {
                var hwnd = new HWND((nint)client.Window.Value);
                if (client.Hidden && PInvoke.IsWindow(hwnd))
                    ShowConcealed(hwnd);
            }
        }

        PutBackFrames();
    }

    // What this run changed on each Client's window, so a restart after a crash can put it back.
    private static RecoverySnapshot Stamp(RecoverySnapshot snapshot) =>
        Effects.Stamp(Borderless.Stamp(Opacity.Stamp(snapshot)));

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

        Notice(@event, hwnd, dwmsEventTime);
    }

    /// <summary>Handles one window event. The hook calls it; tests call it with the event they need.</summary>
    internal void Notice(uint @event, nint handle, uint time = 0) => Notice(@event, new HWND(handle), time);

    private void Notice(uint @event, HWND hwnd, uint dwmsEventTime)
    {
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
                    Concealed.TryRemove(window, out _);
                    Settling.TryRemove(window, out _);
                    Quick.Forget(window);
                    AbandonDrag(window);
                    ForgetLimits(window);
                    ForgetProbe(window);
                    Apply(_core.Handle(new WindowClosed(window)));
                    return;
                case PInvoke.EVENT_OBJECT_LOCATIONCHANGE:
                    if (Decide(hwnd, window))
                        return;

                    if (_sizing == window)
                    {
                        FollowSizing(window);
                        Effects.Refit(window);
                        return;
                    }

                    if (PInvoke.IsZoomed(hwnd) && _core.Manages(window))
                    {
                        RequestMaximize((nint)window.Value);
                        return;
                    }

                    if (Volatile.Read(ref Applying) == 0)
                    {
                        // A frame or a placement Winland sent arrives here too, often after Apply returned. Asking
                        // the window for its limits again would wait on a window that is busy taking it. A move the
                        // application made itself lands somewhere Winland did not put it, and is still read.
                        if (!(TryOuter(hwnd, out var now) && Quick.Ours(window, now)))
                            RereadLimits(hwnd, window);

                        ReportResize(hwnd, window);
                        PutBackIfDrifted(hwnd, window);
                    }

                    Effects.Refit(window);
                    return;
                case PInvoke.EVENT_SYSTEM_MOVESIZESTART:
                    BeginMove(hwnd, window);
                    return;
                case PInvoke.EVENT_SYSTEM_MOVESIZEEND:
                    EndMove(window);
                    return;
                case PInvoke.EVENT_SYSTEM_MINIMIZESTART:
                    UndoMinimize((nint)window.Value);
                    return;
                case PInvoke.EVENT_SYSTEM_MINIMIZEEND:
                    Apply(_core.Handle(new WindowRestored(window)));
                    AdoptShown(hwnd, window);
                    return;
                case PInvoke.EVENT_OBJECT_HIDE:
                    if (PInvoke.IsIconic(hwnd))
                    {
                        UndoMinimize((nint)window.Value);
                        return;
                    }

                    // An application that hides its own window unmaps it, as in the pinned release. It is
                    // adopted again when it is shown. A window shown again before this event arrives stays.
                    if (!Concealed.ContainsKey(window) && !PInvoke.IsWindowVisible(hwnd) && _core.Manages(window))
                    {
                        AbandonDrag(window);
                        ForgetLimits(window);
                        Apply(_core.Handle(new WindowClosed(window)));
                    }

                    return;
                case PInvoke.EVENT_OBJECT_CLOAKED:
                    // A virtual desktop switch cloaks hidden Clients too. A hidden window is not adopted again
                    // when it is uncloaked, so it stays a Client and keeps its Workspace.
                    if (!PInvoke.IsWindowVisible(hwnd) && _core.Manages(window))
                        return;

                    AbandonDrag(window);
                    ForgetLimits(window);
                    Apply(_core.Handle(new WindowClosed(window)));
                    return;
                case PInvoke.EVENT_OBJECT_SHOW:
                case PInvoke.EVENT_OBJECT_UNCLOAKED:
                case PInvoke.EVENT_OBJECT_NAMECHANGE:
                case PInvoke.EVENT_OBJECT_CREATE:
                    if (PInvoke.IsIconic(hwnd))
                        return;

                    // Titles change all the time. A Client's new title changes nothing the core keeps, and
                    // restoring a window the core does not manage does nothing, so neither is sent to it.
                    var managed = _core.Manages(window);
                    if (managed && @event == PInvoke.EVENT_OBJECT_NAMECHANGE)
                        return;

                    if (managed)
                        Apply(_core.Handle(new WindowRestored(window)));

                    AdoptShown(hwnd, window);
                    return;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
    }

    private bool TryDescribe(HWND hwnd, WindowId window, out WindowAppeared appeared)
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
        var owner = PInvoke.GetWindowThreadProcessId(hwnd, out var pid) == 0 ? 0 : pid;
        var identity = PackagedIdentity.Resolve(hwnd, className, ProcessFacts.Shared.For(owner).Exe);
        var topLevel = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOT) == hwnd && (style & ChildStyle) == 0;
        Point? pointer = PInvoke.GetCursorPos(out var cursor) ? new Point(cursor.X, cursor.Y) : null;
        var bounds = PInvoke.GetWindowRect(hwnd, out var rect) ? From(rect) : default;
        var monitorRect = From(info.rcMonitor);
        // A Store app's frame belongs to ApplicationFrameHost. The Client's process is the app it hosts.
        long processId = identity.ProcessId;
        var ancestors = ProcessFacts.Shared.For(identity.ProcessId).Ancestors;
        var (min, max) = LimitsOf(hwnd, window);

        var ownerWindow = PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER);
        appeared = new WindowAppeared(window, From(info.rcWork))
        {
            Pointer = pointer,
            Owner = ownerWindow.IsNull ? null : new WindowId(((IntPtr)ownerWindow).ToInt64()),
            Bounds = bounds,
            ProcessId = processId,
            AncestorProcessIds = ancestors,
            Traits = new WindowTraits(
                Visible: PInvoke.IsWindowVisible(hwnd) && !identity.UnresolvedFrame,
                Titled: title.Length > 0,
                TopLevel: topLevel,
                Tool: (extended & ToolStyle) != 0,
                Tooltip: className == "tooltips_class32",
                Taskbar: className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" || ShellFlyouts.Is(identity.Exe),
                Cloaked: IsCloaked(hwnd),
                Dialog: className == "#32770" || (extended & DialogStyle) != 0,
                Elevated: false,
                Class: identity.Class,
                Title: title,
                Exe: identity.Exe,
                Aumid: identity.Aumid,
                MinSize: min,
                MaxSize: max,
                ExclusiveFullscreen: ExclusiveFullscreenWindow.Is((uint)style, bounds, monitorRect),
                Overlay: IsOverlay(extended)),
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

    // WM_GETMINMAXINFO can take up to its timeout. A Client's limits are kept up to date as it moves, and a
    // window that is described again and again, as its title changes, is asked once in a while.
    private (Size? Min, Size? Max) LimitsOf(HWND hwnd, WindowId window)
    {
        var now = Environment.TickCount64;
        lock (_limitGate)
        {
            if (_limits.TryGetValue(window, out var kept))
                return kept;

            if (_probed.TryGetValue(window, out var probed) && now - probed.At < ProbeMilliseconds)
                return (probed.Min, probed.Max);
        }

        Size? min = null;
        Size? max = null;
        var insets = InsetsFor(window, hwnd, (int)PInvoke.GetDpiForWindow(hwnd));
        if (WindowLimits.TryRead(hwnd, insets, out var readMin, out var readMax))
        {
            min = readMin;
            max = readMax;
        }

        lock (_limitGate)
        {
            if (_probed.Count >= ProbeLimit)
                _probed.Clear();

            _probed[window] = (min, max, now);
        }

        return (min, max);
    }

    private void ForgetProbe(WindowId window)
    {
        lock (_limitGate)
            _probed.Remove(window);
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
        var admitted = Order.Admit(outcome);
        Interlocked.Increment(ref Applying);
        try
        {
            ApplyCore(admitted.Outcome, admitted.Current);
        }
        finally
        {
            Interlocked.Decrement(ref Applying);
        }
    }

    // An outcome that is not current lost its stale parts in Order.Admit. The state every outcome carries
    // whole, the group bars, the input settings and the cursor behavior, is left to the newer outcome that
    // was already applied. Its one-off requests still run.
    private static void ApplyCore(Outcome outcome, bool current)
    {
        var windows = new Dictionary<ClientId, WindowId>(outcome.State.Clients.Count);
        foreach (var client in outcome.State.Clients)
            windows[client.Id] = client.Window;

        // Once the exit cleanup began, nothing starts a motion that would change alpha or the border again.
        // The frame, effects and alpha refuse changes themselves once they were put back.
        var motions = Releasing ? [] : MotionsOf(outcome);
        foreach (var window in outcome.Hide)
            ConcealOrLeave(window, motions);

        if (current)
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

            var fills = borderless.Contains(window);
            Borderless.Apply(window, fills);
            if (outcome.Live)
                PlaceLive(window, placement.Bounds, fills, motions);
            else
                PlaceMoving(window, placement.Bounds, fills, motions);
        }

        foreach (var closing in outcome.Closes)
            CloseOrLeave(closing, motions);

        BeginRest(motions);

        foreach (var window in outcome.Show)
            Reveal(window);

        foreach (var window in outcome.ShowMinimized)
            Reveal(window);

        Restack(outcome.ZOrder);

        if (outcome.Focus is WindowId focus)
            _ = Foreground.MoveTo(focus);

        foreach (var launch in outcome.Launches)
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
                    DropMotion(window);
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

        if (!Releasing && current)
            GroupBarHost.Update(outcome.GroupBars);

        if (current)
            SessionEffects.Apply(outcome);
        else
            SessionEffects.ApplyOneOff(outcome);

        Applied?.Invoke(outcome);

        foreach (var kill in outcome.Kills)
            Kill(kill);

        foreach (var power in outcome.DisplayPower)
            DisplayPowerAdapter.TryApplyToSystem(power);

        // A rotation that matches the display commits nothing, so the display change it causes does not loop.
        foreach (var orientation in outcome.Orientations)
            Rotate(orientation);

        if (outcome.Exit)
            SessionHalt.Request();
    }

    // The owner is read from the window again, because the recorded process can be stale or the frame's host.
    private static void Kill(KillRequest kill)
    {
        long? live = null;
        if (kill.Window is WindowId window)
        {
            var hwnd = new HWND((nint)window.Value);
            if (PInvoke.IsWindow(hwnd))
                live = PackagedIdentity.OwnerProcess(hwnd);
        }

        if (ProcessTerminator.Target(kill.ProcessId, live, pid => pid is > 0 and <= uint.MaxValue ? PackagedIdentity.ExeFile((uint)pid) : "") is long target)
            ProcessTerminator.TryTerminateOnSystem(target);
        else
            Console.Error.WriteLine("The window's process was not ended: it could not be told apart from the frame host.");
    }

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

            // The hide this causes is reported like an application's own, so it is noted first.
            Concealed[window] = 0;
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
        Concealed.TryRemove(new WindowId(((IntPtr)hwnd).ToInt64()), out _);
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

    // A layered window input passes through, such as a screen overlay, cannot be clicked or focused, so
    // it is never a Client. The pinned release draws such surfaces as layers, which it never tiles.
    internal static bool IsOverlay(int extended) =>
        (extended & LayeredStyle) != 0 && (extended & ClickThroughStyle) != 0;

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

        if (!_moveSize.IsNull)
            PInvoke.UnhookWinEvent(_moveSize);

        _objects = default;
        _moveSize = default;
        _minimize = default;
        _foreground = default;
    }
}
