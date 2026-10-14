using System.Runtime.InteropServices;
using Microsoft.Win32;
using Winland.Core.Diagnostics;
using Winland.Core.Tiling;

namespace Winland.Platform.Win32;

/// <summary>
/// The strip along the top of each group's tile. A tool window, under every other window, that never takes focus.
/// A click shows that member. Winland draws these. The shell does not.
/// </summary>
public sealed class GroupStripHost
{
    private const int ShowNoActivate = 4;
    private const int GwlpUserData = -21;
    private const uint WmDestroy = 0x0002;
    private const uint WmErase = 0x0014;
    private const uint WmPaint = 0x000F;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const int MaNoActivate = 3;
    private const uint DtCenter = 0x0001;
    private const uint DtVCenter = 0x0004;
    private const uint DtSingleLine = 0x0020;
    private const uint DtNoPrefix = 0x0800;
    private const uint DtEndEllipsis = 0x8000;
    private const int Transparent = 1;
    private const string ClassName = "Winland.GroupStrip";
    private static readonly nint Bottom = new(1);
    private static readonly Native.WndProc Proc = Dispatch;
    private static bool _registered;

    private readonly Action<long> _show;
    private readonly List<Slot> _slots = [];
    private int _closed;
    private bool _light;
    private int _fontHeight;
    private nint _font;
    private nint _background;
    private nint _tab;
    private nint _active;
    private uint _text;
    private uint _muted;
    private nint _accent;

    /// <summary>Keeps <paramref name="show"/> for a click on a tab.</summary>
    /// <param name="show">Shows the window whose identity was clicked.</param>
    public GroupStripHost(Action<long> show)
    {
        ArgumentNullException.ThrowIfNull(show);
        _show = show;
    }

    /// <summary>The strip height Winland keeps free, in physical pixels, at the system DPI.</summary>
    /// <returns>At least one pixel.</returns>
    public static int StripPixels() => GroupStripMetrics.Pixels(Native.GetDpiForSystem());

    /// <summary>Shows a strip for each of <paramref name="strips"/> and hides the rest. Call it on the thread that pumps messages.</summary>
    /// <param name="strips">The groups on the workspaces in front.</param>
    /// <param name="title">Reads a window's title.</param>
    public void Sync(IReadOnlyList<GroupStrip> strips, Func<long, string> title)
    {
        ArgumentNullException.ThrowIfNull(strips);
        ArgumentNullException.ThrowIfNull(title);
        if (Volatile.Read(ref _closed) != 0 || !EnsureClass())
        {
            return;
        }

        var light = LightTheme();
        for (var i = 0; i < strips.Count; i++)
        {
            if (i == _slots.Count)
            {
                _slots.Add(new Slot(this));
            }

            var strip = strips[i];
            var titles = new string[strip.Members.Count];
            for (var member = 0; member < titles.Length; member++)
            {
                var name = title(strip.Members[member]);
                titles[member] = name.Length == 0 ? "Window " + (member + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : name;
            }

            _slots[i].Show(strip, titles, light);
        }

        for (var i = strips.Count; i < _slots.Count; i++)
        {
            _slots[i].Leave();
        }

        var now = Environment.TickCount64;
        while (_slots.Count > strips.Count && GroupStripSpares.Closes(_slots.Count, strips.Count, now - _slots[^1].ShownAt))
        {
            var surplus = _slots[^1];
            _slots.RemoveAt(_slots.Count - 1);
            surplus.Destroy();
        }
    }

    /// <summary>Destroys every strip. The window manager is leaving.</summary>
    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        foreach (var slot in _slots)
        {
            slot.Destroy();
        }

        _slots.Clear();
        FreeDraw();
    }

    private bool EnsureClass()
    {
        if (_registered)
        {
            return true;
        }

        ShellWindows.Install();
        var instance = Native.GetModuleHandle(null);
        var cls = new Native.WndClass
        {
            Style = 0x0003,
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
            HInstance = instance,
            LpszClassName = ClassName,
        };
        if (Native.RegisterClass(ref cls) == 0 && Marshal.GetLastWin32Error() != Native.ErrorClassAlreadyExists)
        {
            Log.Warning("Winland", "the group strip class was not registered");
            return false;
        }

        _registered = true;
        return true;
    }

