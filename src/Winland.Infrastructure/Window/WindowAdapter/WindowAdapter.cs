using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

public sealed partial class WindowAdapter : IDisposable
{
    private const int ChildStyle = 0x40000000;
    private const int CaptionStyle = 0x00C00000;
    private const int SizingStyle = 0x00040000;
    private const int ToolStyle = 0x00000080;
    private const int DialogStyle = 0x00000001;
    private const int LayeredStyle = 0x00080000;
    private const int ClickThroughStyle = 0x00000020;

    private static readonly Lock ShowGate = new();
    private static volatile bool Releasing;

    // The windows Winland hid, so an application hiding its own window can be told from it.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<WindowId, byte> Concealed = new();

    // The windows the core was last told are disabled.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<WindowId, byte> Disabled = new();
    private static Action<Outcome>? Applied;
    private static WindowManager? Session;

    private readonly WindowManager _core;
    private readonly Action<RecoverySnapshot>? _changed;
    private readonly Action<Outcome>? _publish;
    private readonly WINEVENTPROC _callback;

    // The object events OnEvent reads, as the fewest ranges that hold them. A range hooked wide delivers every event
    // inside it across processes, and browsers and editors raise focus, selection and value changes constantly.
    internal static readonly (uint First, uint Last)[] ObjectEvents =
    [
        (PInvoke.EVENT_OBJECT_CREATE, PInvoke.EVENT_OBJECT_REORDER),
        (PInvoke.EVENT_OBJECT_STATECHANGE, PInvoke.EVENT_OBJECT_NAMECHANGE),
        (PInvoke.EVENT_OBJECT_CLOAKED, PInvoke.EVENT_OBJECT_UNCLOAKED),
    ];

    private const long ProbeMilliseconds = 2000;
    private const int ProbeLimit = 1024;

    private readonly Dictionary<WindowId, (Size? Min, Size? Max)> _limits = [];
    private readonly Dictionary<WindowId, (Size? Min, Size? Max, long At)> _probed = [];
    private readonly Lock _limitGate = new();
    private HWINEVENTHOOK[] _objects = [];
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

    private const long SettleMilliseconds = 1500;

    // Windows whose maximize request was just answered, and when they stop being watched for drifting off their tile.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<WindowId, long> Settling = new();

