using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

public interface ISystemInput
{
    bool TryReadKeyboardDelay(out int delay);

    void WriteKeyboardDelay(int delay);

    bool TryReadKeyboardSpeed(out int speed);

    void WriteKeyboardSpeed(int speed);

    bool TryReadNumLock(out bool enabled);

    void WriteNumLock(bool enabled);

    bool TryReadSwappedButtons(out bool swapped);

    void WriteSwappedButtons(bool swapped);

    bool TryReadMouseCurve(out MouseCurve curve);

    void WriteMouseCurve(MouseCurve curve);

    bool TryReadMouseSpeed(out int speed);

    void WriteMouseSpeed(int speed);

    bool TryReadWheelLines(out int lines);

    void WriteWheelLines(int lines);
}

/// <summary>
/// Applies an input request and remembers the values it replaced. A field that the request
/// leaves unset is not read or written. <see cref="Restore"/> puts the remembered values back.
/// </summary>
public sealed class SystemInputSession
{
    private readonly ISystemInput _input;
    private readonly Lock _gate = new();
    private InputOriginals _originals = new();
    private InputSettingsRequest? _applied;

    public SystemInputSession(ISystemInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _input = input;
    }

    public InputOriginals? Originals
    {
        get
        {
            lock (_gate)
                return _originals.Any ? _originals : null;
        }
    }

