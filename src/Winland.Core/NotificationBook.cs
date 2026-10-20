using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winland;

/// <summary>Where a notification is drawn: the focused Monitor when it was asked for, or a 1920×1080 stand-in.</summary>
public readonly record struct NotificationSpot(Rectangle Monitor, Rectangle WorkArea, double Scale, string Font)
{
    public static NotificationSpot Fallback { get; } = new(
        new Rectangle(0, 0, 1920, 1080),
        new Rectangle(0, 0, 1920, 1080),
        1,
        FontNames.Default);
}

/// <summary>
/// One notification as <c>hl.notification</c> left it, so a config that created it before Winland ran can show the same
/// one afterwards. <see cref="Gone"/> is one whose time already ran out.
/// </summary>
public sealed record NotificationRestore(
    long Id,
    string Text,
    double Timeout,
    long Color,
    int Icon,
    double Font,
    double Elapsed,
    double Since,
    int Locks,
    bool Gone);

/// <summary>A notification color, read as the pinned release reads one: a number, or the strings its config accepts.</summary>
public static class NotificationColor
{
    public static long Bits(BorderColor color) =>
        ((long)color.A << 24) | ((long)color.R << 16) | ((long)color.G << 8) | color.B;

    public static BorderColor FromBits(long bits)
    {
        var value = unchecked((uint)bits);
        return new BorderColor((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    /// <summary>A finite number, truncated toward zero, as the low 32 bits of that integer. A non-finite number is not a color.</summary>
    public static bool TryNumber(double value, out uint bits)
    {
        bits = 0;
        if (!double.IsFinite(value))
            return false;

        var truncated = Math.Truncate(value);
        if (truncated is < long.MinValue or > long.MaxValue)
            return false;

        bits = unchecked((uint)(long)truncated);
        return true;
    }

    public static bool TryParse(string text, out uint bits)
    {
        bits = 0;
        if (text.Length == 0)
            return false;

        if (text[0] == '#')
            return TryHash(text.AsSpan(1), out bits);

        if (text.StartsWith("0x", StringComparison.Ordinal))
            return TryPrefixedHex(text.AsSpan(2), out bits);

        if (text.StartsWith("rgba(", StringComparison.Ordinal) && text[^1] == ')')
            return TryRgba(text[5..^1].Trim(), out bits);

        if (text.StartsWith("rgb(", StringComparison.Ordinal) && text[^1] == ')')
            return TryRgb(text[4..^1].Trim(), out bits);

        if (!AllDigits(text) || !uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out bits))
            return false;

        return true;
    }

    private static bool TryHash(ReadOnlySpan<char> hex, out uint bits)
    {
        bits = 0;
        if (hex.Length == 3 && TryNibbles(hex, out var r, out var g, out var b))
        {
            bits = 0xFF000000 | (r << 16) | (g << 8) | b;
            return true;
        }

        if (hex.Length == 6 && TryByte(hex[..2], out r) && TryByte(hex[2..4], out g) && TryByte(hex[4..], out b))
        {
            bits = 0xFF000000 | (r << 16) | (g << 8) | b;
            return true;
        }

        if (hex.Length == 8
            && TryByte(hex[..2], out r)
            && TryByte(hex[2..4], out g)
            && TryByte(hex[4..6], out b)
            && TryByte(hex[6..], out var a))
        {
            bits = (a << 24) | (r << 16) | (g << 8) | b;
            return true;
        }

        return false;
    }

    private static bool TryNibbles(ReadOnlySpan<char> hex, out uint r, out uint g, out uint b)
    {
        r = g = b = 0;
        if (!TryHex(hex[..1], out var red) || !TryHex(hex.Slice(1, 1), out var green) || !TryHex(hex.Slice(2, 1), out var blue))
            return false;

        r = (uint)(red | (red << 4));
        g = (uint)(green | (green << 4));
        b = (uint)(blue | (blue << 4));
        return true;
    }

    private static bool TryPrefixedHex(ReadOnlySpan<char> hex, out uint bits)
    {
        bits = 0;
        if (!TryHex(hex, out var value))
            return false;

        bits = unchecked((uint)value);
        return true;
    }

    private static bool TryRgb(string body, out uint bits)
    {
        bits = 0;
        if (Count(body, ',') == 2)
        {
            var parts = body.Split(',');
            if (!TryChannel(parts[0], out var r) || !TryChannel(parts[1], out var g) || !TryChannel(parts[2], out var b))
                return false;

            bits = 0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | b;
            return true;
        }

        if (body.Length == 6 && TryHex(body, out var hex))
        {
            bits = 0xFF000000 | unchecked((uint)hex);
            return true;
        }

        return false;
    }

    private static bool TryRgba(string body, out uint bits)
    {
        bits = 0;
        if (Count(body, ',') == 3)
        {
            var parts = body.Split(',');
            if (!TryChannel(parts[0], out var r)
                || !TryChannel(parts[1], out var g)
                || !TryChannel(parts[2], out var b)
                || !double.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var alpha))
                return false;

            var packed = ((long)Math.Floor(alpha * 255.0) << 24) | ((long)r << 16) | ((long)g << 8) | b;
            bits = unchecked((uint)packed);
            return true;
        }

        if (body.Length == 8 && TryHex(body, out var rgba))
        {
            bits = unchecked((uint)((rgba >> 8) + (0x1000000 * (rgba & 0xFF))));
            return true;
        }

        return false;
    }

    private static bool TryChannel(string text, out byte value) =>
        byte.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryByte(ReadOnlySpan<char> text, out uint value)
    {
        value = 0;
        if (!TryHex(text, out var parsed) || parsed > byte.MaxValue)
            return false;

        value = (uint)parsed;
        return true;
    }

    private static bool TryHex(ReadOnlySpan<char> text, out ulong value) =>
        ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);

