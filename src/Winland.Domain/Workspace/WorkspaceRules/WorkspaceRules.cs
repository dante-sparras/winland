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
            if (rules[i].WorkspaceString == rule.WorkspaceString && rules[i].Monitor == rule.Monitor)
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

            if (!Apply(rule, field, warnings, out rule))
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

    private static bool Apply(WorkspaceRule rule, string field, List<string> warnings, out WorkspaceRule updated)
    {
        updated = rule;
        if (Take(field, "gapsin:", out var gapsIn))
            return TryGaps(rule, gapsIn, warnings, static (current, gaps) => current with { GapsIn = gaps }, out updated);

        if (Take(field, "gapsout:", out var gapsOut))
            return TryGaps(rule, gapsOut, warnings, static (current, gaps) => current with { GapsOut = gaps }, out updated);

        if (Take(field, "monitor:", out var monitor))
        {
            updated = rule with { Monitor = monitor.Trim() };
            return true;
        }

        if (Take(field, "default:", out var defaultValue))
            return TryFlag(rule, defaultValue, warnings, static (current, value) => current with { Default = value }, out updated);

        if (Take(field, "persistent:", out var persistent))
            return TryFlag(rule, persistent, warnings, static (current, value) => current with { Persistent = value }, out updated);

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

    // Gaps read as general:gaps_in and general:gaps_out do, one to four edges, none below zero.
    private static bool TryGaps(
        WorkspaceRule rule,
        string text,
        List<string> warnings,
        Func<WorkspaceRule, EdgeInsets, WorkspaceRule> apply,
        out WorkspaceRule updated)
    {
        updated = rule;
        if (!OptionValue.Gaps.Read(text, out var gaps))
        {
            warnings.Add("Error parsing workspace rule gaps: " + text.Trim());
            return false;
        }

        updated = apply(rule, gaps);
        return true;
    }

    private static bool TryFlag(
        WorkspaceRule rule,
        string text,
        List<string> warnings,
        Func<WorkspaceRule, bool, WorkspaceRule> apply,
        out WorkspaceRule updated)
    {
        updated = rule;
        if (!TryBool(text, out var value))
        {
            warnings.Add("Failed parsing a workspace rule");
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
