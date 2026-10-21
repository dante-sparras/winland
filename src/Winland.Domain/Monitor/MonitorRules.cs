using System.Globalization;

namespace Winland.Domain;

internal static class MonitorRules
{
    public const string NotHonored = "Monitor settings other than a reserved area are not honored.";

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
        if (parts.Count <= 1)
        {
            rule = new MonitorRule(selector, 0, 0, 0, 0, false);
            return true;
        }

        if (IsReserved(parts[1]))
            return TryReserved(selector, parts, found, out rule);

        found.Add(NotHonored);
        rule = default;
        return false;
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

    public static Monitor[] Arrange(IReadOnlyList<Monitor> reported, IReadOnlyList<MonitorRule> rules)
    {
        ArgumentNullException.ThrowIfNull(reported);
        ArgumentNullException.ThrowIfNull(rules);
        var result = new Monitor[reported.Count];
        for (var i = 0; i < reported.Count; i++)
            result[i] = Present(reported[i], Select(reported[i], rules));

        return result;
    }

    // A later reserved area replaces an earlier one. A name with no reserved area keeps the one already chosen.
    private static MonitorRule Merge(MonitorRule prior, MonitorRule next) => next.HasReserved ? next : prior;

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

        rule = new MonitorRule(selector, top, bottom, left, right, true);
        return true;
    }

    private static Monitor Present(Monitor reported, MonitorRule? rule)
    {
        if (rule is not { HasReserved: true } chosen)
            return reported;

        return reported with { WorkArea = Inset(reported.WorkArea, chosen, reported.Scale) };
    }

    private static Rectangle Inset(Rectangle work, MonitorRule rule, double scale)
    {
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

    private static bool TryInset(string text, out int value)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return false;

        if (value < 0)
            value = 0;

        return true;
    }

    private static bool IsReserved(string text) =>
        text.Equals("addreserved", StringComparison.OrdinalIgnoreCase)
        || text.Equals("reserved_area", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A <c>monitor</c> line that only reserves space. A name with no reserved area matches and changes nothing.
/// Mode, position, scale, transform, mirror, color, and <c>disable</c> are not rules.
/// </summary>
internal readonly record struct MonitorRule(
    string Selector,
    int ReservedTop,
    int ReservedBottom,
    int ReservedLeft,
    int ReservedRight,
    bool HasReserved);
