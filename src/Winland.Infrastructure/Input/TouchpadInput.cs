using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Devices.HumanInterfaceDevice;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

/// <summary>
/// Reads precision touchpad reports as raw input, in the background, on a message-only window. The window proc
/// only parses and queues; the bind worker hands each swipe to the core. Raw input cannot be consumed, so
/// Windows still acts on its own touchpad gestures. The fingers of each report are also noted for the mouse hook, so a
/// touchpad move is not turned with the mouse.
/// </summary>
internal sealed class TouchpadInput : IDisposable
{
    private const string ClassName = "WinlandTouchpad";
    private const int ClassAlreadyExists = 1410;
    private const uint InputMessage = 0x00FF;
    private const uint DeviceChangeMessage = 0x00FE;
    private const nuint DeviceArrived = 1;
    private const nuint DeviceRemoved = 2;

    // A pad that could not be read is read again at most this often, not on every report.
    private const long RetryMs = 1000;
    private const ushort DigitizerPage = 0x0D;
    private const ushort TouchPadUsage = 0x05;
    private const ushort TipSwitchUsage = 0x42;
    private const ushort ContactIdUsage = 0x51;
    private const ushort ContactCountUsage = 0x54;
    private const ushort GenericDesktopPage = 0x01;
    private const ushort XUsage = 0x30;
    private const ushort YUsage = 0x31;
    private const int HidSuccess = 0x00110000;
    private const uint HidType = 2;
    private const uint ClassSize = 80;

    private static readonly WNDPROC Proc = OnWindow;
    private static TouchpadInput? Active;

    private readonly Action<Input> _post;
    private readonly TouchpadSwipe _swipe = new();
    private readonly DeviceCache<Pad> _pads = new(device => Pad.Open(new HANDLE(device)), () => Environment.TickCount64, RetryMs);
    private HWND _hwnd;
    private bool _disposed;

    internal TouchpadInput(Action<Input> post)
    {
        ArgumentNullException.ThrowIfNull(post);
        _post = post;
        _swipe.Framed = OnFrame;
    }