    private static bool AllDigits(string text)
    {
        if (text.Length == 0)
            return false;

        foreach (var character in text)
        {
            if (!char.IsAsciiDigit(character))
                return false;
        }

        return true;
    }

    private static int Count(string text, char character)
    {
        var count = 0;
        foreach (var item in text)
        {
            if (item == character)
                count++;
        }

        return count;
    }
}

/// <summary>
/// The notifications on screen. Time is a stopwatch, so a test can move it. A pause keeps the time already spent, and
/// a notification is gone once it is not paused and the clock is past the moment it started plus its whole milliseconds.
/// </summary>
public sealed class NotificationBook
{
    private const string Invalid = "error: invalid notification";

    private readonly List<Entry> _entries = [];
    private Func<long> _now = Stopwatch.GetTimestamp;
    private long _next = 1;

    public void UseClock(Func<long> now)
    {
        ArgumentNullException.ThrowIfNull(now);
        _now = now;
    }

    /// <summary>The notifications a config created before it was running, gone ones included so their ids can be forgotten.</summary>
    public IReadOnlyList<NotificationRestore> Snapshot()
    {
        var found = new List<NotificationRestore>(_entries.Count);
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (!_entries[i].Gone(_now))
                continue;

            found.Add(_entries[i].Restore(_now, gone: true));
            _entries.RemoveAt(i);
        }

        foreach (var entry in _entries)
            found.Add(entry.Restore(_now, gone: false));

