using System.Globalization;

namespace Winland;

internal enum AutoDirection
{
    Right,
    Left,
    Up,
    Down,
    CenterRight,
    CenterLeft,
    CenterUp,
    CenterDown,
}

internal enum MonitorRuleKind
{
    Full,
    Disable,
    Reserved,
    Transform,
}

internal readonly record struct MonitorRule(
    string Selector,
    MonitorRuleKind Kind,
    bool Enabled,
    int? X,
    int? Y,
    AutoDirection? Auto,
    int ReservedTop,
    int ReservedBottom,
    int ReservedLeft,
    int ReservedRight,
    bool HasReserved,
    DisplayOrientation? Orientation,
    string? Mode = null,
    double? Scale = null,
    string? ColorPreset = null,
    string? Mirror = null);

internal readonly record struct MonitorArrangement(
    Monitor[] Monitors,
    DisplayOrientationRequest[] Orientations,
    DisplayModeRequest[] Modes,
    DisplayScaleRequest[] Scales,
    DisplayColorRequest[] Colors,
    DisplayMirrorRequest[] Mirrors);

internal static class MonitorRules
{
    public static IReadOnlyList<MonitorRule> From(ConfigDocument? document)
    {
        if (document is null)
            return [];

        var rules = new List<MonitorRule>();
        foreach (var setting in document.Settings)
        {
            if (setting.Keyword != "monitor")
                continue;

            if (TryParse(setting.Value, out var rule, out _))
                rules.Add(rule);
        }

        return rules;
    }

    public static bool TryParse(string value, out MonitorRule rule, out IReadOnlyList<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(value);
        var found = new List<string>();
        warnings = found;
        var parts = Split(value);
        var selector = parts.Count > 0 ? parts[0] : "";
        if (parts.Count >= 2 && IsDisable(parts[1]))
        {
            rule = new MonitorRule(selector, MonitorRuleKind.Disable, false, null, null, null, 0, 0, 0, 0, false, null);
            return true;
        }

        if (parts.Count >= 2 && IsReserved(parts[1]))
            return TryReserved(selector, parts, found, out rule);

        if (parts.Count >= 2 && IsTransform(parts[1]))
            return TryTransformRule(selector, parts, found, out rule);

        int? x = null;
        int? y = null;
        AutoDirection? auto = null;
        if (parts.Count >= 3 && parts[2].Length > 0 && !LooksAutomatic(parts[2]))
        {
            if (!TryPosition(parts[2], out var parsedX, out var parsedY))
            {
                found.Add("Malformed monitor rule.");
                rule = default;
                return false;
            }

            x = parsedX;
            y = parsedY;
        }
        else if (parts.Count >= 3)
        {
            if (!TryDirection(parts[2], out var direction))
                found.Add("Invalid auto direction.");

            auto = direction;
        }

        string? mode = null;
        if (parts.Count >= 2 && parts[1].Length > 0 && !LeavesMode(parts[1]))
        {
            if (DisplaySettings.IsMode(parts[1]))
                mode = parts[1].ToLowerInvariant();
            else
                found.Add("Custom monitor modes are not honored.");
        }

        double? scale = null;
        if (parts.Count >= 4 && parts[3].Length > 0 && !IsAuto(parts[3]))
        {
            if (double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedScale) && parsedScale > 0 && double.IsFinite(parsedScale))
                scale = parsedScale;
            else
                found.Add("Invalid scale.");
        }

