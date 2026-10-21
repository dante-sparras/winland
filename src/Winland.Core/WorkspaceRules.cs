using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winland;

/// <summary>
/// One <c>workspace</c> rule. Unset fields stay null and do not override the general config.
/// </summary>
public sealed record WorkspaceRule
{
    public static IReadOnlyList<WorkspaceRule> None { get; } = [];

    public string WorkspaceString { get; init; } = "";

    public string WorkspaceName { get; init; } = "";

    public string Monitor { get; init; } = "";

    public bool? Default { get; init; }

    public bool? Persistent { get; init; }

    public EdgeInsets? GapsIn { get; init; }

    public EdgeInsets? GapsOut { get; init; }

    /// <summary>The gaps between a floating Client and the Monitor edges, in place of <c>general:float_gaps</c>.</summary>
    public EdgeInsets? FloatGaps { get; init; }

    public int? BorderSize { get; init; }

    public bool? Border { get; init; }

    public bool? Rounding { get; init; }

    public bool? Decorate { get; init; }

    /// <summary>The style this Workspace is switched to and from with, in place of the workspaces node's.</summary>
    public string? Animation { get; init; }

    public string? DefaultName { get; init; }

    public string? OnCreatedEmpty { get; init; }

    /// <summary>The Layout this Workspace uses in place of <c>general:layout</c>.</summary>
    public string? Layout { get; init; }

    public IReadOnlyDictionary<string, string> LayoutOpts { get; init; } = EmptyOpts;

    /// <summary>The id <c>hl.workspace_rule</c> writes, or 0 for a line that is not named.</summary>
    public int RuleId { get; init; }

    /// <summary>False leaves the rule stored and stops it applying, as <c>set_enabled(false)</c> does.</summary>
    public bool Active { get; init; } = true;

    /// <summary>True when the line named <c>ruleon</c>, so a merge can change <see cref="Active"/>.</summary>
    public bool ActiveSpecified { get; init; }

    private static readonly IReadOnlyDictionary<string, string> EmptyOpts =
        new Dictionary<string, string>(StringComparer.Ordinal);

    internal WorkspaceRule Merge(WorkspaceRule other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var opts = new Dictionary<string, string>(LayoutOpts, StringComparer.Ordinal);
        foreach (var pair in other.LayoutOpts)
            opts[pair.Key] = pair.Value;

        return this with
        {
            Monitor = other.Monitor.Length > 0 ? other.Monitor : Monitor,
            Default = other.Default ?? Default,
            Persistent = other.Persistent ?? Persistent,
            GapsIn = other.GapsIn ?? GapsIn,
            GapsOut = other.GapsOut ?? GapsOut,
            FloatGaps = other.FloatGaps ?? FloatGaps,
            BorderSize = other.BorderSize ?? BorderSize,
            Border = other.Border ?? Border,
            Rounding = other.Rounding ?? Rounding,
            Decorate = other.Decorate ?? Decorate,
            Animation = other.Animation ?? Animation,
            DefaultName = other.DefaultName ?? DefaultName,
            OnCreatedEmpty = other.OnCreatedEmpty ?? OnCreatedEmpty,
            Layout = other.Layout ?? Layout,
            LayoutOpts = opts.Count == 0 ? EmptyOpts : opts,
            Active = other.ActiveSpecified ? other.Active : Active,
        };
    }
}

/// <summary>
/// What a <c>w[]</c> workspace selector counts: only tiled or only floating Clients when <see cref="Tiled"/>
/// is set, only pinned ones, Groups in place of Clients, and only the ones shown.
/// </summary>
internal readonly record struct WindowCount(bool? Tiled, bool Pinned, bool Groups, bool Visible);

internal static partial class WorkspaceRules
{
    internal static void ReplaceOrAdd(List<WorkspaceRule> rules, WorkspaceRule rule)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(rule);

        // A named rule is one object. A later line with its id replaces it, as a second hl.workspace_rule rewrites it.
        if (rule.RuleId != 0)
        {
            for (var i = 0; i < rules.Count; i++)
            {
                if (rules[i].RuleId == rule.RuleId)
                {
                    rules[i] = rule;
                    return;
                }
            }

            rules.Add(rule);
            return;
        }

