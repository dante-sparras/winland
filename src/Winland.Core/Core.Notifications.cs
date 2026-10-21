using System.Globalization;

namespace Winland;

/// <summary>One notification <c>notify</c> asked for: its message, color, lifetime in milliseconds, icon, font size and id.</summary>
public sealed record Notification(string Text, BorderColor Color, double TimeMs, int Icon, double FontSize, long Id);

/// <summary>A change to the notifications on screen.</summary>
public abstract record NotificationAction;

/// <summary>
/// A notification to show at the top right of the work area of the Monitor focused when it was asked for, in the font
/// <c>misc:font_family</c> names.
/// </summary>
public sealed record NotificationAdded(
    Notification Notification,
    Rectangle Monitor,
    Rectangle WorkArea,
    double Scale,
    string Font,
    double ElapsedMs = 0,
    double SinceCreationMs = 0,
    bool Paused = false) : NotificationAction;

/// <summary>Takes away the oldest <paramref name="Amount"/> notifications, or every one when it is negative.</summary>
public sealed record NotificationsDismissed(int Amount) : NotificationAction;

/// <summary>Takes away the notification with this id.</summary>
public sealed record NotificationDismissed(long Id) : NotificationAction;

/// <summary>Replaces the text, color, lifetime, icon, font and pause of the notification with this id.</summary>
public sealed record NotificationChanged(
    long Id,
    string Text,
    BorderColor Color,
    double TimeMs,
    int Icon,
    double FontSize,
    bool Paused,
    double ElapsedMs) : NotificationAction;

/// <summary>One notification on screen, timed from the moment it was shown so a pause can hold its progress.</summary>
public sealed class NotificationCard
{
    public NotificationCard(NotificationAdded added, double now)
    {
        Notification = added.Notification;
        Monitor = added.Monitor;
        WorkArea = added.WorkArea;
        Scale = added.Scale;
        Font = added.Font;
        CreatedAt = now - added.SinceCreationMs;
        ElapsedAt = now - added.ElapsedMs;
        Frozen = added.ElapsedMs;
        Paused = added.Paused;
    }

    public Notification Notification { get; private set; }

    public Rectangle Monitor { get; }

    public Rectangle WorkArea { get; }

    public double Scale { get; }

    public string Font { get; }

    public bool Paused { get; private set; }

    private double CreatedAt { get; }

    private double ElapsedAt;

    private double Frozen;

    public double Since(double now) => now - CreatedAt;

    public double Elapsed(double now) => Paused ? Frozen : now - ElapsedAt;

    public bool Gone(double now)
    {
        var life = Notification.TimeMs;
        return !Paused && !double.IsNaN(life) && Elapsed(now) > Math.Truncate(life);
    }

    public void Change(NotificationChanged changed, double now)
    {
        Notification = Notification with
        {
            Text = changed.Text,
            Color = changed.Color,
            TimeMs = changed.TimeMs,
            Icon = changed.Icon,
            FontSize = changed.FontSize,
        };
        Paused = changed.Paused;
        Frozen = changed.ElapsedMs;
        ElapsedAt = now - changed.ElapsedMs;
    }
}

/// <summary>Applies notification changes to the cards on screen, and drops the ones whose time has run out.</summary>
public static class NotificationCards
{
    public static void Apply(List<NotificationCard> cards, NotificationAction action, double now)
    {
        ArgumentNullException.ThrowIfNull(cards);
        switch (action)
        {
            case NotificationAdded added:
                cards.Add(new NotificationCard(added, now));
                break;
            case NotificationsDismissed dismissed:
                var count = dismissed.Amount < 0 ? cards.Count : Math.Min(Math.Max(dismissed.Amount, 0), cards.Count);
                if (count > 0)
                    cards.RemoveRange(0, count);

                break;
            case NotificationDismissed one:
                cards.RemoveAll(card => card.Notification.Id == one.Id);
                break;
            case NotificationChanged changed:
                cards.Find(card => card.Notification.Id == changed.Id)?.Change(changed, now);
                break;
        }
    }

    public static void Expire(List<NotificationCard> cards, double now)
    {
        ArgumentNullException.ThrowIfNull(cards);
        cards.RemoveAll(card => card.Gone(now));
    }
}

/// <summary>The sizes, icons and slide of Winland's notifications.</summary>
public static class NotificationFrames
{
    public const int LeftBar = 5;
    public const int PadX = 20;
    public const int PadY = 10;
    public const int OffsetY = 10;
    public const int GapY = 10;
    public const int IconPad = 3;
    public const double IconScale = 0.9;
    public const int NoIcon = 6;

    private const double SlideMs = 600;
    private const double LagMs = 100;

    private static readonly string[] Icons = ["[!]", "[i]", "[Hint]", "[Err]", "[?]", "[ok]", ""];