    private void ShowSlot(Slot slot, GroupStrip strip, string[] titles, bool light)
    {
        var face = strip.Active.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + string.Join('\n', titles);
        var moved = !slot.Shown || slot.Rect != strip.Rect;
        var changed = face != slot.Face || light != _light;
        slot.Face = face;
        slot.Members = strip.Members as long[] ?? [.. strip.Members];
        slot.Active = strip.Active;
        slot.Rect = strip.Rect;
        slot.ShownAt = Environment.TickCount64;
        if (slot.Hwnd == 0)
        {
            slot.Hwnd = Open(slot, strip.Rect);
            if (slot.Hwnd == 0)
            {
                return;
            }
        }

        Dress(slot.Hwnd, light, strip.Rect.Height);
        if (moved)
        {
            var width = Math.Max(1, strip.Rect.Width);
            var height = Math.Max(1, strip.Rect.Height);
            Native.SetWindowPos(slot.Hwnd, Bottom, strip.Rect.X, strip.Rect.Y, width, height, Native.SwpNoActivate | Native.SwpShowWindow);
            ShowWindow(slot.Hwnd, ShowNoActivate);
            slot.Shown = true;
        }

        if (changed || moved)
        {
            InvalidateRect(slot.Hwnd, 0, false);
            UpdateWindow(slot.Hwnd);
        }
    }

    private nint Open(Slot slot, TileRect rect)
    {
        var hwnd = Native.CreateWindowEx(
            Native.WsExToolWindow | Native.WsExNoActivate,
            ClassName,
            "",
            unchecked((int)Native.WsPopup),
            rect.X,
            rect.Y,
            Math.Max(1, rect.Width),
            Math.Max(1, rect.Height),
            0,
            0,
            Native.GetModuleHandle(null),
            0);
        if (hwnd == 0)
        {
            Log.Warning("Winland", "a group strip was not created");
            return 0;
        }

        slot.Handle = GCHandle.Alloc(slot);
        Native.SetWindowLongPtr(hwnd, GwlpUserData, GCHandle.ToIntPtr(slot.Handle));
        ShellProperty.Tag(hwnd);
        return hwnd;
    }

