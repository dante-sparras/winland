using System.Buffers;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

/// <summary>
/// Names the keyboard a key came from. The low-level hook does not say, and Windows posts that key's raw input only
/// after the hook returns, so a message-only window on the input thread takes keyboard raw input and reports each
/// packet once the hook has returned. Names are read when a keyboard arrives or leaves, and when a handle is not in
/// the cache, never while the hook is deciding. A handle that is still unknown leaves that key with no device.
/// </summary>
internal sealed class KeyDeviceSource : IDisposable
{
    private const string ClassName = "WinlandKeyDevices";
    private const int ClassAlreadyExists = 1410;
    private const uint InputMessage = 0x00FF;
    private const uint DeviceChangeMessage = 0x00FE;
    private const uint KeyboardType = 1;
    private const ushort GenericDesktopPage = 0x01;
    private const ushort KeyboardUsage = 0x06;
    private const ushort BreakFlag = 0x01;
    private const uint ClassSize = 80;
    private const uint LargestBuffer = 65536;

    private static readonly WNDPROC Proc = OnWindow;
    private static KeyDeviceSource? Active;

    private readonly Action _requestRefresh;
    private readonly Action<int, int, bool, long> _onKey;
    private readonly DeviceNameCache _names = new();
    private IInputDevices? _devices;
    private HWND _hwnd;
    private uint _thread;
    private int _refreshPosted;
    private bool _open;
    private bool _disposed;

    internal readonly record struct RawKey(long Device, int VirtualKey, int Scan, bool Up);

    internal KeyDeviceSource(Action requestRefresh, Action<int, int, bool, long> onKey)
    {
        ArgumentNullException.ThrowIfNull(requestRefresh);
        ArgumentNullException.ThrowIfNull(onKey);
        _requestRefresh = requestRefresh;
        _onKey = onKey;
    }

    /// <summary>Starts listening on the calling thread. A refused registration only logs, and the hook then has no devices.</summary>
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _open = false;
        if (ReferenceEquals(Active, this))
            Active = null;

        // DestroyWindow has to run on the thread that created the window. Anywhere else the window stays until exit.
        if (_thread == 0 || _thread != PInvoke.GetCurrentThreadId())
            return;

        Silence();
        if (!_hwnd.IsNull)
        {
            _ = PInvoke.DestroyWindow(_hwnd);
            _hwnd = default;
        }
    }

    /// <summary>The <c>devices</c> name of this keyboard, when the last read listed it.</summary>
    internal bool TryName(long device, out string name) => _names.TryName(device, out name);

    /// <summary>Reads the device list again. Runs off the hook, on the input thread.</summary>
    internal void Refresh()
    {
        Interlocked.Exchange(ref _refreshPosted, 0);
        var devices = _devices;
        if (devices is null)
            return;

        try
        {
            _names.Replace(devices.Read());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);
        }
    }

    /// <summary>A raw keyboard packet. An injected key and a packet with no device are not reported.</summary>
    internal static bool TryKey(int virtualKey, int scan, ushort flags, nuint extra, long device, out RawKey key)
    {
        key = default;
        if (extra == KeyboardHook.InjectedTag || device == 0)
            return false;

        key = new RawKey(device, virtualKey, scan, (flags & BreakFlag) != 0);
        return true;
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
        if ((uint)raw->header.dwType != KeyboardType)
            return;

        var key = raw->data.keyboard;
        var device = (long)raw->header.hDevice.Value;
        if (!TryKey(key.VKey, key.MakeCode, (ushort)key.Flags, (nuint)key.ExtraInformation, device, out var packet))
            return;

        _onKey(packet.VirtualKey, packet.Scan, packet.Up, packet.Device);
    }

    private static unsafe void Listen(HWND hwnd)
    {
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = GenericDesktopPage,
            usUsage = KeyboardUsage,
            dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK | RAWINPUTDEVICE_FLAGS.RIDEV_DEVNOTIFY,
            hwndTarget = hwnd,
        };
        if (!PInvoke.RegisterRawInputDevices(&device, 1, (uint)sizeof(RAWINPUTDEVICE)))
            throw new InvalidOperationException($"Keyboard devices were not registered ({Marshal.GetLastPInvokeError()}).");
    }

    private unsafe void Silence()
    {
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = GenericDesktopPage,
            usUsage = KeyboardUsage,
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
                throw new InvalidOperationException($"The key device window class was not registered ({Marshal.GetLastPInvokeError()}).");
        }
    }

    private static unsafe HWND Create()
    {
        var hwnd = PInvoke.CreateWindowEx(0, ClassName, "Winland key devices", 0, 0, 0, 0, 0, HWND.HWND_MESSAGE, null, null, null);
        if (hwnd.IsNull)
            throw new InvalidOperationException($"The key device window was not created ({Marshal.GetLastPInvokeError()}).");

        return hwnd;
    }
}
