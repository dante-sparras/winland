using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

internal interface IWindowFrame
{
    bool TryReadStyle(WindowId window, out uint style);

    bool TryWriteStyle(WindowId window, uint style);
}

/// <summary>
/// Mode 2 drops the window frame and puts that style back when the Client leaves fullscreen.
/// The placement is applied as the outer rectangle, so frame insets do not pull it off the monitor.
/// </summary>
internal static class BorderlessStyle
{
    internal const uint Popup = 0x80000000;
    internal const uint Caption = 0x00C00000;
    internal const uint ThickFrame = 0x00040000;
    internal const uint Border = 0x00800000;
    internal const uint DialogFrame = 0x00400000;
    internal const uint Frame = Caption | ThickFrame | Border | DialogFrame;

    internal static uint Strip(uint style) => (style & ~Frame) | Popup;

    // Strip changes only the frame and popup bits. The rest, such as whether the window is visible, disabled,
    // minimized or maximized, is the window's own and may have changed since.
    internal static uint Restore(uint current, uint original) => (current & ~(Frame | Popup)) | (original & (Frame | Popup));
}

internal sealed class BorderlessFrame
{
    private readonly IWindowFrame _frames;
    private readonly Func<WindowId, bool> _hung;
    private readonly Dictionary<WindowId, uint> _original = [];
    private readonly Lock _gate = new();
    private bool _closed;

    /// <param name="hung">Whether a window's program has stopped answering. Its frame is not dropped then, since the
    /// write would wait for it.</param>
    internal BorderlessFrame(IWindowFrame frames, Func<WindowId, bool>? hung = null)
    {
        ArgumentNullException.ThrowIfNull(frames);
        _frames = frames;
        _hung = hung ?? (static _ => false);
    }

    internal bool Owns(WindowId window)
    {
        lock (_gate)
            return _original.ContainsKey(window);
    }

    internal void Apply(WindowId window, bool borderless)
    {
        lock (_gate)
        {
            if (!borderless)
            {
                if (_original.Remove(window, out var original))
                    WriteBack(window, original);
                return;
            }

            if (_closed || _original.ContainsKey(window) || _hung(window))
                return;

            if (!_frames.TryReadStyle(window, out var style))
                return;

            var stripped = BorderlessStyle.Strip(style);
            _original[window] = style;
            if (stripped == style)
                return;

            if (!_frames.TryWriteStyle(window, stripped))
                _original.Remove(window);
        }
    }

    internal void Release(WindowId window) => Apply(window, false);

    /// <summary>Records the original style of each Client whose frame this dropped, so a crash does not leave it frameless.</summary>
    internal RecoverySnapshot Stamp(RecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            var clients = new RecoveredClient[snapshot.Clients.Count];
            var changed = false;
            for (var i = 0; i < snapshot.Clients.Count; i++)
            {
                var client = snapshot.Clients[i];
                uint? style = _original.TryGetValue(client.Window, out var original) ? original : null;
                clients[i] = client.FrameStyle == style ? client : client with { FrameStyle = style };
                changed |= client.FrameStyle != style;
            }

            return changed ? snapshot with { Clients = clients } : snapshot;
        }
    }

    /// <summary>Writes back the style a previous run recorded before it dropped the frame and then died.</summary>
    internal void PutBack(WindowId window, uint style)
    {
        lock (_gate)
        {
            _original.Remove(window);
            WriteBack(window, style);
        }
    }

    private void WriteBack(WindowId window, uint original)
    {
        if (_frames.TryReadStyle(window, out var current))
            _frames.TryWriteStyle(window, BorderlessStyle.Restore(current, original));
    }

    /// <summary>
    /// Puts back the frame of every window this dropped it from, for the exit cleanup. No frame is dropped
    /// after that: an outcome that arrives late would leave it dropped for good. A window whose program stopped
    /// answering would hold up the exit, so it keeps the dropped frame, and <see cref="Stamp"/> still records it for the
    /// next start.
    /// </summary>
    internal void ReleaseAll()
    {
        WindowId[] windows;
        lock (_gate)
        {
            _closed = true;
            windows = [.. _original.Keys];
        }

        foreach (var window in windows)
        {
            if (_hung(window))
                Console.Error.WriteLine($"Window 0x{window.Value:x} is not answering. Its frame is put back at the next start.");
            else
                Release(window);
        }
    }
}

// Stryker disable once all : reads and writes a real window's style.
internal sealed class Win32WindowFrame : IWindowFrame
{
    public bool TryReadStyle(WindowId window, out uint style)
    {
        style = 0;
        var hwnd = new HWND((nint)window.Value);
        if (hwnd.IsNull || !PInvoke.IsWindow(hwnd))
            return false;

        style = (uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        return true;
    }

    public bool TryWriteStyle(WindowId window, uint style)
    {
        var hwnd = new HWND((nint)window.Value);
        if (hwnd.IsNull || !PInvoke.IsWindow(hwnd))
            return false;

        Marshal.SetLastPInvokeError(0);
        var previous = PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE, (int)style);
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0)
            return false;

        _ = PInvoke.SetWindowPos(
            hwnd,
            HWND.Null,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE
            | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
            | SET_WINDOW_POS_FLAGS.SWP_NOZORDER
            | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
            | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
        return true;
    }
}
