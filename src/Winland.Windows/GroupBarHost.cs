using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

/// <summary>
/// Draws the tab bar of each shown Group as a per-pixel-alpha layered window on its own thread.
/// A click on a tab focuses that member, a middle click closes it and the wheel steps through the Group.
/// The bars are owned by this process, so they go away with it; hidden members are restored by recovery.
/// </summary>
internal static class GroupBarHost
{
    private const string ClassName = "WinlandGroupBar";
    private const int ClassAlreadyExists = 1410;
    private const uint SyncMessage = 0x8000 + 0x47;
    private const uint StackMessage = 0x8000 + 0x48;
    private const uint QuitMessage = 0x0012;
    private const uint TimerMessage = 0x0113;
    private const uint MouseActivateMessage = 0x0021;
    private const uint LeftDownMessage = 0x0201;
    private const uint MiddleDownMessage = 0x0207;
    private const uint MiddleUpMessage = 0x0208;
    private const uint WheelMessage = 0x020A;
    private const int NoActivate = 3;
    private const int TopmostStyle = 0x00000008;
    private const uint TitlePeriodMilliseconds = 1000;

    private static readonly WNDPROC Proc = OnWindow;
    private static readonly Lock Gate = new();
    private static readonly Dictionary<WindowId, Bar> Bars = [];
    private static readonly Dictionary<HWND, Bar> ByHandle = [];

    // The shown windows that have a bar, read by any thread so a z-order change of another window costs one lookup.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<WindowId, byte> WithBars = new();
    private static IReadOnlyList<GroupBar> _pending = [];
    private static bool _posted;
    private static uint _thread;
    private static Thread? _worker;

    /// <summary>Runs a dispatcher for a click. The window adapter sets it.</summary>
    internal static Action<string, string>? Dispatch { get; set; }

    /// <summary>Tells the core the wheel turned over a bar: the Group's shown window, and whether towards the next member.</summary>
    internal static Action<WindowId, bool>? Scroll { get; set; }

    /// <summary>Replaces every bar with <paramref name="bars"/>. Starts the bar thread the first time a bar is needed.</summary>
    internal static void Update(IReadOnlyList<GroupBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        foreach (var shown in WithBars.Keys)
        {
            if (!bars.Any(bar => bar.Shown == shown))
                WithBars.TryRemove(shown, out _);
        }

        foreach (var bar in bars)
            WithBars[bar.Shown] = 0;

        uint thread;
        lock (Gate)
        {
            if (_worker is null && bars.Count == 0)
                return;

            _pending = bars;
            if (_worker is null)
                Start();

            if (_posted || _thread == 0)
                return;

            _posted = true;
            thread = _thread;
        }

        _ = PInvoke.PostThreadMessage(thread, SyncMessage, default, default);
    }

    /// <summary>
    /// The window was raised or shown, so the bar of the Group it shows goes just above it again, as Winland
    /// draws the bar with its window.
    /// </summary>
    internal static void Follow(WindowId window)
    {
        if (!WithBars.ContainsKey(window))
            return;

        uint thread;
        lock (Gate)
            thread = _thread;

        if (thread != 0)
            _ = PInvoke.PostThreadMessage(thread, StackMessage, new WPARAM((nuint)window.Value), default);
    }

    /// <summary>
    /// Some window changed its place in the z-order. Windows says which parent's children moved, not which window, so every
    /// bar goes just above its shown window again.
    /// </summary>
    internal static void Restack()
    {
        if (WithBars.IsEmpty)
            return;

        uint thread;
        lock (Gate)
            thread = _thread;

        if (thread != 0)
            _ = PInvoke.PostThreadMessage(thread, StackMessage, default, default);
    }