    /// <summary>Starts listening. A machine without a touchpad, or a refused registration, only logs.</summary>
    internal void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_hwnd.IsNull)
            return;

        try
        {
            Active = this;
            Register();
            _hwnd = Create();
            Listen(_hwnd);
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (ReferenceEquals(Active, this))
            Active = null;

        if (!_hwnd.IsNull)
        {
            _ = PInvoke.DestroyWindow(_hwnd);
            _hwnd = default;
        }

        _pads.Dispose();
    }

    private static LRESULT OnWindow(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        var active = Active;
        if (message == InputMessage && active is { _disposed: false })
        {
            try
            {
                active.Read(lParam);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Console.Error.WriteLine(exception.Message);
            }
        }
        else if (message == DeviceChangeMessage && active is { _disposed: false } && wParam.Value is DeviceArrived or DeviceRemoved)
        {
            // A handle can be handed to the next device, so what was read for it no longer holds.
            active._pads.Remove((nint)lParam);
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private unsafe void Read(LPARAM lParam)
    {
        var header = (uint)sizeof(RAWINPUTHEADER);
        uint size = 0;
        var handle = new HRAWINPUT((nint)lParam);
        if (PInvoke.GetRawInputData(handle, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, null, &size, header) != 0 || size <= header)
            return;

        var buffer = new byte[size];
        fixed (byte* data = buffer)
        {
            if (PInvoke.GetRawInputData(handle, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, data, &size, header) != size)
                return;

            var raw = (RAWINPUTHEADER*)data;
            if (raw->dwType != HidType)
                return;

            if (_pads.For(raw->hDevice) is not { } pad)
                return;

            var hid = (RAWHID*)(data + header);
            var each = hid->dwSizeHid;
            var reports = data + header + (2 * sizeof(uint));
            var end = data + size;
            for (var i = 0u; i < hid->dwCount; i++)
            {
                var report = reports + (i * each);
                if (report + each > end)
                    break;

                Parse(pad, report, each);
            }
        }
    }

    private unsafe void Parse(Pad pad, byte* report, uint length)
    {
        var count = pad.Value(DigitizerPage, pad.CountLink, ContactCountUsage, report, length) ?? 0;
        var contacts = new List<TouchContact>(pad.Fingers.Count);
        foreach (var link in pad.Fingers)
        {
            if (pad.Value(GenericDesktopPage, link, XUsage, report, length) is not uint x
                || pad.Value(GenericDesktopPage, link, YUsage, report, length) is not uint y)
                continue;

            var id = pad.Value(DigitizerPage, link, ContactIdUsage, report, length) ?? link;
            contacts.Add(new TouchContact((int)id, pad.Tip(link, report, length), x * pad.MillimetresX, y * pad.MillimetresY));
        }

        foreach (var input in _swipe.Report((int)Math.Min(count, int.MaxValue), contacts))
            _post(input);
    }

    private void OnFrame(IReadOnlyDictionary<int, (double X, double Y)> down) =>
        TouchpadFingers.Note(down.Count, Environment.TickCount64);

    private static unsafe void Listen(HWND hwnd)
    {
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = DigitizerPage,
            usUsage = TouchPadUsage,
            dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK | RAWINPUTDEVICE_FLAGS.RIDEV_DEVNOTIFY,
            hwndTarget = hwnd,
        };
        if (!PInvoke.RegisterRawInputDevices(&device, 1, (uint)sizeof(RAWINPUTDEVICE)))
            throw new InvalidOperationException($"Touchpad input was not registered ({Marshal.GetLastPInvokeError()}).");
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
                throw new InvalidOperationException($"The touchpad window class was not registered ({Marshal.GetLastPInvokeError()}).");
        }
    }

    private static unsafe HWND Create()
    {
        var hwnd = PInvoke.CreateWindowEx(
            0,
            ClassName,
            "Winland touchpad",
            0,
            0,
            0,
            0,
            0,
            HWND.HWND_MESSAGE,
            null,
            null,
            null);
        if (hwnd.IsNull)
            throw new InvalidOperationException($"The touchpad window was not created ({Marshal.GetLastPInvokeError()}).");

        return hwnd;
    }

    /// <summary>The parsing layout of one precision touchpad. A device that is not one has no pad.</summary>
    private sealed unsafe class Pad : IDisposable
    {
        private readonly nint _preparsed;
        private readonly ushort[] _usages;

        private Pad(nint preparsed, ushort countLink, List<ushort> fingers, (double X, double Y) millimetres, int buttons)
        {
            _preparsed = preparsed;
            CountLink = countLink;
            Fingers = fingers;
            MillimetresX = millimetres.X;
            MillimetresY = millimetres.Y;
            _usages = new ushort[Math.Max(1, buttons)];
        }

        internal ushort CountLink { get; }

        internal List<ushort> Fingers { get; }

        internal double MillimetresX { get; }

        internal double MillimetresY { get; }

        internal static Pad? Open(HANDLE device)
        {
            uint size = 0;
            if (PInvoke.GetRawInputDeviceInfo(device, RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_PREPARSEDDATA, null, &size) != 0 || size == 0)
                return null;

            var preparsed = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PInvoke.GetRawInputDeviceInfo(device, RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_PREPARSEDDATA, (void*)preparsed, &size) != size)
                    return Free(preparsed);

                var data = new PHIDP_PREPARSED_DATA(preparsed);
                HIDP_CAPS caps;
                if (PInvoke.HidP_GetCaps(data, &caps).Value != HidSuccess || caps.NumberInputValueCaps == 0)
                    return Free(preparsed);

                var values = new HIDP_VALUE_CAPS[caps.NumberInputValueCaps];
                var length = caps.NumberInputValueCaps;
                fixed (HIDP_VALUE_CAPS* first = values)
                {
                    if (PInvoke.HidP_GetValueCaps(HIDP_REPORT_TYPE.HidP_Input, first, &length, data).Value != HidSuccess)
                        return Free(preparsed);
                }

                ushort? countLink = null;
                var fingers = new List<ushort>();
                double? millimetresX = null;
                double? millimetresY = null;
                for (var i = 0; i < length; i++)
                {
                    var value = values[i];
                    var usage = value.IsRange ? value.Anonymous.Range.UsageMin : value.Anonymous.NotRange.Usage;
                    if (value.UsagePage == DigitizerPage && usage == ContactCountUsage)
                        countLink ??= value.LinkCollection;
                    else if (value.UsagePage == GenericDesktopPage && usage == XUsage)
                    {
                        if (!fingers.Contains(value.LinkCollection))
                            fingers.Add(value.LinkCollection);
                        millimetresX ??= Millimetres(value);
                    }
                    else if (value.UsagePage == GenericDesktopPage && usage == YUsage)
                    {
                        millimetresY ??= Millimetres(value);
                    }
                }

                if (countLink is not ushort link || fingers.Count == 0 || millimetresX is not double x || millimetresY is not double y)
                    return Free(preparsed);

                return new Pad(preparsed, link, fingers, (x, y), caps.NumberInputButtonCaps * 8);
            }
            catch
            {
                Marshal.FreeHGlobal(preparsed);
                throw;
            }
        }

        internal uint? Value(ushort page, ushort link, ushort usage, byte* report, uint length)
        {
            uint value;
            var status = PInvoke.HidP_GetUsageValue(
                HIDP_REPORT_TYPE.HidP_Input,
                page,
                link,
                usage,
                &value,
                new PHIDP_PREPARSED_DATA(_preparsed),
                new PSTR(report),
                length);
            return status.Value == HidSuccess ? value : null;
        }

        internal bool Tip(ushort link, byte* report, uint length)
        {
            fixed (ushort* usages = _usages)
            {
                var count = (uint)_usages.Length;
                var status = PInvoke.HidP_GetUsages(
                    HIDP_REPORT_TYPE.HidP_Input,
                    DigitizerPage,
                    link,
                    usages,
                    &count,
                    new PHIDP_PREPARSED_DATA(_preparsed),
                    new PSTR(report),
                    length);
                if (status.Value != HidSuccess)
                    return false;

                for (var i = 0; i < count; i++)
                {
                    if (usages[i] == TipSwitchUsage)
                        return true;
                }

                return false;
            }
        }

        public void Dispose() => Marshal.FreeHGlobal(_preparsed);

        private static Pad? Free(nint preparsed)
        {
            Marshal.FreeHGlobal(preparsed);
            return null;
        }

        // Millimetres per logical unit, from the physical range and its unit. A pad that reports no physical
        // size is taken as 100 millimetres across.
        private static double Millimetres(HIDP_VALUE_CAPS value)
        {
            var logical = (double)value.LogicalMax - value.LogicalMin;
            if (logical <= 0)
                return 0;

            var physical = (double)value.PhysicalMax - value.PhysicalMin;
            var system = value.Units & 0xF;
            var unit = system switch
            {
                1 => 10d,
                3 => 25.4,
                _ => 0d,
            };
            if (physical <= 0 || unit == 0 || ((value.Units >> 4) & 0xF) != 1)
                return 100 / logical;

            var exponent = (int)(value.UnitsExp & 0xF);
            if (exponent > 7)
                exponent -= 16;

            return physical * unit * Math.Pow(10, exponent) / logical;
        }
    }
}