    public void Apply(InputSettingsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (request.Equals(_applied) && Covers(request))
                return;

            SyncKeyboardDelay(request.KeyboardDelay);
            SyncKeyboardSpeed(request.KeyboardSpeed);
            SyncNumLock(request.NumLock);
            SyncButtons(request.SwapButtons);
            SyncCurve(request.Acceleration);
            SyncMouseSpeed(request.MouseSpeed);
            SyncWheel(request.WheelLines);
            _applied = request;
        }
    }

    public void Restore()
    {
        lock (_gate)
            RestoreLocked();
    }

    public void Revert()
    {
        lock (_gate)
        {
            if (!_originals.Any && _applied is null)
                return;

            RestoreLocked();
        }
    }

    public void RestoreSaved(InputOriginals saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        lock (_gate)
        {
            Write(saved);
            _originals = new InputOriginals();
            _applied = null;
        }
    }

    private bool Covers(InputSettingsRequest request) =>
        (request.KeyboardDelay is null || _originals.KeyboardDelay is not null)
        && (request.KeyboardSpeed is null || _originals.KeyboardSpeed is not null)
        && (request.NumLock is null || _originals.NumLock is not null)
        && (request.SwapButtons is null || _originals.SwappedButtons is not null)
        && (request.Acceleration is null || _originals.MouseCurve is not null)
        && (request.MouseSpeed is null || _originals.MouseSpeed is not null)
        && (request.WheelLines is null || _originals.WheelLines is not null);

    private void RestoreLocked()
    {
        Write(_originals);
        _originals = new InputOriginals();
        _applied = null;
    }

    private void Write(InputOriginals saved)
    {
        if (saved.KeyboardDelay is int delay)
            _input.WriteKeyboardDelay(delay);
        if (saved.KeyboardSpeed is int speed)
            _input.WriteKeyboardSpeed(speed);
        if (saved.NumLock is bool numLock)
            _input.WriteNumLock(numLock);
        if (saved.SwappedButtons is bool swapped)
            _input.WriteSwappedButtons(swapped);
        if (saved.MouseCurve is MouseCurve curve)
            _input.WriteMouseCurve(curve);
        if (saved.MouseSpeed is int mouse)
            _input.WriteMouseSpeed(mouse);
        if (saved.WheelLines is int lines)
            _input.WriteWheelLines(lines);
    }

    private void SyncKeyboardDelay(int? desired)
    {
        if (desired is int delay)
        {
            if (_originals.KeyboardDelay is null)
            {
                if (!_input.TryReadKeyboardDelay(out var original))
                    return;

                _originals = _originals with { KeyboardDelay = original };
            }

            if (_applied?.KeyboardDelay != delay)
                _input.WriteKeyboardDelay(delay);
            return;
        }

        if (_originals.KeyboardDelay is int remembered)
        {
            _input.WriteKeyboardDelay(remembered);
            _originals = _originals with { KeyboardDelay = null };
        }
    }

    private void SyncKeyboardSpeed(int? desired)
    {
        if (desired is int speed)
        {
            if (_originals.KeyboardSpeed is null)
            {
                if (!_input.TryReadKeyboardSpeed(out var original))
                    return;

                _originals = _originals with { KeyboardSpeed = original };
            }

            if (_applied?.KeyboardSpeed != speed)
                _input.WriteKeyboardSpeed(speed);
            return;
        }

        if (_originals.KeyboardSpeed is int remembered)
        {
            _input.WriteKeyboardSpeed(remembered);
            _originals = _originals with { KeyboardSpeed = null };
        }
    }

    private void SyncNumLock(bool? desired)
    {
        if (desired is bool enabled)
        {
            if (_originals.NumLock is null)
            {
                if (!_input.TryReadNumLock(out var original))
                    return;

                _originals = _originals with { NumLock = original };
            }

            if (_applied?.NumLock != enabled)
                _input.WriteNumLock(enabled);
            return;
        }

        if (_originals.NumLock is bool remembered)
        {
            _input.WriteNumLock(remembered);
            _originals = _originals with { NumLock = null };
        }
    }

    private void SyncButtons(bool? desired)
    {
        if (desired is bool swapped)
        {
            if (_originals.SwappedButtons is null)
            {
                if (!_input.TryReadSwappedButtons(out var original))
                    return;

                _originals = _originals with { SwappedButtons = original };
            }

            if (_applied?.SwapButtons != swapped)
                _input.WriteSwappedButtons(swapped);
            return;
        }

        if (_originals.SwappedButtons is bool remembered)
        {
            _input.WriteSwappedButtons(remembered);
            _originals = _originals with { SwappedButtons = null };
        }
    }

    private void SyncCurve(PointerAcceleration? desired)
    {
        if (desired is PointerAcceleration acceleration)
        {
            var curve = acceleration == PointerAcceleration.Flat
                ? new MouseCurve(0, 0, 0)
                : new MouseCurve(6, 10, 1);
            if (_originals.MouseCurve is null)
            {
                if (!_input.TryReadMouseCurve(out var original))
                    return;

                _originals = _originals with { MouseCurve = original };
            }

            if (_applied?.Acceleration != acceleration)
                _input.WriteMouseCurve(curve);
            return;
        }

        if (_originals.MouseCurve is MouseCurve remembered)
        {
            _input.WriteMouseCurve(remembered);
            _originals = _originals with { MouseCurve = null };
        }
    }

    private void SyncMouseSpeed(int? desired)
    {
        if (desired is int speed)
        {
            if (_originals.MouseSpeed is null)
            {
                if (!_input.TryReadMouseSpeed(out var original))
                    return;

                _originals = _originals with { MouseSpeed = original };
            }

            if (_applied?.MouseSpeed != speed)
                _input.WriteMouseSpeed(speed);
            return;
        }

        if (_originals.MouseSpeed is int remembered)
        {
            _input.WriteMouseSpeed(remembered);
            _originals = _originals with { MouseSpeed = null };
        }
    }

    private void SyncWheel(int? desired)
    {
        if (desired is int lines)
        {
            if (_originals.WheelLines is null)
            {
                if (!_input.TryReadWheelLines(out var original))
                    return;

                _originals = _originals with { WheelLines = original };
            }

            if (_applied?.WheelLines != lines)
                _input.WriteWheelLines(lines);
            return;
        }

        if (_originals.WheelLines is int remembered)
        {
            _input.WriteWheelLines(remembered);
            _originals = _originals with { WheelLines = null };
        }
    }
}