    private static readonly BorderColor[] IconColors =
    [
        new(0xFF, 255, 204, 102),
        new(0xFF, 128, 255, 255),
        new(0xFF, 179, 255, 204),
        new(0xFF, 255, 77, 77),
        new(0xFF, 255, 204, 153),
        new(0xFF, 128, 255, 128),
        new(0xFF, 0, 0, 0),
    ];

    /// <summary>The text that stands for an icon when no icon font is installed.</summary>
    public static string IconText(int icon) => Icons[Math.Clamp(icon, 0, NoIcon)];

    /// <summary>The color of an icon, which a notification asked for with the color 0 takes.</summary>
    public static BorderColor IconColor(int icon) => IconColors[Math.Clamp(icon, 0, NoIcon)];

    /// <summary>The text size in pixels: the font size for each 1920 pixels of the Monitor's width, from 8 to 40.</summary>
    public static int FontPixels(double fontSize, int monitorWidth, double scale) =>
        Math.Clamp((int)(fontSize * (monitorWidth * (scale <= 0 ? 1 : scale) / 1920.0)), 8, 40);

    /// <summary>
    /// How far the colored strip and the black card have slid in, from 0 to 1, at <paramref name="sinceCreation"/> and
    /// <paramref name="elapsed"/> milliseconds of a notification that lives <paramref name="life"/>. Both slide in when it
    /// appears and out before it goes, the strip a little ahead.
    /// </summary>
    public static (double Strip, double Card) Slide(double sinceCreation, double elapsed, double life)
    {
        var strip = Progress(sinceCreation, elapsed, life, SlideMs - LagMs);
        var card = Progress(sinceCreation, elapsed, life, SlideMs);
        return (strip, card);
    }

    private static double Progress(double sinceCreation, double elapsed, double life, double span)
    {
        var raw = sinceCreation > span ? (elapsed > life - span ? life - elapsed : span) : sinceCreation;
        var t = Math.Clamp(raw / span, 0, 1);
        return t >= 0.99 ? 1 : Bezier.Default.Ease(t);
    }
}

public sealed partial class Core
{
    private readonly NotificationBook _notes = new();

    internal void UseNotificationClock(Func<long> now) => _notes.UseClock(now);

    // notify ICON TIME_MS COLOR [fontsize:SIZE] MESSAGE, with Winland's replies.
    private CommandReply Notify(NotifyRequest request)
    {
        var words = request.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 4)
            return new CommandReply("not enough args", View([]));

        if (!int.TryParse(words[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var icon))
            return new CommandReply("invalid arg 1", View([]));

        if (icon is < 0 or > NotificationFrames.NoIcon)
            icon = NotificationFrames.NoIcon;

        if (!int.TryParse(words[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var time))
            return new CommandReply("invalid arg 2", View([]));

        BorderColor color;
        if (words[2] == "0")
            color = NotificationFrames.IconColor(icon);
        else if (BorderColor.TryParse(words[2], out var parsed, out _))
            color = parsed.A == 0 && parsed.R == 0 && parsed.G == 0 && parsed.B == 0 ? NotificationFrames.IconColor(icon) : parsed;
        else
            return new CommandReply("invalid arg 3", View([]));

        var message = 3;
        var fontSize = 13.0;
        if (words[3].Length > 9 && words[3].StartsWith("fontsize:", StringComparison.Ordinal))
        {
            if (!double.TryParse(words[3][9..], NumberStyles.Float, CultureInfo.InvariantCulture, out fontSize))
                return new CommandReply("invalid fontsize kwarg", View([]));

            message++;
        }

        if (words.Length <= message)
            return new CommandReply("not enough args", View([]));

        var actions = _notes.Notify(string.Join(' ', words[message..]), color, Math.Max(time, 1), icon, fontSize, Spot());
        return new CommandReply("ok", View([]) with { Notifications = actions });
    }

    // dismissnotify [AMOUNT]: the oldest AMOUNT notifications, or all of them.
    private CommandReply DismissNotify(DismissNotifyRequest request)
    {
        var text = request.Arguments.Trim();
        var amount = -1;
        if (text.Length > 0)
        {
            var first = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            if (!int.TryParse(first, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount))
                return new CommandReply("invalid arg 1", View([]));
        }

        return new CommandReply("ok", View([]) with { Notifications = _notes.DismissOldest(amount) });
    }

    // notification {json}: one notification, or the list of them, for hl.notification.
    private CommandReply Notification(NotificationRequest request)
    {
        var (text, actions) = _notes.Execute(request.Arguments, Spot());
        return new CommandReply(text, View([]) with { Notifications = actions });
    }

    private NotificationSpot Spot()
    {
        var font = FontNames.Windows(_session.FontFamily);
        var monitor = FocusedMonitor()?.Effective;
        if (monitor is null)
            return NotificationSpot.Fallback with { Font = font };

        return new NotificationSpot(
            new Rectangle(monitor.X, monitor.Y, monitor.Width, monitor.Height),
            monitor.WorkArea,
            monitor.Scale,
            font);
    }
}
