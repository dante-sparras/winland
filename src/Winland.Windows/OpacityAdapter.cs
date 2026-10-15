using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

internal readonly record struct WindowChrome(uint ExStyle, bool ColorKey, byte Alpha);

internal interface IWindowStyles
{
    bool TryRead(WindowId window, out WindowChrome chrome);

    bool TryWriteExStyle(WindowId window, uint exStyle);

    bool TrySetAlpha(WindowId window, byte alpha);
}

/// <summary>
/// Adds <c>WS_EX_LAYERED</c> only for windows that were not already layered, and puts that style back.
/// A window that refuses the layered attribute is skipped until it is reset.
/// </summary>
internal sealed class OpacityAdapter
{
    internal const uint LayeredStyle = 0x00080000;

    private readonly IWindowStyles _styles;
    private readonly Dictionary<WindowId, Original> _owned = [];
    private readonly HashSet<WindowId> _failed = [];
    private readonly HashSet<WindowId> _leftAlone = [];
    private readonly List<string> _reports = [];
    private readonly Lock _gate = new();

    internal OpacityAdapter(IWindowStyles styles)
    {
        ArgumentNullException.ThrowIfNull(styles);
        _styles = styles;
    }

    internal IReadOnlyList<string> Reports => _reports;

    internal bool Owns(WindowId window)
    {
        lock (_gate)
            return _owned.ContainsKey(window);
    }

    internal bool Failed(WindowId window)
    {
        lock (_gate)
            return _failed.Contains(window);
    }

    internal void Apply(WindowId window, byte alpha)
    {
        lock (_gate)
        {
            if (_failed.Contains(window) || _leftAlone.Contains(window))
                return;

            if (_owned.TryGetValue(window, out var owned))
            {
                if (alpha == 255)
                {
                    Restore(window, owned);
                    _owned.Remove(window);
                    return;
                }

                if (!_styles.TrySetAlpha(window, alpha))
                    Fail(window);

                return;
            }

            if (!_styles.TryRead(window, out var chrome))
            {
                Fail(window);
                return;
            }

            if ((chrome.ExStyle & LayeredStyle) != 0)
            {
                _leftAlone.Add(window);
                Report(window, "was already layered and was left alone.");
                return;
            }

            if (alpha == 255)
                return;

            var updated = chrome.ExStyle | LayeredStyle;
            _owned[window] = new Original(chrome.ExStyle, chrome.Alpha);
            if (!_styles.TryWriteExStyle(window, updated))
            {
                _owned.Remove(window);
                Fail(window);
                return;
            }

            if (!_styles.TrySetAlpha(window, alpha))
            {
                if (_styles.TryWriteExStyle(window, chrome.ExStyle))
                    _owned.Remove(window);

                Fail(window);
            }
        }
    }

    internal void Reset(WindowId window)
    {
        lock (_gate)
        {
            if (_owned.Remove(window, out var original))
                Restore(window, original);

            _failed.Remove(window);
            _leftAlone.Remove(window);
        }
    }

    internal void ResetAll()
    {
        WindowId[] windows;
        lock (_gate)
            windows = [.. _owned.Keys];

        foreach (var window in windows)
            Reset(window);
    }

    internal RecoverySnapshot Stamp(RecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            if (_owned.Count == 0)
                return snapshot;

            var clients = new RecoveredClient[snapshot.Clients.Count];
            var changed = false;
            for (var i = 0; i < snapshot.Clients.Count; i++)
            {
                var client = snapshot.Clients[i];
                var layered = _owned.ContainsKey(client.Window);
                clients[i] = client.Layered == layered ? client : client with { Layered = layered };
                changed |= client.Layered != layered;
            }

            return changed ? new RecoverySnapshot(clients) : snapshot;
        }
    }

    internal void ReleaseAdded(WindowId window)
    {
        lock (_gate)
        {
            _owned.Remove(window);
            _failed.Remove(window);
            _leftAlone.Remove(window);
        }

        if (!_styles.TryRead(window, out var chrome) || (chrome.ExStyle & LayeredStyle) == 0)
            return;

        _styles.TryWriteExStyle(window, chrome.ExStyle & ~LayeredStyle);
    }

    private void Restore(WindowId window, Original original)
    {
        _styles.TryWriteExStyle(window, original.ExStyle);
        if ((original.ExStyle & LayeredStyle) != 0)
            _styles.TrySetAlpha(window, original.Alpha);
    }

    private void Fail(WindowId window)
    {
        _failed.Add(window);
        Report(window, "could not be layered.");
    }

    private void Report(WindowId window, string reason)
    {
        var message = "Window 0x" + window.Value.ToString("x", CultureInfo.InvariantCulture) + " " + reason;
        _reports.Add(message);
        Console.Error.WriteLine(message);
    }

    private readonly record struct Original(uint ExStyle, byte Alpha);
}

internal sealed class Win32WindowStyles : IWindowStyles
{
    private const uint ColorKeyFlag = 0x1;

    public bool TryRead(WindowId window, out WindowChrome chrome)
    {
        chrome = default;
        var hwnd = new HWND((nint)window.Value);
        if (hwnd.IsNull || !PInvoke.IsWindow(hwnd))
            return false;

        var exStyle = (uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var colorKey = false;
        byte alpha = 255;
        if ((exStyle & OpacityAdapter.LayeredStyle) != 0)
            _ = TryReadLayered(hwnd, out colorKey, out alpha);

        chrome = new WindowChrome(exStyle, colorKey, alpha);
        return true;
    }

    public bool TryWriteExStyle(WindowId window, uint exStyle)
    {
        var hwnd = new HWND((nint)window.Value);
        if (hwnd.IsNull || !PInvoke.IsWindow(hwnd))
            return false;

        Marshal.SetLastPInvokeError(0);
        var previous = PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (int)exStyle);
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

    public bool TrySetAlpha(WindowId window, byte alpha)
    {
        var hwnd = new HWND((nint)window.Value);
        if (hwnd.IsNull || !PInvoke.IsWindow(hwnd))
            return false;

        return PInvoke.SetLayeredWindowAttributes(hwnd, default, alpha, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
    }

    private static unsafe bool TryReadLayered(HWND hwnd, out bool colorKey, out byte alpha)
    {
        colorKey = false;
        alpha = 255;
        Span<byte> value = stackalloc byte[1];
        if (!PInvoke.GetLayeredWindowAttributes(hwnd, out _, value, out var flags))
            return false;

        colorKey = ((uint)flags & ColorKeyFlag) != 0;
        alpha = value[0];
        return true;
    }
}
