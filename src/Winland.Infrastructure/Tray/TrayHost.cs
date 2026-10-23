using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

/// <summary>
/// The notification-area icon on its own thread, and the balloon a config error shows on it. Which number it
/// shows, what the balloon says, and what a click opens, is decided before this: here is only the icon, the
/// balloon, the window that receives Explorer, and the filter that lets an elevated process hear an unelevated
/// one. Nothing is added until <see cref="Start"/>, so a test that applies an outcome does not put an icon on
/// the desktop.
/// </summary>
internal static class TrayHost
{
    private const string ClassName = "WinlandTray";
    private const int ClassAlreadyExists = 1410;
    private const uint IconId = 1;
    private const uint SyncMessage = 0x8000 + 0x72;
    private const uint QuitMessage = 0x0012;
    private const uint LeftButtonUp = 0x0202;
    private const uint RightButtonUp = 0x0205;
    private const uint ContextMenu = 0x007B;
    private const uint Select = 0x0400;
    private const uint KeySelect = 0x0401;
    private const uint BalloonHide = 0x0403;
    private const uint BalloonTimeout = 0x0404;
    private const uint BalloonUserClick = 0x0405;
    private const int Version4 = 4;

    // TrackPopupMenuEx takes the flags as a plain uint. Right-button, return the command id, and do not notify.
    private const uint MenuRightButton = 0x0002;
    private const uint MenuReturnCommand = 0x0100;
    private const uint MenuNoNotify = 0x0080;

    private static readonly WNDPROC Proc = OnWindow;
    private static readonly Lock Gate = new();
    private static TrayIcon? _pending;
    private static TrayIcon? _shown;
    private static ConfigNotice? _pendingNotice;
    private static ConfigNotice? _shownNotice;
    private static Action<TrayCommand>? _onCommand;
    private static Action<string>? _onOpen;
    private static HWND _window;
    private static HICON _icon;
    private static uint _thread;
    private static uint _taskbarCreated;
    private static int _armed;
    private static int _shownGeneration;
    private static int _closedGeneration;
    private static int _swallowHides;
    private static bool _posted;
    private static bool _added;
    private static bool _temporary;
    private static long _menuAt;
    private static Thread? _worker;

    /// <summary>Lets later updates add the icon, and remembers where a chosen command or a balloon click is posted.</summary>
    internal static void Start(Action<TrayCommand> onCommand, Action<string>? onOpen = null)
    {
        ArgumentNullException.ThrowIfNull(onCommand);
        lock (Gate)
        {
            _onCommand = onCommand;
            _onOpen = onOpen;
            _armed = 1;
        }
    }

    /// <summary>
    /// Shows <paramref name="icon"/>, or removes it when the icon says it is hidden, and shows <paramref name="notice"/>
    /// on it. A hidden icon is added only while the notice is up. Null leaves the icon alone.
    /// </summary>
    internal static void Update(TrayIcon? icon, ConfigNotice? notice)
    {
        if (icon is null || Volatile.Read(ref _armed) == 0)
            return;

        uint thread;
        lock (Gate)
        {
            if (_armed == 0)
                return;

            if (_worker is null && !icon.Shown && notice is null)
                return;

            _pending = icon;
            _pendingNotice = notice;
            if (_worker is null)
            {
                _worker = new Thread(Run) { IsBackground = true, Name = "Winland tray" };
                _worker.Start();
            }

            if (_posted || _thread == 0)
                return;

            _posted = true;
            thread = _thread;
        }

        _ = PInvoke.PostThreadMessage(thread, SyncMessage, default, default);
    }

