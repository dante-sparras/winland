using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

/// <summary>
/// Draws the <c>notify</c> notifications as Winland does: a stack of black cards at the top right of the
/// work area, each with a strip and a progress line in its color, sliding in and out. One click-through layered window
/// on its own thread holds the stack, and it goes away with this process.
/// </summary>
// Stryker disable once all : draws on the desktop; the sizes and the slide are tested in NotificationFrames.
internal static class NotificationHost
{
    private const string ClassName = "WinlandNotifications";
    private const int ClassAlreadyExists = 1410;
    private const uint SyncMessage = 0x8000 + 0x4A;
    private const uint QuitMessage = 0x0012;
    private const uint TimerMessage = 0x0113;
    private const uint MouseActivateMessage = 0x0021;
    private const int NoActivate = 3;
    private const uint FrameMilliseconds = 16;

    private static readonly WNDPROC Proc = OnWindow;
    private static readonly Lock Gate = new();
    private static readonly List<NotificationAction> Pending = [];
    private static readonly List<NotificationCard> Notifications = [];
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static bool _posted;
    private static uint _thread;
    private static Thread? _worker;
    private static HWND _window;
    private static nuint _timer;

    /// <summary>Queues the notification changes of an outcome. Starts the thread for the first notification.</summary>
    internal static void Update(IReadOnlyList<NotificationAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Count == 0)
            return;

        uint thread;
        lock (Gate)
        {
            Pending.AddRange(actions);
            if (_worker is null)
            {
                _worker = new Thread(Run) { IsBackground = true, Name = "Winland notifications" };
                _worker.Start();
            }

            if (_posted || _thread == 0)
                return;

            _posted = true;
            thread = _thread;
        }