        for (var i = 0; i < rules.Count; i++)
        {
            if (rules[i].RuleId == 0 && rules[i].WorkspaceString == rule.WorkspaceString)
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
        var ident = LineText.Unescape((comma < 0 ? value : value[..comma]).Trim());
        if (ident.Length == 0)
        {
            warnings.Add("Malformed workspace rule.");
            return null;
        }

        var rule = new WorkspaceRule
        {
            WorkspaceString = ident,
            WorkspaceName = NameOf(ident),
        };
        if (comma < 0)
            return rule;

        var fatal = false;
        foreach (var piece in value[(comma + 1)..].Split(','))
        {
            var field = LineText.Unescape(piece.Trim());
            if (field.Length == 0)
                continue;

            if (!Apply(rule, field, warnings, out rule, out var failed))
                fatal = true;
            else if (failed)
                fatal = true;
        }

        return fatal ? null : rule;
    }

    /// <summary>True when the Workspace matches the selector, as a window rule's workspace match reads it.</summary>
    internal static bool MatchesSelector(
        string selector,
        WorkspaceId id,
        string? displayName,
        Monitor? monitor,
        Func<WindowCount, int>? count,
        FullscreenMode fullscreen) => Matches(selector, id, displayName, monitor, count, fullscreen);

    internal static WorkspaceRule? For(
        IReadOnlyList<WorkspaceRule> rules,
        WorkspaceId id,
        string? displayName,
        Monitor? monitor,
        Func<WindowCount, int>? count = null,
        FullscreenMode fullscreen = FullscreenMode.None)
    {
        ArgumentNullException.ThrowIfNull(rules);
        WorkspaceRule? merged = null;
        foreach (var rule in rules)
        {
            if (!rule.Active || !Matches(rule.WorkspaceString, id, displayName, monitor, count, fullscreen))
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
            if (!rule.Active || rule.Default != true)
                continue;

            if (rule.Monitor.Length > 0 && !MonitorRules.Matches(monitor, rule.Monitor))
                continue;

            return rule.WorkspaceString;
        }

        return null;
    }

    internal static string BoundMonitor(IReadOnlyList<WorkspaceRule> rules, WorkspaceId id)
    {
        ArgumentNullException.ThrowIfNull(rules);
        foreach (var rule in rules)
        {
            if (rule.Active && rule.WorkspaceName == id.Name)
                return rule.Monitor;
        }

        return "";
    }

    internal static bool TryConcrete(string text, out WorkspaceId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        if (text.StartsWith("name:", StringComparison.Ordinal))
        {
            var name = text[5..];
            if (name.Length == 0 || IsPositive(name, out _))
                return false;

            id = new WorkspaceId(name);
            return true;
        }

        if (!IsPositive(text, out var number))
            return false;

        id = WorkspaceId.FromNumber(number);
        return true;
    }

    internal static string Format(IReadOnlyList<WorkspaceRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var views = new RuleJson[rules.Count];
        for (var i = 0; i < rules.Count; i++)
            views[i] = Describe(rules[i]);

        return JsonSerializer.Serialize(views);
    }

