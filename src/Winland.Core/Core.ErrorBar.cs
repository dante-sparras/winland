using System.Globalization;
using System.Text;

namespace Winland;

/// <summary>
/// The error bar <c>seterror</c> and a config with errors show, as the pinned release draws it: a strip across the top
/// or bottom of the focused Monitor, in pixels, holding up to <c>debug:error_limit</c> lines of the message. Its height
/// is kept out of that Monitor's work area.
/// </summary>
public sealed record ErrorBar(Rectangle Bounds, string Text, BorderColor Color, int FontSize)
{
    /// <summary>The Windows font of the message, from <c>misc:font_family</c>.</summary>
    public string Font { get; init; } = FontNames.Default;
}

public sealed partial class Core
{
    // The most lines of errors the message holds before it gives up, as the pinned release's config reload counts them.
    private const int ConfigErrorLines = 15;

    // The pinned release's error color, the first of its error gradient.
    private static readonly BorderColor ConfigErrorColor = new(0xFF, 0xFF, 0x66, 0x66);

    private (string Text, BorderColor Color)? _error;
    private DebugOptions _debug = DebugOptions.Documented;

    /// <summary>The <c>debug</c> options of the config read last, which say where Winland's log goes.</summary>
    public DebugOptions Debug => _debug;

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

    // A config read or reloaded with errors shows them in the bar, unless debug:suppress_errors is set, and one without
    // takes the bar away, a seterror one too, as the pinned release does after it reads its config. A notice that a
    // keyword or feature is a Gap is not an error there, so it is only logged.
    private void ShowConfigErrors(ConfigDocument document)
    {
        var errors = document.Warnings.Where(warning => !ConfigWarnings.IsGapNotice(warning.Message)).ToList();
        if (errors.Count == 0 || _debug.SuppressErrors)
        {
            _error = null;
            return;
        }

        var text = new StringBuilder("Your config has errors:");
        var lines = 1;
        foreach (var error in errors)
        {
            text.Append('\n').Append(error.Line > 0 ? string.Create(CultureInfo.InvariantCulture, $"line {error.Line}: {error.Message}") : error.Message);
            if (++lines > ConfigErrorLines)
            {
                text.Append("\n... more");
                break;
            }
        }

        _error = (text.ToString(), ConfigErrorColor);
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
        var bottom = _debug.ErrorPosition == 1;
        foreach (var screen in _monitors)
        {
            var reserve = 0;
            if (_error is { } error && screen == focused)
            {
                bar = ErrorBarOn(screen.Arranged, error.Text, error.Color, _debug.ErrorLimit, bottom, out reserve) with
                {
                    Font = FontNames.Windows(_session.FontFamily),
                };
            }

            if (screen.BarReserve != reserve || screen.BarAtBottom != bottom)
            {
                screen.BarReserve = reserve;
                screen.BarAtBottom = bottom;
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

    private static ErrorBar ErrorBarOn(Monitor monitor, string text, BorderColor color, int limit, bool bottom, out int reserve)
    {
        var scale = monitor.Scale <= 0 ? 1 : monitor.Scale;
        var fontSize = Math.Clamp((int)(10.0 * monitor.Width * scale / 1920.0), 8, 40);
        var pad = (int)(10.0 * scale);
        var visible = VisibleErrorText(text, limit);
        var lines = 1 + visible.Count(character => character == '\n');
        var height = Math.Max(3, (lines * ErrorLineHeight(fontSize)) + 3);
        reserve = height + pad;
        var y = bottom ? monitor.Y + monitor.Height - pad - height : monitor.Y + pad;
        var bounds = new Rectangle(monitor.X + pad, y, Math.Max(1, monitor.Width - (2 * pad)), height);
        return new ErrorBar(bounds, visible, color, fontSize);
    }

    /// <summary>The height of one line of bar text, in pixels.</summary>
    public static int ErrorLineHeight(int fontSize) => (int)Math.Ceiling(fontSize * 1.35);

    // The first lines of the message, and how many more there are, as the pinned release cuts a long error. A limit of 0
    // leaves only the count.
    private static string VisibleErrorText(string text, int limit)
    {
        var lines = text.Split('\n');
        if (lines.Length <= limit)
            return text;

        var shown = string.Join('\n', lines[..limit]);
        return (shown.Length == 0 ? "" : shown + "\n") + $"({lines.Length - limit} more...)";
    }
}
