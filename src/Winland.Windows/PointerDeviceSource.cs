using System.Buffers;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

/// <summary>
/// Names the pointer a move or a wheel came from. The low-level mouse hook does not say, and Windows posts that
/// pointer's raw input only after the hook returns, so a message-only window on the input thread takes mouse raw
/// input. Legacy mouse messages are left in place: suppressing them would stop every mouse, and the hook still sees
/// the move Windows has already accelerated. Names are read when a pointer arrives or leaves. A handle that is still
/// unknown leaves that packet with no device.
/// </summary>
internal sealed class PointerDeviceSource : IDisposable
{
    private const string ClassName = "WinlandPointerDevices";
    private const int ClassAlreadyExists = 1410;
    private const uint InputMessage = 0x00FF;
    private const uint DeviceChangeMessage = 0x00FE;
    private const uint MouseType = 0;
    private const uint AbsoluteMove = 0x01;
    private const ushort WheelFlag = 0x0400;
    private const ushort HorizontalWheelFlag = 0x0800;
    private const ushort GenericDesktopPage = 0x01;
    private const ushort MouseUsage = 0x02;
    private const uint ClassSize = 80;
    private const uint LargestBuffer = 65536;

    private static readonly WNDPROC Proc = OnWindow;
    private static PointerDeviceSource? Active;

    private readonly Action _requestRefresh;
    private readonly Action<int, int, long> _onMove;
    private readonly Action<int, bool, long> _onWheel;
    private readonly DeviceNameCache _names = new();
    private IInputDevices? _devices;
    private HWND _hwnd;
    private uint _thread;
    private int _refreshPosted;
    private bool _open;
    private bool _disposed;

    internal readonly record struct RawMotion(long Device, int Dx, int Dy);

    internal PointerDeviceSource(Action requestRefresh, Action<int, int, long> onMove, Action<int, bool, long> onWheel)
    {
        ArgumentNullException.ThrowIfNull(requestRefresh);
        ArgumentNullException.ThrowIfNull(onMove);
        ArgumentNullException.ThrowIfNull(onWheel);
        _requestRefresh = requestRefresh;
        _onMove = onMove;
        _onWheel = onWheel;
    }

    internal bool IsOpen => _open;