/// <summary>
/// System keyboard, mouse and wheel settings. Tests do not construct this.
/// Num Lock is toggled with a key event only when the lamp is not already in the requested state.
/// </summary>
internal sealed class Win32SystemInput : ISystemInput
{
    private const byte NumLockKey = 0x90;
    private const byte NumLockScan = 0x45;

    private const SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS Update =
        SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_UPDATEINIFILE
        | SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_SENDCHANGE;

    public bool TryReadKeyboardDelay(out int delay) => TryRead(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETKEYBOARDDELAY, out delay);

    public void WriteKeyboardDelay(int delay) => Write(SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETKEYBOARDDELAY, (uint)delay);

    public bool TryReadKeyboardSpeed(out int speed) => TryRead(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETKEYBOARDSPEED, out speed);

    public void WriteKeyboardSpeed(int speed) => Write(SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETKEYBOARDSPEED, (uint)speed);

    public bool TryReadNumLock(out bool enabled)
    {
        enabled = (PInvoke.GetKeyState(NumLockKey) & 1) != 0;
        return true;
    }

    public void WriteNumLock(bool enabled)
    {
        if (!TryReadNumLock(out var current) || current == enabled)
            return;

        PInvoke.keybd_event(NumLockKey, NumLockScan, KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY, 0);
        PInvoke.keybd_event(
            NumLockKey,
            NumLockScan,
            KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY | KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP,
            0);
    }

    public bool TryReadSwappedButtons(out bool swapped)
    {
        swapped = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_SWAPBUTTON) != 0;
        return true;
    }

    public void WriteSwappedButtons(bool swapped) => _ = PInvoke.SwapMouseButton(swapped);

    public unsafe bool TryReadMouseCurve(out MouseCurve curve)
    {
        curve = default;
        Span<int> values = stackalloc int[3];
        fixed (int* pointer = values)
        {
            if (!PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETMOUSE, 0, pointer, 0))
                return false;
        }

        curve = new MouseCurve(values[0], values[1], values[2]);
        return true;
    }

    public unsafe void WriteMouseCurve(MouseCurve curve)
    {
        Span<int> values = stackalloc int[3];
        values[0] = curve.Threshold1;
        values[1] = curve.Threshold2;
        values[2] = curve.Acceleration;
        fixed (int* pointer = values)
            _ = PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETMOUSE, 0, pointer, Update);
    }

    public bool TryReadMouseSpeed(out int speed) => TryRead(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETMOUSESPEED, out speed);

    public void WriteMouseSpeed(int speed) => Write(SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETMOUSESPEED, (uint)speed);

    public bool TryReadWheelLines(out int lines) => TryRead(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETWHEELSCROLLLINES, out lines);

    public void WriteWheelLines(int lines) => Write(SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWHEELSCROLLLINES, (uint)lines);

    private static unsafe bool TryRead(SYSTEM_PARAMETERS_INFO_ACTION action, out int value)
    {
        var read = 0;
        var ok = PInvoke.SystemParametersInfo(action, 0, &read, 0);
        value = read;
        return ok;
    }

    private static unsafe void Write(SYSTEM_PARAMETERS_INFO_ACTION action, uint value) =>
        _ = PInvoke.SystemParametersInfo(action, value, null, Update);
}

/// <summary>Moves and hides the cursor. Tests do not construct this.</summary>
internal sealed class Win32Cursor : ICursorSurface
{
    private bool _hidden;

    public void Warp(Point position) => _ = PInvoke.SetCursorPos(position.X, position.Y);

    public void Hide()
    {
        if (_hidden)
            return;

        _hidden = true;
        _ = PInvoke.ShowCursor(false);
    }

    public void Show()
    {
        if (!_hidden)
            return;

        _hidden = false;
        _ = PInvoke.ShowCursor(true);
    }
}