    private static bool Apply(WorkspaceRule rule, string field, List<string> warnings, out WorkspaceRule updated, out bool failed)
    {
        updated = rule;
        failed = false;
        if (Take(field, "gapsin:", out var gapsIn))
            return TryGaps(rule, gapsIn, warnings, static (current, gaps) => current with { GapsIn = gaps }, out updated, out failed);

        if (Take(field, "gapsout:", out var gapsOut))
            return TryGaps(rule, gapsOut, warnings, static (current, gaps) => current with { GapsOut = gaps }, out updated, out failed);

        if (Take(field, "bordersize:", out var borderSize))
        {
            if (!int.TryParse(borderSize.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
            {
                warnings.Add("Error parsing workspace rule bordersize: " + borderSize.Trim());
                failed = true;
                return false;
            }

            updated = rule with { BorderSize = size };
            return true;
        }

        if (Take(field, "border:", out var border))
            return TryFlag(rule, border, warnings, static (current, value) => current with { Border = value }, out updated, out failed);

        if (Take(field, "rounding:", out var rounding))
            return TryFlag(rule, rounding, warnings, static (current, value) => current with { Rounding = value }, out updated, out failed);

        if (Take(field, "decorate:", out var decorate))
            return TryFlag(rule, decorate, warnings, static (current, value) => current with { Decorate = value }, out updated, out failed);

        if (Take(field, "monitor:", out var monitor))
        {
            updated = rule with { Monitor = monitor.Trim() };
            return true;
        }

        if (Take(field, "defaultName:", out var defaultName))
        {
            updated = rule with { DefaultName = defaultName.Trim() };
            return true;
        }

        if (Take(field, "default:", out var defaultValue))
            return TryFlag(rule, defaultValue, warnings, static (current, value) => current with { Default = value }, out updated, out failed);

        if (Take(field, "persistent:", out var persistent))
            return TryFlag(rule, persistent, warnings, static (current, value) => current with { Persistent = value }, out updated, out failed);

        if (Take(field, "on-created-empty:", out var command))
        {
            if (!TryClean(rule.WorkspaceName.Length > 0 ? rule.WorkspaceName : rule.WorkspaceString, command, out var cleaned))
            {
                warnings.Add("Failed parsing a workspace rule");
                failed = true;
                return false;
            }

            updated = rule with { OnCreatedEmpty = cleaned };
            return true;
        }

        if (Take(field, "layout:", out var layout))
        {
            var name = layout.Trim();
            if (LayoutFactory.Fallback(name).Warning is string unknown)
                warnings.Add(unknown);

            updated = rule with { Layout = name };
            return true;
        }

        if (Take(field, "layoutopt:", out var layoutOpt))
        {
            var split = layoutOpt.IndexOf(':');
            if (split <= 0)
            {
                warnings.Add("Invalid workspace rule found: " + field.Trim());
                failed = true;
                return false;
            }

            var key = layoutOpt[..split];
            var layoutValue = layoutOpt[(split + 1)..];
            if (!LayoutOptions.TryOverrideDwindle(LayoutOptions.Unconfigured, key, layoutValue, out _))
                return true;

            var opts = new Dictionary<string, string>(rule.LayoutOpts, StringComparer.Ordinal)
            {
                [key] = layoutValue,
            };
            updated = rule with { LayoutOpts = opts };
            return true;
        }

        if (Take(field, "animation:", out var animation))
        {
            if (animation.Trim() is { Length: > 0 } style)
                updated = rule with { Animation = style };
            return true;
        }

        if (Take(field, "floatgaps:", out var floatGaps) || Take(field, "float_gaps:", out floatGaps))
            return TryGaps(rule, floatGaps, warnings, static (current, gaps) => current with { FloatGaps = gaps }, out updated, out failed);

        if (Take(field, "ruleid:", out var ruleId))
        {
            if (!int.TryParse(ruleId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id < 1)
            {
                warnings.Add("Malformed workspace rule.");
                failed = true;
                return false;
            }

            updated = rule with { RuleId = id };
            return true;
        }

        if (Take(field, "ruleon:", out var ruleOn))
        {
            if (!TryBool(ruleOn, out var active))
            {
                warnings.Add("Malformed workspace rule.");
                failed = true;
                return false;
            }

            updated = rule with { Active = active, ActiveSpecified = true };
            return true;
        }

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

    private static bool TryClean(string workspaceName, string dirty, out string cleaned)
    {
        cleaned = "";
        var cmd = dirty.Trim();
        if (cmd.Length == 0)
            return false;

        var workspaceRule = "workspace " + workspaceName;
        if (cmd[0] != '[')
        {
            cleaned = "[" + workspaceRule + "] " + cmd;
            return true;
        }

        var end = cmd.LastIndexOf(']');
        if (end < 1)
            return false;

        var parts = cmd[1..end].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        var had = false;
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i].StartsWith("workspace", StringComparison.Ordinal))
            {
                parts[i] = workspaceRule;
                had = true;
            }
        }

        if (!had)
            parts.Add(workspaceRule);

        cleaned = "[" + string.Join(';', parts) + "]" + cmd[(end + 1)..];
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

    private static RuleJson Describe(WorkspaceRule rule) => new(
        rule.WorkspaceString,
        rule.Active,
        rule.Monitor.Length == 0 ? null : rule.Monitor,
        rule.Default,
        rule.Persistent,
        Gaps(rule.GapsIn),
        Gaps(rule.GapsOut),
        rule.BorderSize,
        rule.Border,
        rule.Rounding,
        rule.Decorate,
        rule.DefaultName,
        rule.OnCreatedEmpty);

    private static int[]? Gaps(EdgeInsets? gaps) =>
        gaps is EdgeInsets value ? [value.Top, value.Right, value.Bottom, value.Left] : null;

    private sealed record RuleJson(
        [property: JsonPropertyName("workspaceString")] string WorkspaceString,
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("monitor"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Monitor,
        [property: JsonPropertyName("default"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Default,
        [property: JsonPropertyName("persistent"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Persistent,
        [property: JsonPropertyName("gapsIn"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int[]? GapsIn,
        [property: JsonPropertyName("gapsOut"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int[]? GapsOut,
        [property: JsonPropertyName("borderSize"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? BorderSize,
        [property: JsonPropertyName("border"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Border,
        [property: JsonPropertyName("rounding"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Rounding,
        [property: JsonPropertyName("decorate"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Decorate,
        [property: JsonPropertyName("defaultName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DefaultName,
        [property: JsonPropertyName("onCreatedEmpty"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OnCreatedEmpty);
}