    /// <summary>Starts listening on the calling thread. A refused registration only logs, and moves are left as Windows sent them.</summary>
    internal void Start(IInputDevices? devices)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_open)
            return;

        _devices = devices;
        _thread = PInvoke.GetCurrentThreadId();
        try
        {
            Active = this;
            Register();
            _hwnd = Create();
            Listen(_hwnd);
            _open = true;
            Refresh();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _open = false;
            Console.Error.WriteLine(exception.Message);
        }
    }

    /// <summary>Stops listening. A later <see cref="Start"/> can listen again. Runs on the thread that started it.</summary>
    internal void Stop()
    {
        if (!_open)
            return;

        _open = false;
        if (ReferenceEquals(Active, this))
            Active = null;

        if (_thread == 0 || _thread != PInvoke.GetCurrentThreadId())
            return;

        Silence();
        if (!_hwnd.IsNull)
        {
            _ = PInvoke.DestroyWindow(_hwnd);
            _hwnd = default;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
    }

    internal bool TryName(long device, out string name) => _names.TryName(device, out name);

    internal void Refresh()
    {
        Interlocked.Exchange(ref _refreshPosted, 0);
        var devices = _devices;
        if (devices is null)
            return;

        try
        {
            _names.Replace(devices.Read(), InputDeviceKind.Pointer);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);
        }
    }

    /// <summary>A relative mouse move. An injected packet, a packet with no device, and an absolute pointer are not moves.</summary>
    internal static bool TryMove(uint flags, int dx, int dy, nuint extra, long device, out RawMotion motion)
    {
        motion = default;
        if (extra == KeyboardHook.InjectedTag || device == 0 || (flags & AbsoluteMove) != 0 || (dx == 0 && dy == 0))
            return false;

        motion = new RawMotion(device, dx, dy);
        return true;
    }

    /// <summary>The wheel delta in a raw packet, or null when that axis did not turn. The delta is the packet's own.</summary>
    internal static int? WheelDelta(ushort buttonFlags, ushort buttonData, nuint extra, long device, bool horizontal)
    {
        if (extra == KeyboardHook.InjectedTag || device == 0)
            return null;

        var flag = horizontal ? HorizontalWheelFlag : WheelFlag;
        if ((buttonFlags & flag) == 0)
            return null;

        return (short)buttonData;
    }

    internal void AskRefresh()
    {
        if (Interlocked.Exchange(ref _refreshPosted, 1) == 0)
            _requestRefresh();
    }

    private static LRESULT OnWindow(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        var active = Active;
        if (active is { _disposed: false, _open: true })
        {
            try
            {
                if (message == InputMessage)
                    active.Read(lParam);
                else if (message == DeviceChangeMessage)
                    active.AskRefresh();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Console.Error.WriteLine(exception.Message);
            }
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private unsafe void Read(LPARAM lParam)
    {
        var header = (uint)sizeof(RAWINPUTHEADER);
        uint size = 0;
        var handle = new HRAWINPUT((nint)lParam);
        if (PInvoke.GetRawInputData(handle, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, null, &size, header) != 0 || size < header || size > LargestBuffer)
            return;

        var rented = ArrayPool<byte>.Shared.Rent((int)size);
        try
        {
            fixed (byte* data = rented)
            {
                if (PInvoke.GetRawInputData(handle, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, data, &size, header) != size)
                    return;

                Report((RAWINPUT*)data);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private unsafe void Report(RAWINPUT* raw)
    {
        if ((uint)raw->header.dwType != MouseType)
            return;

        var mouse = raw->data.mouse;
        var device = (long)raw->header.hDevice.Value;
        var extra = (nuint)mouse.ulExtraInformation;
        if (TryMove((uint)mouse.usFlags, mouse.lLastX, mouse.lLastY, extra, device, out var motion))
            _onMove(motion.Dx, motion.Dy, motion.Device);

        var buttons = mouse.Anonymous.Anonymous;
        if (WheelDelta(buttons.usButtonFlags, buttons.usButtonData, extra, device, horizontal: false) is int vertical)
            _onWheel(vertical, false, device);
        if (WheelDelta(buttons.usButtonFlags, buttons.usButtonData, extra, device, horizontal: true) is int horizontal)
            _onWheel(horizontal, true, device);
    }

    private static unsafe void Listen(HWND hwnd)
    {
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = GenericDesktopPage,
            usUsage = MouseUsage,
            dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK | RAWINPUTDEVICE_FLAGS.RIDEV_DEVNOTIFY,
            hwndTarget = hwnd,
        };
        if (!PInvoke.RegisterRawInputDevices(&device, 1, (uint)sizeof(RAWINPUTDEVICE)))
            throw new InvalidOperationException($"Pointer devices were not registered ({Marshal.GetLastPInvokeError()}).");
    }

    private unsafe void Silence()
    {
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = GenericDesktopPage,
            usUsage = MouseUsage,
            dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_REMOVE,
        };
        _ = PInvoke.RegisterRawInputDevices(&device, 1, (uint)sizeof(RAWINPUTDEVICE));
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
                cbSize = ClassSize,
                lpfnWndProc = Proc,
                hInstance = instance,
                lpszClassName = name,
            };
            if (PInvoke.RegisterClassEx(in info) != 0)
                return;

            if (Marshal.GetLastPInvokeError() != ClassAlreadyExists)
                throw new InvalidOperationException($"The pointer device window class was not registered ({Marshal.GetLastPInvokeError()}).");
        }
    }

    private static unsafe HWND Create()
    {
        var hwnd = PInvoke.CreateWindowEx(0, ClassName, "Winland pointer devices", 0, 0, 0, 0, 0, HWND.HWND_MESSAGE, null, null, null);
        if (hwnd.IsNull)
            throw new InvalidOperationException($"The pointer device window was not created ({Marshal.GetLastPInvokeError()}).");

        return hwnd;
    }
}
