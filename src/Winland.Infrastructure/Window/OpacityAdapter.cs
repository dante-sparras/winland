using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

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
    private readonly Func<WindowId, bool> _hung;
    private readonly Dictionary<WindowId, Original> _owned = [];
    private readonly HashSet<WindowId> _failed = [];
    private readonly HashSet<WindowId> _leftAlone = [];
    private readonly Dictionary<WindowId, byte> _alphas = [];
    private readonly Dictionary<WindowId, byte> _veiled = [];
    private readonly Lock _gate = new();
    private bool _closed;

    /// <param name="hung">Whether a window's program has stopped answering. A style write waits for the window, so
    /// one that would block is put off until the program answers again.</param>
    internal OpacityAdapter(IWindowStyles styles, Func<WindowId, bool>? hung = null)
    {
        ArgumentNullException.ThrowIfNull(styles);
        _styles = styles;
        _hung = hung ?? (static _ => false);
    }

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
            if (_veiled.ContainsKey(window))
            {
                _veiled[window] = alpha;
                return;
            }

            Set(window, alpha);
        }
    }

    /// <summary>The alpha the window was last given, which a veiled window shows once it is unveiled.</summary>
    internal byte Wanted(WindowId window)
    {
        lock (_gate)
            return _veiled.TryGetValue(window, out var alpha) ? alpha : _alphas.GetValueOrDefault(window, (byte)255);
    }

    /// <summary>
    /// Makes the window fully transparent until <see cref="Unveil"/>. An alpha applied meanwhile is kept for then.
    /// Returns false when the window cannot be made transparent, and it is left as it was.
    /// </summary>
    internal bool Veil(WindowId window)
    {
        lock (_gate)
        {
            if (_veiled.ContainsKey(window))
                return true;

            var alpha = _alphas.GetValueOrDefault(window, (byte)255);
            if (_closed || !Set(window, 0) || !_owned.ContainsKey(window))
            {
                Set(window, alpha);
                return false;
            }

            _veiled[window] = alpha;
            return true;
        }
    }

    /// <summary>Gives a veiled window back the alpha it was last asked for.</summary>
    internal void Unveil(WindowId window)
    {
        lock (_gate)
        {
            if (_veiled.Remove(window, out var alpha))
                Set(window, alpha);
        }
    }

    // Returns false when the window could not be given the alpha.
    private bool Set(WindowId window, byte alpha)
    {
        _alphas[window] = alpha;
        if (_failed.Contains(window) || _leftAlone.Contains(window))
            return false;

        if (_owned.TryGetValue(window, out var owned))
        {
            // A hung window keeps its layered style at full alpha, which needs no answer from it.
            if (alpha == 255 && _hung(window))
                return _styles.TrySetAlpha(window, alpha);

            if (alpha == 255)
            {
                Restore(window, owned);
                _owned.Remove(window);
                return true;
            }

            if (_styles.TrySetAlpha(window, alpha))
                return true;

            Fail(window);
            return false;
        }

        if (_closed)
            return alpha == 255;

        if (!_styles.TryRead(window, out var chrome))
        {
            Fail(window);
            return false;
        }

        if ((chrome.ExStyle & LayeredStyle) != 0)
        {
            _leftAlone.Add(window);
            Report(window, "was already layered and was left alone.");
            return false;
        }

        if (alpha == 255)
            return true;

        if (_hung(window))
            return false;

        var updated = chrome.ExStyle | LayeredStyle;
        _owned[window] = new Original(chrome.ExStyle, chrome.Alpha);
        if (!_styles.TryWriteExStyle(window, updated))
        {
            _owned.Remove(window);
            Fail(window);
            return false;
        }

        if (_styles.TrySetAlpha(window, alpha))
            return true;

        if (_styles.TryWriteExStyle(window, chrome.ExStyle))
            _owned.Remove(window);

        Fail(window);
        return false;
    }

    /// <summary>
    /// Gives the window back the style and alpha it had. One whose program stopped answering would hold this up, so it
    /// is only made opaque, which does not wait, and stays owned: the exit's snapshot records it for the next start.
    /// </summary>
    internal void Reset(WindowId window)
    {
        lock (_gate)
        {
            if (_owned.TryGetValue(window, out var original) && _hung(window))
            {
                _styles.TrySetAlpha(window, 255);
                Report(window, "is not answering. It stays layered until the next start.");
            }
            else if (_owned.Remove(window))
            {
                Restore(window, original);
            }

            _failed.Remove(window);
            _leftAlone.Remove(window);
            _alphas.Remove(window);
            _veiled.Remove(window);
        }
    }

    /// <summary>Forgets a window that is gone, without touching it.</summary>
    internal void Forget(WindowId window)
    {
        lock (_gate)
        {
            _owned.Remove(window);
            _failed.Remove(window);
            _leftAlone.Remove(window);
            _alphas.Remove(window);
            _veiled.Remove(window);
        }
    }

    /// <summary>
    /// Restores every window this made layered, for the exit cleanup. No window is made layered after that:
    /// an outcome or a motion that arrives late would leave it so for good.
    /// </summary>
    internal void ResetAll()
    {
        WindowId[] windows;
        lock (_gate)
        {
            _closed = true;
            windows = [.. _owned.Keys];
        }

        foreach (var window in windows)
            Reset(window);
    }

    /// <summary>
    /// Marks each Client this made layered, and records the other windows it made layered, the ones held transparent
    /// while they were created hidden, so a crash does not leave them so.
    /// </summary>
    internal RecoverySnapshot Stamp(RecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            if (_owned.Count == 0)
                return snapshot;

            var clients = new RecoveredClient[snapshot.Clients.Count];
            var changed = false;
            var others = new HashSet<WindowId>(_owned.Keys);
            for (var i = 0; i < snapshot.Clients.Count; i++)
            {
                var client = snapshot.Clients[i];
                var layered = _owned.ContainsKey(client.Window);
                clients[i] = client.Layered == layered ? client : client with { Layered = layered };
                changed |= client.Layered != layered;
                others.Remove(client.Window);
            }

            if (others.Count > 0)
                return snapshot with { Clients = clients, Held = [.. others.Select(window => new HeldWindow(window, "", 0))] };

            return changed ? snapshot with { Clients = clients } : snapshot;
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

    // Only the layered style is Winland's. The rest is read again, so a style the program set meanwhile, such as always
    // on top, stays.
    private void Restore(WindowId window, Original original)
    {
        if (_styles.TryRead(window, out var now))
            _styles.TryWriteExStyle(window, (now.ExStyle & ~LayeredStyle) | (original.ExStyle & LayeredStyle));
        if ((original.ExStyle & LayeredStyle) != 0)
            _styles.TrySetAlpha(window, original.Alpha);
    }

    private void Fail(WindowId window)
    {
        _failed.Add(window);
        Report(window, "could not be layered.");
    }

    private static void Report(WindowId window, string reason)
    {
        var message = "Window 0x" + window.Value.ToString("x", CultureInfo.InvariantCulture) + " " + reason;

        Console.Error.WriteLine(message);
    }

    private readonly record struct Original(uint ExStyle, byte Alpha);
}

// Stryker disable once all : each member reads or writes a real window's extended style or alpha.
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

        // Becoming layered needs no new frame: the alpha set next is what shows. Asking the window to answer a frame
        // change there would redraw it once more, before it has an alpha. Leaving the layered style needs the repaint.
        if ((exStyle & ~(uint)previous) == OpacityAdapter.LayeredStyle)
            return true;

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