        _ = PInvoke.PostThreadMessage(thread, SyncMessage, default, default);
    }

    /// <summary>Takes every notification away and ends the thread.</summary>
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
            _posted = false;
            Pending.Clear();
        }

        if (worker is null)
            return;

        if (thread != 0)
            _ = PInvoke.PostThreadMessage(thread, QuitMessage, default, default);

        _ = worker.Join(TimeSpan.FromSeconds(2));
    }

    private static void Run()
    {
        _ = PInvoke.PeekMessage(out _, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
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

                if (message.hwnd.IsNull && message.message == TimerMessage)
                {
                    Frame();
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

        if (_timer != 0)
            _ = PInvoke.KillTimer(HWND.Null, _timer);

        if (!_window.IsNull)
            _ = PInvoke.DestroyWindow(_window);

        _timer = 0;
        _window = HWND.Null;
        Notifications.Clear();
    }

    private static void Sync()
    {
        NotificationAction[] actions;
        lock (Gate)
        {
            actions = [.. Pending];
            Pending.Clear();
            _posted = false;
        }

        var now = Clock.Elapsed.TotalMilliseconds;
        foreach (var action in actions)
            NotificationCards.Apply(Notifications, action, now);

        if (Notifications.Count > 0 && _timer == 0)
            _timer = PInvoke.SetTimer(HWND.Null, 0, FrameMilliseconds, null);

        Frame();
    }

    private static void Frame()
    {
        var now = Clock.Elapsed.TotalMilliseconds;
        NotificationCards.Expire(Notifications, now);
        if (Notifications.Count == 0)
        {
            if (_timer != 0)
                _ = PInvoke.KillTimer(HWND.Null, _timer);

            _timer = 0;
            if (!_window.IsNull)
                _ = PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_HIDE);

            return;
        }

        if (_window.IsNull)
            _window = Create();

        if (!_window.IsNull)
            Paint(_window, now);
    }

    private static unsafe void Paint(HWND hwnd, double now)
    {
        var place = Notifications[^1];
        var screen = PInvoke.GetDC(HWND.Null);
        var memory = PInvoke.CreateCompatibleDC(screen);
        try
        {
            // Every card is measured first, so the window fits the widest and the whole stack.
            var measured = new List<Card>(Notifications.Count);
            var height = NotificationFrames.OffsetY;
            var widest = 0;
            foreach (var shown in Notifications)
            {
                var card = Measure(memory, shown);
                measured.Add(card);
                height += card.Height + NotificationFrames.GapY;
                widest = Math.Max(widest, card.Width);
            }

            var width = widest + NotificationFrames.LeftBar + 1;
            height += NotificationFrames.OffsetY;
            var info = new BITMAPINFO();
            info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
            info.bmiHeader.biWidth = width;
            info.bmiHeader.biHeight = -height;
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            void* bits;
            var bitmap = PInvoke.CreateDIBSection(memory, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
            if (bitmap.IsNull || bits is null)
                return;

            var old = PInvoke.SelectObject(memory, bitmap);
            var pixels = new Span<uint>(bits, width * height);
            pixels.Clear();
            var top = NotificationFrames.OffsetY;
            for (var i = 0; i < measured.Count; i++)
            {
                var shown = Notifications[i];
                var card = measured[i];
                var life = shown.Notification.TimeMs;
                var since = shown.Since(now);
                var elapsed = shown.Elapsed(now);
                var (strip, slide) = NotificationFrames.Slide(since, elapsed, life);
                var stripWidth = (int)Math.Round((card.Width + NotificationFrames.LeftBar) * strip);
                var cardWidth = (int)Math.Round(card.Width * slide);
                var cardX = width - cardWidth;
                var color = shown.Notification.Color;
                Fill(pixels, width, height, new Rectangle(width - stripWidth, top, stripWidth, card.Height), color);
                Fill(pixels, width, height, new Rectangle(cardX, top, cardWidth, card.Height), new BorderColor(0xFF, 0, 0, 0));
                Write(memory, card, cardX, top);
                Opaque(pixels, width, height, new Rectangle(cardX, top, cardWidth, card.Height));
                var progress = (int)Math.Round(Math.Clamp(life <= 0 ? 1 : elapsed / life, 0, 1) * Math.Max(0, card.Width - 6));
                Fill(pixels, width, height, new Rectangle(cardX + 3, top + card.Height - 4, progress, 2), color);
                top += card.Height + NotificationFrames.GapY;
            }

            var size = new SIZE(width, height);
            var origin = new System.Drawing.Point(0, 0);
            var position = new System.Drawing.Point(place.WorkArea.X + place.WorkArea.Width - width, place.WorkArea.Y);
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            _ = PInvoke.UpdateLayeredWindow(hwnd, screen, &position, &size, memory, &origin, new COLORREF(0), &blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
            _ = PInvoke.SetWindowPos(
                hwnd,
                new HWND(-1),
                0,
                0,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
            _ = PInvoke.SelectObject(memory, old);
            _ = PInvoke.DeleteObject(bitmap);
        }
        finally
        {
            _ = PInvoke.DeleteDC(memory);
            _ = PInvoke.ReleaseDC(HWND.Null, screen);
        }
    }

    // The text and the icon of one card, measured in its font, and the card's size around them.
    private static Card Measure(HDC memory, NotificationCard shown)
    {
        var notification = shown.Notification;
        var fontPixels = NotificationFrames.FontPixels(notification.FontSize, shown.Monitor.Width, shown.Scale);
        var iconText = NotificationFrames.IconText(notification.Icon);
        var iconPixels = Math.Max(8, (int)Math.Round(fontPixels * NotificationFrames.IconScale));
        var text = Extent(memory, notification.Text, fontPixels, shown.Font);
        var icon = iconText.Length == 0 ? new SIZE(0, 0) : Extent(memory, iconText, iconPixels, shown.Font);
        var iconPad = iconText.Length == 0 ? 0 : NotificationFrames.IconPad;
        var width = text.cx + NotificationFrames.PadX + icon.cx + (2 * iconPad);
        var height = Math.Max(text.cy, icon.cy) + NotificationFrames.PadY;
        return new Card(notification.Text, iconText, shown.Font, fontPixels, iconPixels, text, icon, iconPad, width, height);
    }

    private static unsafe SIZE Extent(HDC memory, string text, int pixels, string family)
    {
        using var font = GdiFonts.Create(pixels, 400, FONT_QUALITY.ANTIALIASED_QUALITY, family);
        var previous = PInvoke.SelectObject(memory, font);
        var box = new RECT(0, 0, 0, 0);
        fixed (char* chars = text)
            _ = PInvoke.DrawText(memory, chars, text.Length, &box, DRAW_TEXT_FORMAT.DT_CALCRECT | DRAW_TEXT_FORMAT.DT_NOPREFIX);

        _ = PInvoke.SelectObject(memory, previous);
        return new SIZE(box.right - box.left, box.bottom - box.top);
    }

    // The icon and the message in white on the card, placed as Winland places them.
    private static void Write(HDC memory, Card card, int cardX, int top)
    {
        _ = PInvoke.SetBkMode(memory, BACKGROUND_MODE.TRANSPARENT);
        _ = PInvoke.SetTextColor(memory, new COLORREF(0x00FFFFFF));
        if (card.Icon.Length > 0)
        {
            var iconX = cardX + NotificationFrames.LeftBar + card.IconPad - 1;
            var iconY = top - 2 + (int)Math.Round((card.Height - card.IconSize.cy) / 2.0);
            Text(memory, card.Icon, card.IconPixels, card.Font, iconX, iconY, card.IconSize);
        }

        var textX = cardX + NotificationFrames.LeftBar + card.IconSize.cx + (2 * card.IconPad);
        var textY = top - 2 + (int)Math.Round((card.Height - card.TextSize.cy) / 2.0);
        Text(memory, card.Text, card.FontPixels, card.Font, textX, textY, card.TextSize);
    }

    private static unsafe void Text(HDC memory, string text, int pixels, string family, int x, int y, SIZE size)
    {
        using var font = GdiFonts.Create(pixels, 400, FONT_QUALITY.ANTIALIASED_QUALITY, family);
        var previous = PInvoke.SelectObject(memory, font);
        var box = new RECT(x, y, x + size.cx + 1, y + size.cy + 1);
        fixed (char* chars = text)
            _ = PInvoke.DrawText(memory, chars, text.Length, &box, DRAW_TEXT_FORMAT.DT_NOPREFIX | DRAW_TEXT_FORMAT.DT_NOCLIP);

        _ = PInvoke.SelectObject(memory, previous);
    }

    private static void Fill(Span<uint> pixels, int width, int height, Rectangle box, BorderColor color)
    {
        uint alpha = color.A;
        var pixel = (alpha << 24)
            | ((uint)(color.R * alpha / 255) << 16)
            | ((uint)(color.G * alpha / 255) << 8)
            | (uint)(color.B * alpha / 255);
        Each(pixels, width, height, box, (ref uint value) => value = pixel);
    }

    // GDI clears the alpha of the pixels it draws text on. The card is opaque, so every pixel on it is.
    private static void Opaque(Span<uint> pixels, int width, int height, Rectangle box) =>
        Each(pixels, width, height, box, (ref uint value) => value |= 0xFF000000);

    private delegate void PixelAction(ref uint value);

    private static void Each(Span<uint> pixels, int width, int height, Rectangle box, PixelAction action)
    {
        var left = Math.Max(0, box.X);
        var top = Math.Max(0, box.Y);
        var right = Math.Min(width, box.X + box.Width);
        var bottom = Math.Min(height, box.Y + box.Height);
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
                action(ref pixels[(y * width) + x]);
        }
    }

    private static LRESULT OnWindow(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam) =>
        message == MouseActivateMessage ? new LRESULT(NoActivate) : PInvoke.DefWindowProc(hwnd, message, wParam, lParam);

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
                throw new InvalidOperationException($"The notification window class was not registered ({Marshal.GetLastPInvokeError()}).");
        }
    }

    private static unsafe HWND Create() =>
        PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE
                | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TRANSPARENT,
            ClassName,
            "",
            WINDOW_STYLE.WS_POPUP,
            0,
            0,
            0,
            0,
            HWND.Null,
            null,
            null,
            null);

    private readonly record struct Card(
        string Text,
        string Icon,
        string Font,
        int FontPixels,
        int IconPixels,
        SIZE TextSize,
        SIZE IconSize,
        int IconPad,
        int Width,
        int Height);
}