        DisplayOrientation? orientation = null;
        string? preset = null;
        string? mirror = null;
        ReadOptions(parts, found, ref orientation, ref preset, ref mirror);
        rule = new MonitorRule(selector, MonitorRuleKind.Full, true, x, y, auto, 0, 0, 0, 0, false, orientation, mode, scale, preset, mirror);
        return true;
    }

    public static MonitorRule? Select(Monitor monitor, IReadOnlyList<MonitorRule> rules)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        MonitorRule? fallback = null;
        MonitorRule? match = null;
        foreach (var rule in rules)
        {
            if (rule.Selector.Length == 0)
            {
                fallback = fallback is null ? rule : Merge(fallback.Value, rule);
                continue;
            }

            if (Matches(monitor, rule.Selector))
                match = match is null ? rule : Merge(match.Value, rule);
        }

        return match ?? fallback;
    }

    public static MonitorArrangement Arrange(IReadOnlyList<Monitor> reported, IReadOnlyList<MonitorRule> rules)
    {
        ArgumentNullException.ThrowIfNull(reported);
        ArgumentNullException.ThrowIfNull(rules);
        var count = reported.Count;
        var result = new Monitor[count];
        var boxes = new LogicalBox[count];
        var placed = new bool[count];
        var pending = new AutoDirection?[count];
        var requests = new List<DisplayOrientationRequest>();
        var modes = new List<DisplayModeRequest>();
        var scales = new List<DisplayScaleRequest>();
        var colors = new List<DisplayColorRequest>();
        var chosen = new MonitorRule?[count];
        for (var i = 0; i < count; i++)
            chosen[i] = Select(reported[i] ?? throw new ArgumentNullException(nameof(reported)), rules);

        var (sources, mirrors) = Mirrors(reported, chosen);

        for (var i = 0; i < count; i++)
        {
            var monitor = reported[i];
            var rule = chosen[i];
            result[i] = Present(monitor, rule);
            if (sources[i] >= 0)
                result[i] = result[i] with { Enabled = false, MirrorOf = reported[sources[i]].Name };

            if (!result[i].Enabled)
                continue;

            if (rule?.Orientation is DisplayOrientation orientation)
                requests.Add(new DisplayOrientationRequest(monitor.Name, orientation));

            if (rule?.Mode is string mode)
                modes.Add(new DisplayModeRequest(monitor.Name, mode));

            if (rule?.ColorPreset is string preset && preset != DisplayColors.Auto)
                colors.Add(new DisplayColorRequest(monitor.Name, preset));

            // A scale Windows already shows asks for nothing, so the display change a new scale causes does not loop.
            if (rule?.Scale is double scale && DisplaySettings.NearestPercent(scale) != DisplaySettings.NearestPercent(monitor.Scale <= 0 ? 1 : monitor.Scale))
                scales.Add(new DisplayScaleRequest(monitor.Name, scale));

            if (rule?.X is int x && rule.Value.Y is int y)
            {
                boxes[i] = Box(x, y, monitor);
                placed[i] = true;
                result[i] = Move(result[i], MonitorPixels.Physical(x, monitor.Scale), MonitorPixels.Physical(y, monitor.Scale));
            }
            else if (rule?.Auto is AutoDirection direction)
            {
                pending[i] = direction;
            }
            else
            {
                boxes[i] = Box(MonitorPixels.Logical(monitor.X, monitor.Scale), MonitorPixels.Logical(monitor.Y, monitor.Scale), monitor);
                placed[i] = true;
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (pending[i] is not AutoDirection direction)
                continue;

            var monitor = reported[i];
            var width = MonitorPixels.Logical(monitor.Width, monitor.Scale);
            var height = MonitorPixels.Logical(monitor.Height, monitor.Scale);
            var (x, y) = Pack(direction, Measure(boxes, placed), width, height);
            boxes[i] = new LogicalBox(x, y, width, height);
            placed[i] = true;
            result[i] = Move(result[i], MonitorPixels.Physical(x, monitor.Scale), MonitorPixels.Physical(y, monitor.Scale));
        }

        // A mirror shows its source's picture, so it sits where its source does.
        for (var i = 0; i < count; i++)
        {
            if (sources[i] >= 0)
                result[i] = Move(result[i], result[sources[i]].X, result[sources[i]].Y);
        }

        return new MonitorArrangement(result, requests.ToArray(), modes.ToArray(), scales.ToArray(), colors.ToArray(), mirrors);
    }

    // Which display each one mirrors, by index, or -1, and the mirrors to ask Windows for. As in the pinned release, a
    // display cannot mirror itself, a mirror, or a display that is off, and a display another one mirrors cannot
    // become a mirror. A display Windows still shows as a mirror stays out of the layout until it is put back.
    private static (int[] Sources, DisplayMirrorRequest[] Requests) Mirrors(IReadOnlyList<Monitor> reported, MonitorRule?[] rules)
    {
        var count = reported.Count;
        var sources = new int[count];
        Array.Fill(sources, -1);
        var requests = new List<DisplayMirrorRequest>();
        for (var i = 0; i < count; i++)
        {
            if (rules[i] is not { Enabled: true, Mirror: { Length: > 0 } name })
                continue;

            var source = Find(reported, rules, candidate => Matches(reported[candidate], name), i);
            if (source < 0 || sources[source] >= 0 || Array.IndexOf(sources, i) >= 0)
                continue;

            sources[i] = source;
            requests.Add(new DisplayMirrorRequest(reported[i].Name, reported[source].Name));
        }

        for (var i = 0; i < count; i++)
        {
            if (sources[i] >= 0 || reported[i].MirrorOf.Length == 0)
                continue;

            var source = Find(reported, rules, candidate => string.Equals(reported[candidate].Name, reported[i].MirrorOf, StringComparison.OrdinalIgnoreCase), i);
            if (source >= 0 && sources[source] < 0)
                sources[i] = source;
        }

        return (sources, requests.ToArray());
    }

    private static int Find(IReadOnlyList<Monitor> reported, MonitorRule?[] rules, Func<int, bool> matches, int self)
    {
        for (var i = 0; i < reported.Count; i++)
        {
            if (i != self && rules[i]?.Enabled != false && matches(i))
                return i;
        }

        return -1;
    }

    private static MonitorRule Merge(MonitorRule prior, MonitorRule next) => next.Kind switch
    {
        MonitorRuleKind.Reserved => prior with
        {
            HasReserved = true,
            ReservedTop = next.ReservedTop,
            ReservedBottom = next.ReservedBottom,
            ReservedLeft = next.ReservedLeft,
            ReservedRight = next.ReservedRight,
        },
        MonitorRuleKind.Transform => next.Orientation is null
            ? prior
            : prior with { Orientation = next.Orientation },
        MonitorRuleKind.Disable => prior with
        {
            Enabled = false,
            X = null,
            Y = null,
            Auto = null,
        },
        _ => next with
        {
            HasReserved = next.HasReserved || prior.HasReserved,
            ReservedTop = next.HasReserved ? next.ReservedTop : prior.ReservedTop,
            ReservedBottom = next.HasReserved ? next.ReservedBottom : prior.ReservedBottom,
            ReservedLeft = next.HasReserved ? next.ReservedLeft : prior.ReservedLeft,
            ReservedRight = next.HasReserved ? next.ReservedRight : prior.ReservedRight,
            Orientation = next.Orientation ?? prior.Orientation,
            Mode = next.Mode ?? prior.Mode,
            Scale = next.Scale ?? prior.Scale,
            ColorPreset = next.ColorPreset ?? prior.ColorPreset,
            Mirror = next.Mirror ?? prior.Mirror,
        },
    };

    private static bool TryReserved(string selector, List<string> parts, List<string> warnings, out MonitorRule rule)
    {
        if (parts.Count != 6
            || !TryInset(parts[2], out var top)
            || !TryInset(parts[3], out var bottom)
            || !TryInset(parts[4], out var left)
            || !TryInset(parts[5], out var right))
        {
            warnings.Add("Malformed monitor rule.");
            rule = default;
            return false;
        }

        rule = new MonitorRule(
            selector,
            MonitorRuleKind.Reserved,
            true,
            null,
            null,
            null,
            top,
            bottom,
            left,
            right,
            true,
            null);
        return true;
    }

    private static bool TryTransformRule(string selector, List<string> parts, List<string> warnings, out MonitorRule rule)
    {
        if (parts.Count < 3)
        {
            warnings.Add("Malformed monitor rule.");
            rule = default;
            return false;
        }

        var orientation = ParseTransform(parts[2], warnings);
        if (parts.Count != 3)
            warnings.Add("Malformed monitor rule.");

        if (orientation is null)
        {
            rule = default;
            return false;
        }

        rule = new MonitorRule(selector, MonitorRuleKind.Transform, true, null, null, null, 0, 0, 0, 0, false, orientation);
        return true;
    }

    private static void ReadOptions(List<string> parts, List<string> warnings, ref DisplayOrientation? orientation, ref string? preset, ref string? mirror)
    {
        for (var i = 4; i < parts.Count; i++)
        {
            var key = parts[i];
            if (key.Length == 0)
                continue;

            if (i + 1 >= parts.Count)
            {
                warnings.Add("Malformed monitor rule.");
                return;
            }

            var option = parts[++i];
            if (IsTransform(key))
                orientation = ParseTransform(option, warnings) ?? orientation;
            else if (key.Equals("mirror", StringComparison.OrdinalIgnoreCase))
                mirror = option;
            else if (key.Equals("bitdepth", StringComparison.OrdinalIgnoreCase))
                warnings.Add("Changing a monitor's bit depth is not honored.");
            else if (key.Equals("vrr", StringComparison.OrdinalIgnoreCase))
                warnings.Add("Variable refresh rate is not honored.");
            else if (key.Equals("cm", StringComparison.OrdinalIgnoreCase) && DisplayColors.IsPreset(option))
                preset = option.ToLowerInvariant();
            else if (key.Equals("cm", StringComparison.OrdinalIgnoreCase))
                warnings.Add("Invalid color management preset.");
            else if (IsColor(key))
                warnings.Add("Monitor color management other than its cm preset is not honored.");
            else
                warnings.Add("Malformed monitor rule.");
        }
    }

    private static DisplayOrientation? ParseTransform(string text, List<string> warnings)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value is < 0 or > 7)
        {
            warnings.Add("Invalid monitor transform.");
            return null;
        }

        if (value > 3)
        {
            warnings.Add("Flipped monitor transforms are not honored.");
            return null;
        }

        return (DisplayOrientation)value;
    }

    private static Monitor Present(Monitor reported, MonitorRule? rule)
    {
        if (rule is null)
            return reported;

        return reported with
        {
            Enabled = rule.Value.Enabled,
            WorkArea = Inset(reported.WorkArea, rule.Value, reported.Scale),
            Orientation = rule.Value.Orientation,
            ColorPreset = rule.Value.ColorPreset ?? "",
        };
    }

    private static Rectangle Inset(Rectangle work, MonitorRule rule, double scale)
    {
        if (!rule.HasReserved)
            return work;

        var top = MonitorPixels.Physical(rule.ReservedTop, scale);
        var bottom = MonitorPixels.Physical(rule.ReservedBottom, scale);
        var left = MonitorPixels.Physical(rule.ReservedLeft, scale);
        var right = MonitorPixels.Physical(rule.ReservedRight, scale);
        return new Rectangle(
            work.X + left,
            work.Y + top,
            Math.Max(0, work.Width - left - right),
            Math.Max(0, work.Height - top - bottom));
    }

    private static Monitor Move(Monitor monitor, int x, int y)
    {
        var work = monitor.WorkArea;
        return monitor with
        {
            X = x,
            Y = y,
            WorkArea = new Rectangle(work.X + (x - monitor.X), work.Y + (y - monitor.Y), work.Width, work.Height),
        };
    }

    private static LogicalBox Box(int x, int y, Monitor monitor) =>
        new(x, y, MonitorPixels.Logical(monitor.Width, monitor.Scale), MonitorPixels.Logical(monitor.Height, monitor.Scale));

    private static Extents Measure(LogicalBox[] boxes, bool[] placed)
    {
        var left = 0;
        var right = 0;
        var up = 0;
        var down = 0;
        for (var i = 0; i < boxes.Length; i++)
        {
            if (!placed[i])
                continue;

            var box = boxes[i];
            right = Math.Max(right, box.X + box.Width);
            left = Math.Min(left, box.X);
            down = Math.Max(down, box.Y + box.Height);
            up = Math.Min(up, box.Y);
        }

        return new Extents(left, right, up, down);
    }

    private static (int X, int Y) Pack(AutoDirection direction, Extents extents, int width, int height) => direction switch
    {
        AutoDirection.Up => (0, extents.Up - height),
        AutoDirection.Down => (0, extents.Down),
        AutoDirection.Left => (extents.Left - width, 0),
        AutoDirection.Right => (extents.Right, 0),
        AutoDirection.CenterUp => (extents.Left + ((extents.Right - extents.Left - width) / 2), extents.Up - height),
        AutoDirection.CenterDown => (extents.Left + ((extents.Right - extents.Left - width) / 2), extents.Down),
        AutoDirection.CenterLeft => (extents.Left - width, extents.Up + ((extents.Down - extents.Up - height) / 2)),
        AutoDirection.CenterRight => (extents.Right, extents.Up + ((extents.Down - extents.Up - height) / 2)),
        _ => (extents.Right, 0),
    };

    internal static bool Matches(Monitor monitor, string selector)
    {
        const string Prefix = "desc:";
        if (selector.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var description = selector[Prefix.Length..].Trim();
            return monitor.Description.StartsWith(description, StringComparison.Ordinal);
        }

        return string.Equals(monitor.Name, selector.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> Split(string value)
    {
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != ',')
                continue;

            parts.Add(value[start..i].Trim());
            start = i + 1;
        }

        parts.Add(value[start..].Trim());
        return parts;
    }

    private static bool TryPosition(string text, out int x, out int y)
    {
        x = 0;
        y = 0;
        var split = text.IndexOf('x', StringComparison.OrdinalIgnoreCase);
        if (split <= 0 || split == text.Length - 1)
            return false;

        return int.TryParse(text[..split], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
            && int.TryParse(text[(split + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
    }

    private static bool TryInset(string text, out int value)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return false;

        if (value < 0)
            value = 0;

        return true;
    }

    private static bool TryDirection(string text, out AutoDirection direction)
    {
        if (text.Length == 0
            || text.Equals("auto", StringComparison.OrdinalIgnoreCase)
            || text.Equals("auto-right", StringComparison.OrdinalIgnoreCase))
        {
            direction = AutoDirection.Right;
            return true;
        }

        if (text.Equals("auto-left", StringComparison.OrdinalIgnoreCase))
        {
            direction = AutoDirection.Left;
            return true;
        }

        if (text.Equals("auto-up", StringComparison.OrdinalIgnoreCase))
        {
            direction = AutoDirection.Up;
            return true;
        }

        if (text.Equals("auto-down", StringComparison.OrdinalIgnoreCase))
        {
            direction = AutoDirection.Down;
            return true;
        }

        if (text.Equals("auto-center-right", StringComparison.OrdinalIgnoreCase))
        {
            direction = AutoDirection.CenterRight;
            return true;
        }

        if (text.Equals("auto-center-left", StringComparison.OrdinalIgnoreCase))
        {
            direction = AutoDirection.CenterLeft;
            return true;
        }

        if (text.Equals("auto-center-up", StringComparison.OrdinalIgnoreCase))
        {
            direction = AutoDirection.CenterUp;
            return true;
        }

        if (text.Equals("auto-center-down", StringComparison.OrdinalIgnoreCase))
        {
            direction = AutoDirection.CenterDown;
            return true;
        }

        direction = AutoDirection.Right;
        return false;
    }

    private static bool IsDisable(string text) =>
        text.Equals("disable", StringComparison.OrdinalIgnoreCase)
        || text.Equals("disabled", StringComparison.OrdinalIgnoreCase);

    private static bool IsReserved(string text) =>
        text.Equals("addreserved", StringComparison.OrdinalIgnoreCase)
        || text.Equals("reserved_area", StringComparison.OrdinalIgnoreCase);

    private static bool IsTransform(string text) =>
        text.Equals("transform", StringComparison.OrdinalIgnoreCase);

    private static bool IsColor(string text) =>
        text.Equals("icc", StringComparison.OrdinalIgnoreCase)
        || text.Equals("sdrbrightness", StringComparison.OrdinalIgnoreCase)
        || text.Equals("sdrsaturation", StringComparison.OrdinalIgnoreCase)
        || text.Equals("sdr_eotf", StringComparison.OrdinalIgnoreCase);

    private static bool LeavesMode(string text) =>
        text.Length == 0 || text.StartsWith("pref", StringComparison.OrdinalIgnoreCase);

    private static bool IsAuto(string text) =>
        text.Equals("auto", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("auto-", StringComparison.OrdinalIgnoreCase);

    private static bool LooksAutomatic(string text) =>
        text.Equals("auto", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("auto-", StringComparison.OrdinalIgnoreCase);

    private readonly record struct LogicalBox(int X, int Y, int Width, int Height);

    private readonly record struct Extents(int Left, int Right, int Up, int Down);
}