    private void Dress(nint hwnd, bool light, int height)
    {
        var square = 1;
        var border = unchecked((int)0xFFFFFFFE);
        var dark = light ? 0 : 1;
        var still = 1;
        _ = Native.DwmSetWindowAttribute(hwnd, Native.DwmwaWindowCornerPreference, ref square, sizeof(int));
        _ = Native.DwmSetWindowAttribute(hwnd, Native.DwmwaBorderColor, ref border, sizeof(int));
        _ = Native.DwmSetWindowAttribute(hwnd, Native.DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        _ = Native.DwmSetWindowAttribute(hwnd, Native.DwmwaTransitionsForcedDisabled, ref still, sizeof(int));
        EnsureDraw(light, height);
    }

    private void EnsureDraw(bool light, int height)
    {
        var fontHeight = -Math.Max(11, height * 12 / Math.Max(1, GroupStripMetrics.HeightDips));
        if (_font != 0 && _light == light && _fontHeight == fontHeight)
        {
            return;
        }

        FreeDraw();
        _light = light;
        _fontHeight = fontHeight;
        _font = CreateFont(fontHeight, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        if (light)
        {
            _background = CreateSolidBrush(0x00F3F3F3);
            _tab = CreateSolidBrush(0x00FFFFFF);
            _active = CreateSolidBrush(0x00F8F0E8);
            _text = 0x001A1A1A;
            _muted = 0x00606060;
        }
        else
        {
            _background = CreateSolidBrush(0x00202020);
            _tab = CreateSolidBrush(0x002A2A2A);
            _active = CreateSolidBrush(0x00333333);
            _text = 0x00FFFFFF;
            _muted = 0x00A0A0A0;
        }

        _accent = CreateSolidBrush(0x00F59C5B);
    }

    private void FreeDraw()
    {
        Drop(ref _font);
        Drop(ref _background);
        Drop(ref _tab);
        Drop(ref _active);
        Drop(ref _accent);
    }

    private static void Drop(ref nint handle)
    {
        if (handle != 0)
        {
            Native.DeleteObject(handle);
            handle = 0;
        }
    }

    private void Pick(Slot slot, nint lParam)
    {
        var index = GroupStripHit.Tab((short)(lParam & 0xFFFF), slot.Rect.Width, slot.Members.Length);
        if (index >= 0 && index < slot.Members.Length)
        {
            _show(slot.Members[index]);
        }
    }

    private void Paint(Slot slot, nint hwnd)
    {
        var paint = BeginPaint(hwnd, out var info);
        if (paint == 0)
        {
            return;
        }

        var width = Math.Max(1, slot.Rect.Width);
        var height = Math.Max(1, slot.Rect.Height);
        var whole = new Rect(0, 0, width, height);
        FillRect(paint, ref whole, _background);
        var count = slot.Members.Length;
        if (count > 0 && _font != 0)
        {
            var previous = SelectObject(paint, _font);
            SetBkMode(paint, Transparent);
            var gap = Math.Max(2, height / 14);
            for (var i = 0; i < count; i++)
            {
                var left = (i * width / count) + gap;
                var right = ((i + 1) * width / count) - gap;
                if (right <= left)
                {
                    right = left + 1;
                }

                var body = new Rect(left, gap, right, height - gap);
                var shown = i == slot.Active;
                FillRect(paint, ref body, shown ? _active : _tab);
                if (shown)
                {
                    var line = new Rect(left, height - gap - 2, right, height - gap);
                    FillRect(paint, ref line, _accent);
                }

                SetTextColor(paint, shown ? _text : _muted);
                var label = i < slot.Titles.Length ? slot.Titles[i] : "";
                DrawText(paint, label, label.Length, ref body, DtCenter | DtVCenter | DtSingleLine | DtEndEllipsis | DtNoPrefix);
            }

            SelectObject(paint, previous);
        }

        EndPaint(hwnd, ref info);
    }

    private static nint Dispatch(nint hwnd, uint message, nint wParam, nint lParam)
    {
        var slot = SlotOf(hwnd);
        if (slot is null)
        {
            return Native.DefWindowProc(hwnd, message, wParam, lParam);
        }

        if (message == WmMouseActivate)
        {
            return MaNoActivate;
        }

        if (message == WmErase)
        {
            return 1;
        }

        if (message == WmLButtonUp || message == WmLButtonDblClk)
        {
            slot.Host.Pick(slot, lParam);
            return 0;
        }

        if (message == WmPaint)
        {
            slot.Host.Paint(slot, hwnd);
            return 0;
        }

        if (message == WmDestroy)
        {
            slot.Hwnd = 0;
            slot.Shown = false;
            if (slot.Handle.IsAllocated)
            {
                Native.SetWindowLongPtr(hwnd, GwlpUserData, 0);
                slot.Handle.Free();
            }

            return Native.DefWindowProc(hwnd, message, wParam, lParam);
        }

        return Native.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static Slot? SlotOf(nint hwnd)
    {
        var raw = Native.GetWindowLongPtr(hwnd, GwlpUserData);
        if (raw == 0)
        {
            return null;
        }

        try
        {
            return GCHandle.FromIntPtr(raw).Target as Slot;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool LightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed class Slot
    {
        public Slot(GroupStripHost host) => Host = host;

        public GroupStripHost Host { get; }

        public nint Hwnd { get; set; }

        public GCHandle Handle { get; set; }

        public long ShownAt { get; set; }

        public bool Shown { get; set; }

        public string Face { get; set; } = "";

        public string[] Titles { get; set; } = [];

        public long[] Members { get; set; } = [];

        public int Active { get; set; }

        public TileRect Rect { get; set; }

        public void Show(GroupStrip strip, string[] titles, bool light)
        {
            Titles = titles;
            Host.ShowSlot(this, strip, titles, light);
        }

        public void Leave()
        {
            if (!Shown || Hwnd == 0)
            {
                return;
            }

            Shown = false;
            ShowWindow(Hwnd, Native.SwHide);
        }

        public void Destroy()
        {
            if (Hwnd != 0)
            {
                Native.DestroyWindow(Hwnd);
            }
        }
    }

    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public Rect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }

    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public nint Hdc;
        public int Erase;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Restore;
        public int IncUpdate;
        public long Reserved0;
        public long Reserved1;
        public long Reserved2;
        public long Reserved3;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint BeginPaint(nint hwnd, out PaintStruct paint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(nint hwnd, ref PaintStruct paint);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint hdc, ref Rect rect, nint brush);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawText(nint hdc, string text, int count, ref Rect rect, uint format);

    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(uint color);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateFont(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        uint charSet,
        uint outPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string face);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(nint hdc, uint color);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(nint hdc, int mode);
}