    /// <summary>Removes every bar and ends the bar thread.</summary>
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
            _pending = [];
            _posted = false;
        }

        if (worker is null)
            return;

        if (thread != 0)
            _ = PInvoke.PostThreadMessage(thread, QuitMessage, default, default);

        _ = worker.Join(TimeSpan.FromSeconds(2));
    }

    // The new thread syncs the pending bars itself once its queue exists, so nothing waits here.
    private static void Start()
    {
        _worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "Winland group bars",
        };
        _worker.Start();
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
        var timer = PInvoke.SetTimer(HWND.Null, 0, TitlePeriodMilliseconds, null);
        while (PInvoke.GetMessage(out var message, default, 0, 0) > 0)
        {
            try
            {
                if (message.hwnd.IsNull && message.message == SyncMessage)
                {
                    Sync();
                    continue;
                }

                if (message.hwnd.IsNull && message.message == StackMessage)
                {
                    if ((nuint)message.wParam == 0)
                    {
                        foreach (var each in Bars.Values)
                            each.Stack();
                    }
                    else if (Bars.TryGetValue(new WindowId((long)(nuint)message.wParam), out var raised))
                    {
                        raised.Stack();
                    }

                    continue;
                }

                if (message.hwnd.IsNull && message.message == TimerMessage)
                {
                    Refresh(force: false);
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

        if (timer != 0)
            _ = PInvoke.KillTimer(HWND.Null, timer);

        foreach (var bar in Bars.Values)
            bar.Dispose();

        Bars.Clear();
        ByHandle.Clear();
    }

    private static void Sync()
    {
        IReadOnlyList<GroupBar> wanted;
        lock (Gate)
        {
            wanted = _pending;
            _posted = false;
        }

        var keep = new HashSet<WindowId>(wanted.Count);
        foreach (var spec in wanted)
        {
            keep.Add(spec.Shown);
            if (!Bars.TryGetValue(spec.Shown, out var bar))
            {
                var hwnd = Create();
                if (hwnd.IsNull)
                    continue;

                bar = new Bar(hwnd, spec);
                Bars[spec.Shown] = bar;
                ByHandle[hwnd] = bar;
                bar.Render();
            }
            else if (!bar.Spec.SameAs(spec))
            {
                bar.Spec = spec;
                bar.Render();
            }

            bar.Stack();
        }

        foreach (var shown in Bars.Keys.ToArray())
        {
            if (keep.Contains(shown))
                continue;

            var bar = Bars[shown];
            Bars.Remove(shown);
            ByHandle.Remove(bar.Handle);
            bar.Dispose();
        }
    }

    // Titles are read live, so a title that changes after the window opened shows up within a second.
    private static void Refresh(bool force)
    {
        foreach (var bar in Bars.Values)
        {
            if (force || bar.TitlesChanged())
                bar.Render();
        }
    }

    private static LRESULT OnWindow(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == MouseActivateMessage)
            return new LRESULT(NoActivate);

        if (!ByHandle.TryGetValue(hwnd, out var bar))
            return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);

        var x = (short)((nint)lParam & 0xFFFF);
        var y = (short)(((nint)lParam >> 16) & 0xFFFF);
        switch (message)
        {
            case LeftDownMessage:
                var tab = bar.Spec.TabAt(x, y);
                Run("focuswindow", tab >= 0 ? bar.Spec.Tabs[tab].Window : bar.Spec.Shown);
                return default;
            case MiddleDownMessage:
                bar.Pressed = (x, y);
                return default;
            case MiddleUpMessage:
                var closed = bar.Spec.TabAt(x, y);
                if (bar.Spec.Style.MiddleClickClose && closed >= 0 && bar.Pressed == (x, y))
                    Run("closewindow", bar.Spec.Tabs[closed].Window);
                bar.Pressed = null;
                return default;
            case WheelMessage when bar.Spec.Style.Scrolling:
                var delta = (short)(((nuint)wParam >> 16) & 0xFFFF);
                try
                {
                    Scroll?.Invoke(bar.Spec.Shown, delta < 0);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    Console.Error.WriteLine(exception.Message);
                }

                return default;
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static int IndexOf(GroupBar spec, WindowId window)
    {
        for (var i = 0; i < spec.Tabs.Count; i++)
        {
            if (spec.Tabs[i].Window == window)
                return i;
        }

        return -1;
    }

    private static void Run(string dispatcher, WindowId window)
    {
        try
        {
            Dispatch?.Invoke(dispatcher, "address:" + CommandProtocol.Address(window));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);
        }
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
                throw new InvalidOperationException($"The group bar window class was not registered ({Marshal.GetLastPInvokeError()}).");
        }
    }

    private static unsafe HWND Create() =>
        PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE,
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

    private sealed class Bar(HWND handle, GroupBar spec) : IDisposable
    {
        private readonly BarBackdrop _backdrop = new(new Win32BackdropSurface(handle));
        private string[] _titles = [];

        public HWND Handle { get; } = handle;

        public GroupBar Spec { get; set; } = spec;

        public (int X, int Y)? Pressed { get; set; }

        public bool TitlesChanged() => !_titles.SequenceEqual(GroupBarPainter.Titles(Spec));

        public void Render()
        {
            _titles = GroupBarPainter.Titles(Spec);
            _backdrop.Set(Spec.Style.Blur);
            GroupBarPainter.Paint(Handle, Spec, _titles);
        }

        // Just above the shown window, so a floating window over the tile still covers the bar.
        public void Stack()
        {
            var shown = new HWND((nint)Spec.Shown.Value);
            var above = PInvoke.GetWindow(shown, GET_WINDOW_CMD.GW_HWNDPREV);
            var topmost = !above.IsNull && (PInvoke.GetWindowLong(above, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) & TopmostStyle) != 0;
            var flags = SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW;

            // A bar already just above the window stays where it is. Raising it to the top would put it over a floating window.
            if (above == Handle)
                flags |= SET_WINDOW_POS_FLAGS.SWP_NOZORDER;

            var after = above.IsNull || topmost ? new HWND(0) : above;
            _ = PInvoke.SetWindowPos(Handle, after, 0, 0, 0, 0, flags);
        }

        public void Dispose() => _ = PInvoke.DestroyWindow(Handle);
    }
}
