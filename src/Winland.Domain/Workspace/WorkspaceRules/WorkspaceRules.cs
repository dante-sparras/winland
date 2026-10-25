using System.Globalization;

namespace Winland.Domain;

internal static partial class WorkspaceRules
{
    internal static void ReplaceOrAdd(List<WorkspaceRule> rules, WorkspaceRule rule)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(rule);

        for (var i = 0; i < rules.Count; i++)
        {
            if (rules[i].WorkspaceString == rule.WorkspaceString)
            {
                rules[i] = rules[i].Merge(rule);
                return;
            }
        }

        rules.Add(rule);
    }

    internal static WorkspaceRule? Parse(string value, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(warnings);
        var comma = value.IndexOf(',');
        var ident = (comma < 0 ? value : value[..comma]).Trim();
        if (ident.Length == 0)
        {
            warnings.Add("Malformed workspace rule.");
            return null;
        }

        // A workspace rule names a Workspace by its number.
        if (!IsPositive(ident, out _))
        {
            warnings.Add($"A workspace rule names a Workspace by its number, not '{ident}'.");
            return null;
        }

        var rule = new WorkspaceRule { WorkspaceString = ident };
        if (comma < 0)
            return rule;

        var fatal = false;
        foreach (var piece in value[(comma + 1)..].Split(','))
        {
            var field = piece.Trim();
            if (field.Length == 0)
                continue;

            if (!Apply(rule, field, warnings, out rule, out var failed))
                fatal = true;
            else if (failed)
                fatal = true;
        }

        return fatal ? null : rule;
    }

    internal static WorkspaceRule? For(IReadOnlyList<WorkspaceRule> rules, WorkspaceId id, Monitor? monitor)
    {
        ArgumentNullException.ThrowIfNull(rules);
        WorkspaceRule? merged = null;
        foreach (var rule in rules)
        {
            if (!id.TryGetNumber(out var number) || !IsPositive(rule.WorkspaceString, out var wanted) || number != wanted)
                continue;

            if (rule.Monitor.Length > 0 && (monitor is null || !MonitorRules.Matches(monitor, rule.Monitor)))
                continue;

            merged = merged is null ? rule : merged.Merge(rule);
        }

        return merged;
    }

    internal static string? DefaultWorkspace(IReadOnlyList<WorkspaceRule> rules, Monitor monitor)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(monitor);
        foreach (var rule in rules)
        {
            if (rule.Default != true)
                continue;

            if (rule.Monitor.Length > 0 && !MonitorRules.Matches(monitor, rule.Monitor))
                continue;

            return rule.WorkspaceString;
        }

        return null;
    }

    internal static bool TryConcrete(string text, MonitorId monitor, out WorkspaceId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        if (!IsPositive(text, out var number))
            return false;

        id = WorkspaceId.FromNumber(number, monitor);
        return true;
    }

    private static bool Apply(WorkspaceRule rule, string field, List<string> warnings, out WorkspaceRule updated, out bool failed)
    {
        updated = rule;
        failed = false;
        if (Take(field, "gapsin:", out var gapsIn))
            return TryGaps(rule, gapsIn, warnings, static (current, gaps) => current with { GapsIn = gaps }, out updated, out failed);

        if (Take(field, "gapsout:", out var gapsOut))
            return TryGaps(rule, gapsOut, warnings, static (current, gaps) => current with { GapsOut = gaps }, out updated, out failed);

        if (Take(field, "monitor:", out var monitor))
        {
            updated = rule with { Monitor = monitor.Trim() };
            return true;
        }

        if (Take(field, "default:", out var defaultValue))
            return TryFlag(rule, defaultValue, warnings, static (current, value) => current with { Default = value }, out updated, out failed);

        if (Take(field, "persistent:", out var persistent))
            return TryFlag(rule, persistent, warnings, static (current, value) => current with { Persistent = value }, out updated, out failed);

        if (Take(field, "layout:", out var layout))
        {
            var name = layout.Trim();
            if (LayoutFactory.Fallback(name).Warning is string unknown)
                warnings.Add(unknown);

            updated = rule with { Layout = name };
            return true;
        }

        // A field Winland does not read warns, and the rest of the rule still applies.
        var colon = field.IndexOf(':', StringComparison.Ordinal);
        warnings.Add($"Unsupported workspace rule field '{(colon < 0 ? field : field[..colon])}'.");
        return true;
    }

    private static bool TryGaps(
        WorkspaceRule rule,
        string text,
        List<string> warnings,
        Func<WorkspaceRule, EdgeInsets, WorkspaceRule> apply,
        out WorkspaceRule updated,
        out bool failed)
    {
        updated = rule;
        failed = false;
        var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !TryInset(parts, out var gaps))
        {
            warnings.Add("Error parsing workspace rule gaps: " + text.Trim());
            failed = true;
            return false;
        }

        if (parts.Length > 4)
            warnings.Add("Too many arguments provided for gaps.");

        updated = apply(rule, gaps);
        return true;
    }

    private static bool TryInset(string[] parts, out EdgeInsets gaps)
    {
        gaps = EdgeInsets.Zero;
        var count = Math.Min(parts.Length, 4);
        var values = new int[count];
        for (var i = 0; i < count; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                return false;
        }

        gaps = count switch
        {
            1 => EdgeInsets.Uniform(values[0]),
            2 => new EdgeInsets(values[0], values[1], values[0], values[1]),
            3 => new EdgeInsets(values[0], values[1], values[2], values[1]),
            _ => new EdgeInsets(values[0], values[1], values[2], values[3]),
        };
        return true;
    }

    private static bool TryFlag(
        WorkspaceRule rule,
        string text,
        List<string> warnings,
        Func<WorkspaceRule, bool, WorkspaceRule> apply,
        out WorkspaceRule updated,
        out bool failed)
    {
        updated = rule;
        failed = false;
        if (!TryBool(text, out var value))
        {
            warnings.Add("Failed parsing a workspace rule");
            failed = true;
            return false;
        }

        updated = apply(rule, value);
        return true;
    }

    private static bool TryBool(string text, out bool value)
    {
        var trimmed = text.Trim();
        if (Starts(trimmed, "true") || Starts(trimmed, "on") || Starts(trimmed, "yes"))
        {
            value = true;
            return true;
        }

        if (Starts(trimmed, "false") || Starts(trimmed, "off") || Starts(trimmed, "no"))
        {
            value = false;
            return true;
        }

        if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            value = false;
            return false;
        }

        value = number != 0;
        return true;
    }

    private static bool Take(string field, string prefix, out string value)
    {
        if (field.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = field[prefix.Length..];
            return true;
        }

        value = "";
        return false;
    }

    private static bool Starts(string text, string word) =>
        text.StartsWith(word, StringComparison.OrdinalIgnoreCase);

    private static bool IsPositive(string text, out int number)
    {
        number = 0;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number)
            && number > 0
            && number.ToString(CultureInfo.InvariantCulture) == text;
    }
}
