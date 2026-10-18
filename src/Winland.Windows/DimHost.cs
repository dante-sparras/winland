using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

/// <summary>
/// Draws the dim_around layers as black, click-through layered windows on their own thread, each kept just below the
/// window it belongs to. The layers go away with this process.
/// </summary>
internal static class DimHost
{
    private const string ClassName = "WinlandDimAround";
    private const int ClassAlreadyExists = 1410;
    private const uint SyncMessage = 0x8000 + 0x49;
    private const uint QuitMessage = 0x0012;
    private const uint MouseActivateMessage = 0x0021;
    private const int NoActivate = 3;

    private static readonly WNDPROC Proc = OnWindow;
    private static readonly Lock Gate = new();
    private static readonly Dictionary<WindowId, (HWND Handle, DimAround Spec)> Layers = [];
    private static IReadOnlyList<DimAround> _pending = [];
    private static bool _posted;
    private static uint _thread;
    private static Thread? _worker;

    /// <summary>Replaces every layer with <paramref name="dims"/>. Starts the thread the first time a layer is needed.</summary>
    internal static void Update(IReadOnlyList<DimAround> dims)
    {
        ArgumentNullException.ThrowIfNull(dims);
        uint thread;
        lock (Gate)
        {
            if (_worker is null && dims.Count == 0)
                return;

            _pending = dims;
            if (_worker is null)
            {
                _worker = new Thread(Run) { IsBackground = true, Name = "Winland dim around" };
                _worker.Start();
            }

            if (_posted || _thread == 0)
                return;

            _posted = true;
            thread = _thread;
        }

        _ = PInvoke.PostThreadMessage(thread, SyncMessage, default, default);
    }

    /// <summary>Removes every layer and ends the thread.</summary>
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

        foreach (var layer in Layers.Values)
            _ = PInvoke.DestroyWindow(layer.Handle);

        Layers.Clear();
    }

    private static void Sync()
    {
        IReadOnlyList<DimAround> wanted;
        lock (Gate)
        {
            wanted = _pending;
            _posted = false;
        }

        var keep = new HashSet<WindowId>(wanted.Count);
        foreach (var dim in wanted)
        {
            keep.Add(dim.Window);
            if (!Layers.TryGetValue(dim.Window, out var layer))
            {
                var hwnd = Create();
                if (hwnd.IsNull)
                    continue;

                layer = (hwnd, dim);
                Shade(hwnd, dim.Alpha);
            }
            else if (layer.Spec.Alpha != dim.Alpha)
            {
                Shade(layer.Handle, dim.Alpha);
            }

            Layers[dim.Window] = (layer.Handle, dim);
            Stack(layer.Handle, dim);
        }

        foreach (var window in Layers.Keys.ToArray())
        {
            if (keep.Contains(window))
                continue;

            _ = PInvoke.DestroyWindow(Layers[window].Handle);
            Layers.Remove(window);
        }
    }

    private static void Shade(HWND hwnd, double alpha) =>
        _ = PInvoke.SetLayeredWindowAttributes(hwnd, new COLORREF(0), (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);

    // Just below the window, over the whole Monitor. A layer already there stays where it is.
    private static void Stack(HWND layer, DimAround dim)
    {
        var target = new HWND((nint)dim.Window.Value);
        var flags = SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW;
        if (PInvoke.GetWindow(target, GET_WINDOW_CMD.GW_HWNDNEXT) == layer)
            flags |= SET_WINDOW_POS_FLAGS.SWP_NOZORDER;

        var area = dim.Monitor;
        _ = PInvoke.SetWindowPos(layer, target, area.X, area.Y, area.Width, area.Height, flags);
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
                hbrBackground = new HBRUSH((nint)PInvoke.GetStockObject(GET_STOCK_OBJECT_FLAGS.BLACK_BRUSH).Value),
                lpszClassName = name,
            };
            if (PInvoke.RegisterClassEx(in info) != 0)
                return;

            if (Marshal.GetLastPInvokeError() != ClassAlreadyExists)
                throw new InvalidOperationException($"The dim window class was not registered ({Marshal.GetLastPInvokeError()}).");
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
}