    /// <summary>Removes the icon and ends its thread.</summary>
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
            _pending = null;
            _pendingNotice = null;
            _onOpen = null;
            _posted = false;
        }

        if (worker is null)
        {
            _shownNotice = null;
            _shownGeneration = 0;
            _closedGeneration = 0;
            _temporary = false;
            _swallowHides = 0;
            return;
        }

        if (thread != 0)
            _ = PInvoke.PostThreadMessage(thread, QuitMessage, default, default);

        _ = worker.Join(TimeSpan.FromSeconds(2));
    }

    private static void Run()
    {
        _ = PInvoke.PeekMessage(out _, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
        _taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
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

        Remove();
        if (!_window.IsNull)
            _ = PInvoke.DestroyWindow(_window);

        _window = HWND.Null;
        _shown = null;
        _shownNotice = null;
        _shownGeneration = 0;
        _closedGeneration = 0;
        _temporary = false;
        _swallowHides = 0;
    }

    private static void Sync()
    {
        TrayIcon? wanted;
        ConfigNotice? notice;
        lock (Gate)
        {
            wanted = _pending;
            notice = _pendingNotice;
            _posted = false;
        }

        var plan = TrayBalloon.For(wanted is { Shown: true }, notice, _shownGeneration, _closedGeneration);
        if (!plan.ShowIcon)
        {
            TakeDown();
            _shown = wanted;
            _shownNotice = notice;
            if (notice is null)
            {
                _shownGeneration = 0;
                _closedGeneration = 0;
            }

            return;
        }

        var window = EnsureWindow();
        if (window.IsNull)
            return;

        if (plan.Temporary)
        {
            if (!_added || !_temporary)
                Present(window, "!", "Winland");

            _temporary = true;
        }
        else
        {
            // Changing the icon while a balloon is up takes the balloon away, so an outcome that
            // leaves the number alone does not send the icon again.
            var same = _added && !_temporary && _shown == wanted;
            _temporary = false;
            if (!same)
                Present(window, wanted!);
        }

        _shown = wanted;
        _shownNotice = notice;
        if (!_added)
            return;

        if (plan.ShowBalloon && notice is not null)
        {
            ShowBalloon(window, notice);
            _shownGeneration = notice.Generation;
        }
        else if (plan.ClearBalloon)
        {
            HideBalloon(window);
            _shownGeneration = 0;
            _closedGeneration = 0;
        }
    }

    private static HWND EnsureWindow()
    {
        if (!_window.IsNull)
            return _window;

        _window = Create();
        if (_window.IsNull)
        {
            Log("The notification-area window was not created");
            return HWND.Null;
        }

        // A message-only window never sees TaskbarCreated. This one is top-level so Explorer's broadcast arrives,
        // and the filter lets that broadcast, and the click, through from the unelevated shell.
        foreach (var message in TrayMessages.Allowed(_taskbarCreated))
            Allow(message);

        return _window;
    }

    private static unsafe void Allow(uint message)
    {
        if (PInvoke.ChangeWindowMessageFilterEx(_window, message, WINDOW_MESSAGE_FILTER_ACTION.MSGFLT_ALLOW, null))
            return;

        Log("A notification-area message was not allowed through");
    }

    private static unsafe void Present(HWND window, TrayIcon icon) => Present(window, icon.Number, icon.Tip);

    private static unsafe void Present(HWND window, string label, string tip)
    {
        var drawn = Draw(label);
        if (drawn.IsNull)
            return;

        var data = Blank(window);
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP;
        data.uCallbackMessage = TrayMessages.Callback;
        data.hIcon = drawn;
        WriteTip(ref data, tip);

        var added = _added && Tell(NOTIFY_ICON_MESSAGE.NIM_MODIFY, ref data);
        if (!added)
        {
            added = Tell(NOTIFY_ICON_MESSAGE.NIM_ADD, ref data);
            if (added)
                SetVersion(window);
        }

        if (!added)
        {
            Log("The notification-area icon was not added");
            _ = PInvoke.DestroyIcon(drawn);
            _added = false;
            return;
        }

        _added = true;
        var previous = _icon;
        _icon = drawn;
        if (!previous.IsNull)
            _ = PInvoke.DestroyIcon(previous);
    }

    // NOTIFYICON_VERSION_4 reports the icon id in the high word, so a click can be told from a classic one.
    // Explorer starting again drops the icon, and the next add has to ask for that version once more.
    private static unsafe void SetVersion(HWND window)
    {
        var data = Blank(window);
        data.Anonymous.uVersion = Version4;
        if (!Tell(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, ref data))
            Log("The notification-area icon version was not set");
    }

    private static unsafe void Remove()
    {
        if (_added && !_window.IsNull)
        {
            var data = Blank(_window);
            if (!Tell(NOTIFY_ICON_MESSAGE.NIM_DELETE, ref data))
                Log("The notification-area icon was not removed");

            _added = false;
        }

        if (!_icon.IsNull)
        {
            _ = PInvoke.DestroyIcon(_icon);
            _icon = default;
        }
    }

    private static NOTIFYICONDATAW Blank(HWND window)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = window,
            uID = IconId,
        };
        return data;
    }

    private static unsafe bool Tell(NOTIFY_ICON_MESSAGE message, ref NOTIFYICONDATAW data)
    {
        fixed (NOTIFYICONDATAW* pointer = &data)
            return PInvoke.Shell_NotifyIcon(message, pointer);
    }

    private static void WriteTip(ref NOTIFYICONDATAW data, string tip)
    {
        var text = tip.Length > 127 ? tip[..127] : tip;
        for (var i = 0; i < text.Length; i++)
            data.szTip[i] = text[i];

        data.szTip[text.Length] = '\0';
    }

    // A new balloon replaces the one already showing. szInfo is 256 characters with the null, and the title 64.
    private static void ShowBalloon(HWND window, ConfigNotice notice)
    {
        var data = Blank(window);
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_INFO;
        WriteTitle(ref data, notice.Title);
        WriteBody(ref data, notice.Text);
        data.dwInfoFlags = NOTIFY_ICON_INFOTIP_FLAGS.NIIF_ERROR;
        _swallowHides = 1;
        if (!Tell(NOTIFY_ICON_MESSAGE.NIM_MODIFY, ref data))
            Log("The config notification was not shown");
    }

    // An empty body takes the balloon away and leaves the icon.
    private static void HideBalloon(HWND window)
    {
        var data = Blank(window);
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_INFO;
        data.szInfo[0] = '\0';
        _swallowHides = 1;
        if (!Tell(NOTIFY_ICON_MESSAGE.NIM_MODIFY, ref data))
            Log("The config notification was not removed");
    }

    private static void WriteTitle(ref NOTIFYICONDATAW data, string title)
    {
        var text = title.Length > 63 ? title[..63] : title;
        for (var i = 0; i < text.Length; i++)
            data.szInfoTitle[i] = text[i];

        data.szInfoTitle[text.Length] = '\0';
    }

    private static void WriteBody(ref NOTIFYICONDATAW data, string body)
    {
        var text = body.Length > 255 ? body[..255] : body;
        for (var i = 0; i < text.Length; i++)
            data.szInfo[i] = text[i];

        data.szInfo[text.Length] = '\0';
    }

    private static void TakeDown()
    {
        if (!_added && _icon.IsNull)
            return;

        _temporary = false;
        _swallowHides++;
        Remove();
    }

    private static LRESULT OnWindow(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (_taskbarCreated != 0 && message == _taskbarCreated)
        {
            // The shell that held the icon is gone. Add it again; modifying the old one never comes back.
            _added = false;
            if (_shown is { Shown: true } icon)
                Present(hwnd, icon);
            else if (_temporary && _shownGeneration != 0 && _shownGeneration != _closedGeneration)
                Present(hwnd, "!", "Winland");

            if (_added && _shownNotice is not null && _shownGeneration != 0 && _shownGeneration != _closedGeneration)
                ShowBalloon(hwnd, _shownNotice);

            return default;
        }

        if (message == TrayMessages.Callback)
            OnClick(hwnd, wParam, lParam);

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    // Version 4 packs the event in the low word and the icon id in the high word, with the point in wParam.
    // A classic callback puts the icon id in wParam and the mouse message in lParam. One click can deliver
    // both a select and a button-up, so a second open within a moment is the same click.
    private static void OnClick(HWND hwnd, WPARAM wParam, LPARAM lParam)
    {
        var low = (uint)(lParam.Value & 0xFFFF);
        var high = (uint)((lParam.Value >> 16) & 0xFFFF);
        if (high == IconId)
        {
            if (IsBalloon(low))
            {
                OnBalloon(low);
                return;
            }

            if (low is Select or KeySelect or ContextMenu or RightButtonUp)
                ShowMenu(hwnd, PointFrom(wParam));

            return;
        }

        if ((uint)(wParam.Value & 0xFFFF) == IconId && IsBalloon(low))
        {
            OnBalloon(low);
            return;
        }

        if ((uint)(wParam.Value & 0xFFFF) == IconId && low is LeftButtonUp or RightButtonUp or ContextMenu)
            ShowMenu(hwnd, Cursor());
    }

    private static bool IsBalloon(uint code) => code is BalloonHide or BalloonTimeout or BalloonUserClick;

    // A hide that follows our own add, replace or delete is not the user dismissing the balloon.
    // A timeout or a click is. A click opens the file the notice names, and a temporary icon goes away.
    private static void OnBalloon(uint code)
    {
        if (code == BalloonHide && _swallowHides > 0)
        {
            _swallowHides--;
            return;
        }

        if (code == BalloonUserClick)
            OpenShown();

        _closedGeneration = _shownGeneration;
        if (!_temporary)
            return;

        _temporary = false;
        _swallowHides++;
        Remove();
    }

    private static void OpenShown()
    {
        string? file;
        Action<string>? open;
        lock (Gate)
        {
            file = _shownNotice?.File;
            open = _onOpen;
        }

        if (!string.IsNullOrEmpty(file))
            open?.Invoke(file);
    }

    private static (int X, int Y) PointFrom(WPARAM wParam)
    {
        var bits = unchecked((long)wParam.Value);
        var x = (short)(bits & 0xFFFF);
        var y = (short)((bits >> 16) & 0xFFFF);
        if (x == 0 && y == 0)
            return Cursor();

        return (x, y);
    }

    private static (int X, int Y) Cursor() =>
        PInvoke.GetCursorPos(out var point) ? (point.X, point.Y) : (0, 0);

    private static unsafe void ShowMenu(HWND hwnd, (int X, int Y) at)
    {
        var now = Environment.TickCount64;
        if (now - _menuAt < 300)
            return;

        _menuAt = now;
        var menu = PInvoke.CreatePopupMenu();
        if (menu.IsNull)
        {
            Log("The notification-area menu was not created");
            return;
        }

        try
        {
            for (var i = 0; i < TrayIcon.Menu.Count; i++)
            {
                var label = TrayIcon.Menu[i].Label;
                fixed (char* chars = label)
                {
                    if (!PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, (nuint)(i + 1), chars))
                        Log("A notification-area command was not added");
                }
            }

            // Without the foreground, the menu never dismisses. The null message is what lets it go.
            _ = PInvoke.SetForegroundWindow(hwnd);
            var chosen = PInvoke.TrackPopupMenuEx(
                menu,
                MenuRightButton | MenuReturnCommand | MenuNoNotify,
                at.X,
                at.Y,
                hwnd,
                null);
            _ = PInvoke.PostMessage(hwnd, 0, default, default);
            var id = chosen.Value;
            if (id < 1 || id > TrayIcon.Menu.Count)
                return;

            Action<TrayCommand>? command;
            lock (Gate)
                command = _onCommand;

            command?.Invoke(TrayIcon.Menu[id - 1].Command);
        }
        finally
        {
            _ = PInvoke.DestroyMenu(menu);
        }
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
                cbSize = 80,
                lpfnWndProc = Proc,
                hInstance = instance,
                lpszClassName = name,
            };
            if (PInvoke.RegisterClassEx(in info) != 0)
                return;

            if (Marshal.GetLastPInvokeError() != ClassAlreadyExists)
                throw new InvalidOperationException($"The notification-area window class was not registered ({Marshal.GetLastPInvokeError()}).");
        }
    }

    private static unsafe HWND Create() =>
        PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
            ClassName,
            "Winland",
            WINDOW_STYLE.WS_POPUP,
            0,
            0,
            0,
            0,
            HWND.Null,
            null,
            null,
            null);

    private static unsafe HICON Draw(string text)
    {
        var size = Math.Max(16, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON));
        var screen = PInvoke.GetDC(HWND.Null);
        var memory = PInvoke.CreateCompatibleDC(screen);
        var info = new BITMAPINFO();
        info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = size;
        info.bmiHeader.biHeight = -size;
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        void* bits;
        var color = PInvoke.CreateDIBSection(memory, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
        if (color.IsNull || bits is null)
        {
            _ = PInvoke.DeleteDC(memory);
            _ = PInvoke.ReleaseDC(HWND.Null, screen);
            Log("The notification-area icon was not drawn");
            return default;
        }

        var old = PInvoke.SelectObject(memory, color);
        var pixels = new Span<uint>(bits, size * size);
        pixels.Clear();
        DrawLabel(memory, size, text);
        // GDI may still hold the label's drawing back. The pixels are read once it has written them.
        _ = PInvoke.GdiFlush();
        TrayBadge.Compose(pixels, size);
        _ = PInvoke.SelectObject(memory, old);
        _ = PInvoke.DeleteDC(memory);
        _ = PInvoke.ReleaseDC(HWND.Null, screen);

        var mask = Mask(pixels, size);
        if (mask.IsNull)
        {
            _ = PInvoke.DeleteObject(color);
            Log("The notification-area icon was not drawn");
            return default;
        }

        var iconInfo = new ICONINFO
        {
            fIcon = true,
            hbmColor = color,
            hbmMask = mask,
        };
        var icon = PInvoke.CreateIconIndirect(&iconInfo);
        _ = PInvoke.DeleteObject(color);
        _ = PInvoke.DeleteObject(mask);
        if (icon.IsNull)
            Log("The notification-area icon was not drawn");

        return icon;
    }

    private static unsafe void DrawLabel(HDC memory, int size, string text)
    {
        _ = PInvoke.SetBkMode(memory, BACKGROUND_MODE.TRANSPARENT);
        var height = FontHeight(size, text.Length);
        using var font = GdiFonts.Create(height, 700, FONT_QUALITY.ANTIALIASED_QUALITY, FontNames.Default);
        var previous = PInvoke.SelectObject(memory, font);
        _ = PInvoke.SetTextColor(memory, new COLORREF(0x00FFFFFF));
        var box = new RECT(0, 0, size, size);
        fixed (char* chars = text)
        {
            _ = PInvoke.DrawText(
                memory,
                chars,
                text.Length,
                &box,
                DRAW_TEXT_FORMAT.DT_CENTER | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_NOPREFIX);
        }

        _ = PInvoke.SelectObject(memory, previous);
    }

    private static int FontHeight(int size, int length)
    {
        var percent = length switch
        {
            1 => 62,
            2 => 48,
            3 => 36,
            _ => 28,
        };
        return Math.Max(6, size * percent / 100);
    }

    // The mask is bottom-up. A set bit is transparent, which keeps the corners clear where the alpha is not read.
    private static unsafe HBITMAP Mask(Span<uint> pixels, int size)
    {
        var stride = ((size + 31) / 32) * 4;
        var bytes = new byte[stride * size];
        for (var y = 0; y < size; y++)
        {
            var row = (size - 1 - y) * stride;
            for (var x = 0; x < size; x++)
            {
                if (TrayBadge.Clear(pixels[(y * size) + x]))
                    bytes[row + (x / 8)] |= (byte)(0x80 >> (x % 8));
            }
        }

        fixed (byte* bits = bytes)
            return PInvoke.CreateBitmap(size, size, 1, 1, bits);
    }

    private static void Log(string message) =>
        Console.Error.WriteLine($"{message} ({Marshal.GetLastPInvokeError()}).");
}