        return found;
    }

    public IReadOnlyList<NotificationAction> Notify(
        string text,
        BorderColor color,
        double timeMs,
        int icon,
        double fontSize,
        NotificationSpot spot)
    {
        var actions = new List<NotificationAction>();
        Sweep(actions);
        var entry = Add(text, timeMs, color, icon, fontSize, resolveZero: false);
        actions.Add(entry.Added(spot, _now));
        return actions;
    }

    public IReadOnlyList<NotificationAction> DismissOldest(int amount)
    {
        var actions = new List<NotificationAction>();
        Sweep(actions);
        if (amount < 0)
            _entries.Clear();
        else if (amount > 0)
            _entries.RemoveRange(0, Math.Min(amount, _entries.Count));

        actions.Add(new NotificationsDismissed(amount));
        return actions;
    }

    public (string Reply, IReadOnlyList<NotificationAction> Actions) Execute(string json, NotificationSpot spot)
    {
        var actions = new List<NotificationAction>();
        Sweep(actions);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("op", out var opElement)
                || opElement.ValueKind != JsonValueKind.String)
                return (Invalid, actions);

            var reply = opElement.GetString() switch
            {
                "create" => Create(root, spot, actions),
                "restore" => Restore(root, spot, actions),
                "get" => JsonSerializer.Serialize(_entries.ConvertAll(entry => entry.View(_now))),
                "query" => Find(root) is Entry found ? JsonSerializer.Serialize(found.View(_now)) : "null",
                "text" => Change(root, actions, entry =>
                {
                    entry.Text = TextOf(root);
                    return true;
                }),
                "timeout" => Change(root, actions, entry =>
                {
                    if (!TryDouble(root, "timeout", out var timeout) || timeout < 0)
                        return false;

                    entry.ResetTimeout(timeout, _now);
                    return true;
                }),
                "color" => Change(root, actions, entry =>
                {
                    if (!TryLong(root, "color", out var color))
                        return false;

                    entry.Color = NotificationColor.FromBits(color);
                    return true;
                }),
                "icon" => Change(root, actions, entry =>
                {
                    if (!TryInt(root, "icon", out var icon) || icon is < 0 or > NotificationFrames.NoIcon)
                        return false;

                    entry.Icon = icon;
                    return true;
                }),
                "font" => Change(root, actions, entry =>
                {
                    if (!TryDouble(root, "font", out var font) || font <= 0)
                        return false;

                    entry.FontSize = font;
                    return true;
                }),
                "dismiss" => Dismiss(root, actions),
                "pause" => Change(root, actions, entry =>
                {
                    entry.Pause(_now);
                    return true;
                }),
                "resume" => Change(root, actions, entry =>
                {
                    entry.Resume(_now);
                    return true;
                }),
                _ => Invalid,
            };
            return (reply, actions);
        }
        catch (JsonException)
        {
            return (Invalid, actions);
        }
    }

    private string Create(JsonElement root, NotificationSpot spot, List<NotificationAction> actions)
    {
        if (!TryDouble(root, "timeout", out var timeout) || timeout < 0)
            return Invalid;

        var icon = TryInt(root, "icon", out var parsed) ? parsed : NotificationFrames.NoIcon;
        if (icon is < 0 or > NotificationFrames.NoIcon)
            return Invalid;

        var color = TryLong(root, "color", out var bits) ? NotificationColor.FromBits(bits) : default;
        var font = TryDouble(root, "font", out var parsedFont) ? parsedFont : 13;
        if (font <= 0)
            return Invalid;

        var entry = Add(TextOf(root), timeout < 1 ? 1 : timeout, color, icon, font, resolveZero: true);
        actions.Add(entry.Added(spot, _now));
        return JsonSerializer.Serialize(new { id = entry.Id });
    }

    private string Restore(JsonElement root, NotificationSpot spot, List<NotificationAction> actions)
    {
        if (!TryLong(root, "id", out var id)
            || !TryDouble(root, "timeout", out var timeout)
            || timeout < 0
            || !TryInt(root, "icon", out var icon)
            || icon is < 0 or > NotificationFrames.NoIcon
            || !TryLong(root, "color", out var color)
            || !TryDouble(root, "font", out var font)
            || font <= 0
            || !TryDouble(root, "elapsed", out var elapsed)
            || !TryDouble(root, "since", out var since)
            || !TryInt(root, "locks", out var locks))
            return Invalid;

        if (locks <= 0 && !double.IsNaN(timeout) && elapsed > Math.Truncate(timeout))
            return "null";

        var entry = new Entry
        {
            Id = Allocate(id),
            Text = TextOf(root),
            Color = NotificationColor.FromBits(color),
            TimeMs = timeout,
            Icon = icon,
            FontSize = font,
            Locks = Math.Max(locks, 0),
            LastPauseElapsed = elapsed,
        };
        var now = _now();
        entry.CreatedAt = now - Entry.Ticks(since);
        entry.StartedAt = now - Entry.Ticks(elapsed);
        _entries.Add(entry);
        actions.Add(entry.Added(spot, _now));
        return JsonSerializer.Serialize(new { id = entry.Id });
    }

    private string Change(JsonElement root, List<NotificationAction> actions, Func<Entry, bool> change)
    {
        if (Find(root) is not Entry entry)
            return "null";

        if (!change(entry))
            return Invalid;

        actions.Add(entry.Changed(_now));
        return "true";
    }

    private string Dismiss(JsonElement root, List<NotificationAction> actions)
    {
        if (Find(root) is not Entry entry)
            return "null";

        _entries.Remove(entry);
        actions.Add(new NotificationDismissed(entry.Id));
        return "true";
    }

    private Entry Add(string text, double timeMs, BorderColor color, int icon, double fontSize, bool resolveZero)
    {
        if (resolveZero && color == default)
            color = NotificationFrames.IconColor(icon);

        var now = _now();
        var entry = new Entry
        {
            Id = _next++,
            Text = text,
            Color = color,
            TimeMs = timeMs,
            Icon = icon,
            FontSize = fontSize,
            CreatedAt = now,
            StartedAt = now,
        };
        _entries.Add(entry);
        return entry;
    }

    private long Allocate(long requested)
    {
        if (requested > 0 && !_entries.Exists(entry => entry.Id == requested))
        {
            if (requested >= _next)
                _next = requested + 1;

            return requested;
        }

        while (_entries.Exists(entry => entry.Id == _next))
            _next++;

        return _next++;
    }

    private Entry? Find(JsonElement root) =>
        TryLong(root, "id", out var id) ? _entries.Find(entry => entry.Id == id) : null;

    private void Sweep(List<NotificationAction> actions)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (!_entries[i].Gone(_now))
                continue;

            actions.Add(new NotificationDismissed(_entries[i].Id));
            _entries.RemoveAt(i);
        }
    }

    private static string TextOf(JsonElement root) =>
        root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() ?? "" : "";

    private static bool TryDouble(JsonElement root, string name, out double value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value);
    }

    private static bool TryLong(JsonElement root, string name, out long value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element) && element.TryGetInt64(out value);
    }

    private static bool TryInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element) && element.TryGetInt32(out value);
    }

    private sealed class Entry
    {
        public long Id;
        public string Text = "";
        public BorderColor Color;
        public double TimeMs;
        public int Icon;
        public double FontSize;
        public long CreatedAt;
        public long StartedAt;
        public double LastPauseElapsed;
        public int Locks;

        public Notification Notification() => new(Text, Color, TimeMs, Icon, FontSize, Id);

        public NotificationAdded Added(NotificationSpot spot, Func<long> clock) => new(
            Notification(),
            spot.Monitor,
            spot.WorkArea,
            spot.Scale,
            spot.Font,
            ElapsedMs(clock),
            SinceMs(clock),
            Locks > 0);

        public NotificationChanged Changed(Func<long> clock) =>
            new(Id, Text, Color, TimeMs, Icon, FontSize, Locks > 0, ElapsedMs(clock));

        public NotificationView View(Func<long> clock) => new(
            Id,
            Text,
            TimeMs,
            NotificationColor.Bits(Color),
            Icon,
            FontSize,
            Whole(ElapsedMs(clock)),
            Whole(SinceMs(clock)),
            Locks > 0);

        public NotificationRestore Restore(Func<long> clock, bool gone) => new(
            Id,
            Text,
            TimeMs,
            NotificationColor.Bits(Color),
            Icon,
            FontSize,
            ElapsedMs(clock),
            SinceMs(clock),
            Locks,
            gone);

        // As in the pinned release, the lock count rises before the elapsed time is read, so the read sees the frozen
        // value: 0 for a first pause, and what the last pause kept after that. A resume then starts the timeout again
        // from that value.
        public void Pause(Func<long> clock)
        {
            Locks++;
            if (Locks == 1)
                LastPauseElapsed = ElapsedMs(clock);
        }

        public void Resume(Func<long> clock)
        {
            if (Locks == 0)
                return;

            Locks--;
            if (Locks == 0)
                StartedAt = clock() - Ticks(LastPauseElapsed);
        }

        public void ResetTimeout(double timeMs, Func<long> clock)
        {
            TimeMs = timeMs;
            StartedAt = clock();
        }

        public bool Gone(Func<long> clock) => Locks == 0 && !double.IsNaN(TimeMs) && clock() > StartedAt + Ticks(TimeMs);

        private double ElapsedMs(Func<long> clock) => Locks > 0 ? LastPauseElapsed : Milliseconds(clock() - StartedAt);

        private double SinceMs(Func<long> clock) => Milliseconds(clock() - CreatedAt);

        public static long Ticks(double milliseconds)
        {
            if (double.IsNaN(milliseconds) || milliseconds <= 0)
                return 0;

            return (long)(Math.Truncate(milliseconds) * Stopwatch.Frequency / 1000.0);
        }

        private static double Milliseconds(long ticks)
        {
            var milliseconds = ticks * 1000.0 / Stopwatch.Frequency;
            return milliseconds >= 0 ? Math.Floor(milliseconds) : Math.Ceiling(milliseconds);
        }

        private static long Whole(double milliseconds) => (long)Math.Truncate(milliseconds);
    }

    private sealed record NotificationView(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("timeout")] double Timeout,
        [property: JsonPropertyName("color")] long Color,
        [property: JsonPropertyName("icon")] int Icon,
        [property: JsonPropertyName("font")] double Font,
        [property: JsonPropertyName("elapsed")] long Elapsed,
        [property: JsonPropertyName("since")] long Since,
        [property: JsonPropertyName("paused")] bool Paused);
}
