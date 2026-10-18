namespace Winland;

/// <summary>
/// The error bar <c>seterror</c> shows, as the pinned release draws it: a strip across the top of the focused Monitor,
/// in pixels, holding up to five lines of the message. Its height is kept out of that Monitor's work area.
/// </summary>
public sealed record ErrorBar(Rectangle Bounds, string Text, BorderColor Color, int FontSize);

public sealed partial class Core
{
    private const int ErrorLineLimit = 5;

    private (string Text, BorderColor Color)? _error;

    // seterror COLOR MESSAGE shows the bar, and seterror alone or seterror disable takes it away, as in the pinned release.
    private CommandReply SetError(SetErrorRequest request)
    {
        var words = request.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2)
        {
            _error = null;
            var reply = words.Length == 1 && !words[0].Contains("dis", StringComparison.Ordinal) ? "var 1 not color or disable" : "ok";
            return new CommandReply(reply, View([]));
        }

        var color = BorderColor.TryParse(words[0], out var parsed, out _) ? parsed : new BorderColor(0, 0, 0, 0);
        _error = (string.Join(' ', words[1..]), color);
        return new CommandReply("ok", View([]));
    }

    // The bar on the focused Monitor, and its height on that Monitor's work area, kept up to date after every change.
    // The placements of the Workspaces the reservation moved.
    private List<Placement> PlaceErrorBar(out ErrorBar? bar)
    {
        bar = null;
        var placements = new List<Placement>();
        LiveMonitor? focused = null;
        foreach (var screen in _monitors)
        {
            if (screen.Focused && screen.Effective.Enabled)
                focused = screen;
        }

        var changed = false;
        foreach (var screen in _monitors)
        {
            var reserve = 0;
            if (_error is { } error && screen == focused)
            {
                bar = ErrorBarOn(screen.Arranged, error.Text, error.Color, out reserve);
            }

            if (screen.BarReserve != reserve)
            {
                screen.BarReserve = reserve;
                changed = true;
            }
        }

        if (!changed)
            return placements;

        foreach (var desk in _workspaces)
        {
            Refit(desk);
            if (IsShown(desk.Id))
                placements.AddRange(desk.Layout.Visible());
        }

        return placements;
    }

    private static ErrorBar ErrorBarOn(Monitor monitor, string text, BorderColor color, out int reserve)
    {
        var scale = monitor.Scale <= 0 ? 1 : monitor.Scale;
        var fontSize = Math.Clamp((int)(10.0 * monitor.Width * scale / 1920.0), 8, 40);
        var pad = (int)(10.0 * scale);
        var visible = VisibleErrorText(text, ErrorLineLimit);
        var lines = 1 + visible.Count(character => character == '\n');
        var height = Math.Max(3, (lines * ErrorLineHeight(fontSize)) + 3);
        reserve = height + pad;
        var bounds = new Rectangle(monitor.X + pad, monitor.Y + pad, Math.Max(1, monitor.Width - (2 * pad)), height);
        return new ErrorBar(bounds, visible, color, fontSize);
    }

    /// <summary>The height of one line of bar text, in pixels.</summary>
    public static int ErrorLineHeight(int fontSize) => (int)Math.Ceiling(fontSize * 1.35);

    // The first lines of the message, and how many more there are, as the pinned release cuts a long error.
    private static string VisibleErrorText(string text, int limit)
    {
        var lines = text.Split('\n');
        if (lines.Length <= limit)
            return text;

        return string.Join('\n', lines[..limit]) + $"\n({lines.Length - limit} more...)";
    }
}
