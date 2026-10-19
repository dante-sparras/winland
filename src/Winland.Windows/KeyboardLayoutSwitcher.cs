using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winland.Windows;

/// <summary>What the switcher needs from Windows. Tests pass a stand-in.</summary>
public interface IKeyboardLayouts
{
    IReadOnlyList<nint> Loaded();

    /// <summary>The layout the foreground window uses.</summary>
    nint Current();

    nint Load(string id);

    void Unload(nint layout);

    void Activate(nint layout);
}

/// <summary>
/// Loads the layouts <c>input:kb_layout</c> names and switches to the one asked for. A layout Winland loaded that was
/// not loaded before is unloaded when the config no longer names it, and on exit. The layouts that were loaded already
/// stay.
/// </summary>
public sealed class KeyboardLayoutSwitcher(IKeyboardLayouts system)
{
    private readonly HashSet<nint> _ours = [];
    private readonly Dictionary<string, nint> _ids = new(StringComparer.OrdinalIgnoreCase);
    private KeyboardLayoutRequest? _applied;
    private nint _first;

    /// <summary>The first layout the config names, once loaded, or 0. The keyboard hook reads it to name keys for binds.</summary>
    public nint First => Volatile.Read(ref _first);
    private HashSet<nint>? _before;
    private nint? _beforeActive;

    public void Apply(KeyboardLayoutRequest? request)
    {
        if (request == _applied)
            return;

        if (request is null)
        {
            Restore();
            return;
        }

        _before ??= [.. system.Loaded()];
        _beforeActive ??= system.Current();
        var wanted = new List<nint>();
        foreach (var id in request.IdList)
        {
            // Each load counts, so a layout is loaded once and its handle kept.
            if (id.Length > 0 && !_ids.ContainsKey(id))
                _ids[id] = system.Load(id);

            var layout = id.Length == 0 ? 0 : _ids[id];
            wanted.Add(layout);
            if (layout != 0 && !_before.Contains(layout))
                _ours.Add(layout);
        }

        foreach (var layout in _ours.ToArray())
        {
            if (wanted.Contains(layout))
                continue;

            system.Unload(layout);
            _ours.Remove(layout);
            foreach (var (id, handle) in _ids.ToArray())
            {
                if (handle == layout)
                    _ids.Remove(id);
            }
        }

        Volatile.Write(ref _first, wanted.Count > 0 ? wanted[0] : 0);
        if (request.Active < wanted.Count && wanted[request.Active] != 0)
            system.Activate(wanted[request.Active]);

        _applied = request;
    }

    /// <summary>Switches back to the layout used before, and unloads the layouts Winland loaded.</summary>
    public void Restore()
    {
        if (_beforeActive is nint active && active != 0)
            system.Activate(active);

        foreach (var layout in _ours)
        {
            if (layout != _beforeActive)
                system.Unload(layout);
        }

        _ours.Clear();
        _ids.Clear();
        Volatile.Write(ref _first, 0);
        _applied = null;
        _before = null;
        _beforeActive = null;
    }
}

/// <summary>The real keyboard layouts. Tests do not construct this.</summary>
// Stryker disable once all : loads and switches the real keyboard layouts.
internal sealed class Win32KeyboardLayouts : IKeyboardLayouts
{
    private const uint InputLanguageChangeRequest = 0x0050;

    public unsafe IReadOnlyList<nint> Loaded()
    {
        var count = PInvoke.GetKeyboardLayoutList(0, null);
        if (count <= 0)
            return [];

        var layouts = new HKL[count];
        fixed (HKL* buffer = layouts)
            count = PInvoke.GetKeyboardLayoutList(count, buffer);

        return [.. layouts.Take(Math.Max(0, count)).Select(layout => (nint)layout.Value)];
    }

    public unsafe nint Current()
    {
        var foreground = PInvoke.GetForegroundWindow();
        var thread = foreground.IsNull ? 0 : PInvoke.GetWindowThreadProcessId(foreground, null);
        return (nint)PInvoke.GetKeyboardLayout(thread).Value;
    }

    public nint Load(string id)
    {
        // The handle is kept loaded: the switcher unloads it itself, so the safe handle must not.
        var handle = PInvoke.LoadKeyboardLayout(id, 0);
        var layout = handle.DangerousGetHandle();
        handle.SetHandleAsInvalid();
        if (layout == 0)
            Console.Error.WriteLine($"The keyboard layout {id} was not loaded ({Marshal.GetLastPInvokeError()}).");

        return layout;
    }

    public void Unload(nint layout) => _ = PInvoke.UnloadKeyboardLayout(new HKL(layout));

    // Windows switches the input language of the window in the foreground, and with it the one the user types in.
    public void Activate(nint layout)
    {
        var foreground = PInvoke.GetForegroundWindow();
        if (!foreground.IsNull)
            _ = PInvoke.PostMessage(foreground, InputLanguageChangeRequest, default, new LPARAM(layout));
    }
}