    public WindowAdapter(WindowManager core, Action<RecoverySnapshot>? changed = null)
    {
        ArgumentNullException.ThrowIfNull(core);
        _core = core;
        _core.ReducedMotion = ReducedMotion;
        Session = core;
        GroupBarHost.Dispatch = (name, arguments) =>
        {
            if (Session is WindowManager session)
                Apply(session.Handle(new Dispatch(name, arguments)));
        };
        GroupBarHost.Close = window =>
        {
            if (Session is WindowManager session)
                Apply(session.Handle(new GroupTabClosed(window)));
        };
        GroupBarHost.Scroll = (shown, forward) =>
        {
            if (Session is WindowManager session)
                Apply(session.Handle(new GroupBarScrolled(shown, forward)));
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
        if (_objects.Length > 0)
            return;

        var flags = PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS;
        _objects = [.. ObjectEvents.Select(range => PInvoke.SetWinEventHook(
            range.First,
            range.Last,
            default(HMODULE),
            _callback,
            0,
            0,
            flags))];
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

        if (_objects.Any(hook => hook.IsNull) || _minimize.IsNull || _foreground.IsNull || _moveSize.IsNull)
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

    internal static void Pump(Func<MSG, bool>? intercept)
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

    private void OnEvent(
        HWINEVENTHOOK hook,
        uint @event,
        HWND hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime)
    {
        // A reorder names the parent whose children moved, so it is not a window event. Every tab bar is stacked again.
        if (@event == PInvoke.EVENT_OBJECT_REORDER)
        {
            GroupBarHost.Restack();
            return;
        }

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

            // A Client's border follows its window wherever it goes, and hides with it.
            if (@event is PInvoke.EVENT_OBJECT_LOCATIONCHANGE or PInvoke.EVENT_OBJECT_SHOW or PInvoke.EVENT_OBJECT_HIDE
                or PInvoke.EVENT_OBJECT_CLOAKED or PInvoke.EVENT_OBJECT_UNCLOAKED or PInvoke.EVENT_SYSTEM_MINIMIZESTART
                or PInvoke.EVENT_SYSTEM_MINIMIZEEND or PInvoke.EVENT_SYSTEM_FOREGROUND)
            {
                BorderHost.Follow(window);
            }

            // A Group's shown member raised or shown takes its tab bar up with it.
            if (@event is PInvoke.EVENT_SYSTEM_FOREGROUND or PInvoke.EVENT_OBJECT_SHOW)
                GroupBarHost.Follow(window);

            switch (@event)
            {
                case PInvoke.EVENT_SYSTEM_FOREGROUND:
                    var cause = Foreground.Classify(window, dwmsEventTime);
                    Apply(_core.Handle(new WindowFocused(window, cause) { Clicked = cause == FocusCause.User ? InputClock.Clicked : null }));
                    return;
                case PInvoke.EVENT_OBJECT_DESTROY:
                    Concealed.TryRemove(window, out _);
                    Disabled.TryRemove(window, out _);
                    Settling.TryRemove(window, out _);
                    Quick.Forget(window);
                    AbandonDrag(window);
                    ForgetLimits(window);
                    ForgetProbe(window);
                    Apply(_core.Handle(new WindowClosed(window)));
                    // A window held when it was created may never have been shown.
                    Opacity.Forget(window);
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
                case PInvoke.EVENT_OBJECT_STATECHANGE:
                    // Windows disables the owner of a modal dialog while it is open.
                    if (_core.Manages(window))
                        NoteEnabled(hwnd, window);

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
                case PInvoke.EVENT_OBJECT_CLOAKED:
                case PInvoke.EVENT_OBJECT_SHOW:
                case PInvoke.EVENT_OBJECT_UNCLOAKED:
                case PInvoke.EVENT_OBJECT_NAMECHANGE:
                case PInvoke.EVENT_OBJECT_CREATE:
                    var facts = new WindowFacts(
                        Minimized: PInvoke.IsIconic(hwnd),
                        Visible: PInvoke.IsWindowVisible(hwnd),
                        Managed: _core.Manages(window),
                        Concealed: Concealed.ContainsKey(window));
                    switch (WindowVisibility.Decide(@event, facts))
                    {
                        case VisibilityAction.UndoMinimize:
                            UndoMinimize((nint)window.Value);
                            break;
                        case VisibilityAction.Close:
                            AbandonDrag(window);
                            ForgetLimits(window);
                            Apply(_core.Handle(new WindowClosed(window)));
                            break;
                        case VisibilityAction.Retitle:
                            Apply(_core.Handle(new WindowTitleChanged(window, WindowTitle(hwnd))));
                            break;
                        case VisibilityAction.Restore:
                            Apply(_core.Handle(new WindowRestored(window)));
                            AdoptShown(hwnd, window);
                            break;
                        // Windows are created, renamed and shown hidden by the thousand, child windows among them. Only a
                        // shown top-level window can become a Client, and reading the others would hold up the next
                        // window that opens.
                        case VisibilityAction.Adopt when PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOT) != hwnd:
                            break;
                        // Windows draws a window the moment it is shown, before Winland hears of it. One created hidden is
                        // made transparent now, while nothing shows it, and is held until it is adopted when shown.
                        case VisibilityAction.Adopt when !facts.Visible:
                            if (@event == PInvoke.EVENT_OBJECT_CREATE && Holds(hwnd))
                                Opacity.Apply(window, 0);
                            break;
                        case VisibilityAction.Adopt:
                            if (@event is PInvoke.EVENT_OBJECT_SHOW or PInvoke.EVENT_OBJECT_UNCLOAKED or PInvoke.EVENT_OBJECT_CREATE)
                                AdoptHidden(hwnd, window);
                            else
                                AdoptShown(hwnd, window);
                            break;
                    }

                    return;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
    }

    // A window is on screen where it opened before Winland has read it, so for a frame or two it shows away from its
    // tile. One that looks like an application window is made transparent first. Once adopted, its opening motion or
    // its decoration gives it its alpha. One that neither touched, such as a window that was not adopted, is opaque again.
    private void AdoptHidden(HWND hwnd, WindowId window)
    {
        // A window held since it was created stays held, even if it no longer looks like one to hold.
        var held = Opacity.Wanted(window) == 0 || Holds(hwnd);
        if (held)
            Opacity.Apply(window, 0);

        try
        {
            AdoptShown(hwnd, window);
        }
        finally
        {
            if (held)
            {
                lock (Motions.For(window))
                {
                    if (!Motions.Running(window) && Opacity.Wanted(window) == 0)
                        Opacity.Apply(window, 255);
                }
            }
        }
    }

    // An application window has a title bar or a sizing border, and a title. A program that draws its own title bar,
    // as Windows Terminal does, keeps only the sizing border.
    private static bool Holds(HWND hwnd)
    {
        var style = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        var extended = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        return ((style & CaptionStyle) == CaptionStyle || (style & SizingStyle) != 0)
            && (style & ChildStyle) == 0
            && (extended & ToolStyle) == 0
            && PInvoke.GetWindowTextLength(hwnd) > 0;
    }

    // A window's state changes often, as it gains focus or is pressed, so only a change of enabled reaches the core.
    private void NoteEnabled(HWND hwnd, WindowId window)
    {
        var disabled = !PInvoke.IsWindowEnabled(hwnd);
        if (disabled ? !Disabled.TryAdd(window, 0) : !Disabled.TryRemove(window, out _))
            return;

        Apply(_core.Handle(new WindowEnabledChanged(window, !disabled)));
    }

    private static bool Note(WindowId window, bool disabled)
    {
        if (disabled)
            Disabled[window] = 0;
        else
            Disabled.TryRemove(window, out _);

        return disabled;
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
            Disabled = Note(window, !PInvoke.IsWindowEnabled(hwnd)),
            Bounds = bounds,
            ProcessId = processId,
            AncestorProcessIds = ancestors,
            Executable = PackagedIdentity.ImagePath(identity.ProcessId),
            Traits = new WindowTraits(
                Visible: PInvoke.IsWindowVisible(hwnd) && !identity.UnresolvedFrame,
                Titled: title.Length > 0,
                TopLevel: topLevel,
                Tool: (extended & ToolStyle) != 0,
                Tooltip: className == "tooltips_class32",
                Taskbar: className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" || ShellFlyouts.Is(identity.Exe, className),
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

    private void Release()
    {
        foreach (var hook in _objects)
        {
            if (!hook.IsNull)
                PInvoke.UnhookWinEvent(hook);
        }

        if (!_minimize.IsNull)
            PInvoke.UnhookWinEvent(_minimize);
        if (!_foreground.IsNull)
            PInvoke.UnhookWinEvent(_foreground);

        if (!_moveSize.IsNull)
            PInvoke.UnhookWinEvent(_moveSize);

        _objects = [];
        _moveSize = default;
        _minimize = default;
        _foreground = default;
    }
}
