using System.Globalization;
using System.Text.RegularExpressions;

namespace Winland.Domain;

internal static partial class WindowRuleParser
{
    // The effects TryParseEffect and its helpers read, for the hint a mistyped one gets. A test keeps every name parsing.
    internal static readonly string[] EffectNames =
    [
        "float", "tile", "ignore", "no_initial_focus", "no_focus", "opaque", "pin", "workspace", "min_size",
        "max_size", "scrolling_width", "opacity", "fullscreen", "maximize", "move", "size", "center", "monitor",
        "border_size", "rounding", "animation", "border_color",
        .. WindowProp.All.Where(prop => prop.IsRuleEffect).Select(prop => prop.Name),
    ];

    // The fields TryParseMatch reads.
    private static readonly string[] MatchFields =
        ["class", "initialClass", "initial_class", "title", "initialTitle", "initial_title", "exe", "aumid"];

    internal static bool TryParse(string value, out WindowRule? rule, out string error)
    {
        rule = null;
        var comma = value.IndexOf(',');
        if (comma <= 0 || comma == value.Length - 1)
        {
            error = "Malformed window rule.";
            return false;
        }

        if (!TryParseEffect(value[..comma].Trim(), out var effect, out var workspace, out var enabled, out var size, out var scrollingWidth, out var opacity, out var fullscreen, out var placement, out var props, out error))
            return false;

        string? classPattern = null;
        string? titlePattern = null;
        string? initialTitlePattern = null;
        string? initialClassPattern = null;
        string? exePattern = null;
        string? aumidPattern = null;
        foreach (var part in SplitFields(value[(comma + 1)..]))
        {
            if (!TryParseMatch(part, ref classPattern, ref titlePattern, ref initialTitlePattern, ref initialClassPattern, ref exePattern, ref aumidPattern, out error))
                return false;
        }

        // Winland never applies a rule that has nothing to match on.
        if (classPattern is null && titlePattern is null && initialTitlePattern is null && initialClassPattern is null && exePattern is null && aumidPattern is null)
        {
            error = "Malformed window rule.";
            return false;
        }

        rule = new WindowRule(effect, workspace, classPattern, titlePattern, exePattern, aumidPattern, enabled, size, scrollingWidth, opacity, fullscreen, placement, props, initialTitlePattern, initialClassPattern);
        return true;
    }

    internal static bool TryParseBare(string text, out WindowRule? rule, out string error)
    {
        rule = null;
        if (!TryParseEffect(text, out var effect, out var workspace, out var enabled, out var size, out var scrollingWidth, out var opacity, out var fullscreen, out var placement, out var props, out error))
            return false;

        rule = new WindowRule(effect, workspace, null, null, null, Enabled: enabled, Size: size, ScrollingWidth: scrollingWidth, Opacity: opacity, Fullscreen: fullscreen, Placement: placement, Props: props);
        return true;
    }

    internal static WindowDecision Decide(WindowTraits traits, IReadOnlyList<WindowRule> rules)
    {
        bool? adopt = null;
        bool? floating = null;
        string? workspace = null;
        bool? noInitialFocus = null;
        bool? noFocus = null;
        Size? ruleMin = null;
        Size? ruleMax = null;
        ExpressionPair? minExpression = null;
        ExpressionPair? maxExpression = null;
        double? scrollingWidth = null;
        OpacityRule? opacity = null;
        bool? opaque = null;
        bool? fullscreen = null;
        bool? maximize = null;
        bool? pin = null;
        var placement = default(MapPlacement);
        Dictionary<string, object>? props = null;
        foreach (var rule in rules)
        {
            if (!Matches(rule, traits))
                continue;

            if (rule.Effect == WindowRuleEffect.Prop && rule.Props is not null)
            {
                props ??= new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var (key, value) in rule.Props)
                    props[key] = value;
                continue;
            }

            switch (rule.Effect)
            {
                case WindowRuleEffect.Ignore:
                    adopt = false;
                    break;
                case WindowRuleEffect.Float:
                    adopt = true;
                    floating = true;
                    break;
                case WindowRuleEffect.Tile:
                    adopt = true;
                    floating = false;
                    break;
                case WindowRuleEffect.Workspace:
                    adopt = true;
                    workspace = rule.Workspace;
                    break;
                case WindowRuleEffect.NoInitialFocus:
                    noInitialFocus = rule.Enabled;
                    break;
                case WindowRuleEffect.NoFocus:
                    noFocus = rule.Enabled;
                    break;
                case WindowRuleEffect.MinSize:
                    ruleMin = rule.Size;
                    minExpression = rule.Size is null ? rule.Placement?.Expression : null;
                    break;
                case WindowRuleEffect.MaxSize:
                    ruleMax = rule.Size;
                    maxExpression = rule.Size is null ? rule.Placement?.Expression : null;
                    break;
                case WindowRuleEffect.ScrollingWidth:
                    scrollingWidth = rule.ScrollingWidth;
                    break;
                case WindowRuleEffect.Opacity:
                    opacity = rule.Opacity;
                    break;
                case WindowRuleEffect.Opaque:
                    opaque = rule.Enabled;
                    break;
                case WindowRuleEffect.Fullscreen:
                    fullscreen = rule.Enabled;
                    break;
                case WindowRuleEffect.Maximize:
                    maximize = rule.Enabled;
                    break;
                case WindowRuleEffect.Pin:
                    pin = rule.Enabled;
                    break;
                default:
                    placement = Place(placement, rule);
                    break;
            }
        }

        var sizes = new SizeRule(ruleMin, ruleMax);
        var limits = sizes.Floating(traits.MinSize, traits.MaxSize);

        return new WindowDecision(
            adopt is bool forced ? forced && Surface(traits) : Adopts(traits),
            floating ?? traits.Dialog,
            workspace,
            noInitialFocus ?? false,
            noFocus ?? false,
            limits.Min,
            limits.Max,
            scrollingWidth,
            opacity,
            opaque ?? false,
            RequestedFullscreen(fullscreen, maximize),
            pin ?? false,
            placement,
            sizes,
            props,
            minExpression,
            maxExpression);
    }

    private static bool TryParseEffect(
        string text,
        out WindowRuleEffect effect,
        out string? workspace,
        out bool enabled,
        out Size? size,
        out double? scrollingWidth,
        out OpacityRule? opacity,
        out FullscreenRule? fullscreen,
        out RulePlacement? placement,
        out IReadOnlyDictionary<string, object>? props,
        out string error)
    {
        props = null;
        effect = default;
        workspace = null;
        enabled = true;
        size = null;
        scrollingWidth = null;
        opacity = null;
        fullscreen = null;
        placement = null;
        var space = text.IndexOfAny([' ', '\t']);
        var name = space < 0 ? text : text[..space];
        var argument = space < 0 ? null : text[(space + 1)..].Trim();
        if (argument is { Length: 0 })
            argument = null;

        if (name.Length == 0)
        {
            error = "Malformed window rule.";
            return false;
        }

        switch (name)
        {
            case "float":
                effect = WindowRuleEffect.Float;
                break;
            case "tile":
                effect = WindowRuleEffect.Tile;
                break;
            case "ignore":
                effect = WindowRuleEffect.Ignore;
                break;
            case "no_initial_focus":
                effect = WindowRuleEffect.NoInitialFocus;
                break;
            case "no_focus":
                effect = WindowRuleEffect.NoFocus;
                break;
            case "opaque":
                effect = WindowRuleEffect.Opaque;
                break;
            case "pin":
                effect = WindowRuleEffect.Pin;
                break;
            case "workspace":
                if (argument is null)
                {
                    error = "Malformed window rule.";
                    return false;
                }

                if (WorkspaceAddress.IsNamed(argument))
                {
                    error = "Named workspaces are not supported.";
                    return false;
                }

                effect = WindowRuleEffect.Workspace;
                workspace = argument;
                error = "";
                return true;
            case "min_size":
            case "max_size":
                effect = name == "min_size" ? WindowRuleEffect.MinSize : WindowRuleEffect.MaxSize;
                error = "";
                if (argument is not null && TryParseSize(argument, out var parsed))
                {
                    size = parsed;
                    return true;
                }

                // either size may be an expression, read when the window opens.
                if (argument is not null && ExpressionPair.TryParse(argument, out var expression))
                {
                    placement = new RulePlacement(Expression: expression);
                    return true;
                }

                error = "Malformed window rule.";
                return false;
            case "scrolling_width":
                if (argument is null
                    || !double.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
                    || !double.IsFinite(width))
                {
                    error = "Malformed window rule.";
                    return false;
                }

                effect = WindowRuleEffect.ScrollingWidth;
                scrollingWidth = width;
                error = "";
                return true;
            case "opacity":
                if (!TryParseOpacity(argument, out var parsedOpacity, out error))
                    return false;

                effect = WindowRuleEffect.Opacity;
                opacity = parsedOpacity;
                error = "";
                return true;
            case "fullscreen":
            case "maximize":
                if (argument is null)
                    enabled = true;
                else if (!TryParseEnabled(argument, out enabled))
                {
                    error = "Malformed window rule.";
                    return false;
                }

                effect = name == "fullscreen" ? WindowRuleEffect.Fullscreen : WindowRuleEffect.Maximize;
                if (enabled)
                {
                    var mode = name == "fullscreen" ? FullscreenMode.Fullscreen : FullscreenMode.Maximized;
                    fullscreen = new FullscreenRule(mode);
                }

                error = "";
                return true;
            default:
                if (TryParsePropRule(name, argument, out props, out error))
                {
                    effect = WindowRuleEffect.Prop;
                    return true;
                }

                if (error.Length > 0)
                    return false;

                if (TryParsePlacement(name, argument, out effect, out enabled, out placement, out error))
                    return true;

                if (error.Length == 0)
                    error = $"Unsupported window rule '{name}'.{DidYouMean.For(name, EffectNames)}";
                return false;
        }

        if (effect is WindowRuleEffect.NoInitialFocus or WindowRuleEffect.NoFocus or WindowRuleEffect.Opaque or WindowRuleEffect.Pin)
        {
            if (argument is null)
            {
                error = "";
                return true;
            }

            if (!TryParseEnabled(argument, out enabled))
            {
                error = "Malformed window rule.";
                return false;
            }

            error = "";
            return true;
        }

        if (argument is not null)
        {
            error = "Malformed window rule.";
            return false;
        }

        error = "";
        return true;
    }

    // One fraction is used for every state. Two set the active and the inactive fractions, and leave fullscreen at 1.
    // Each multiplies the configured fraction.
    private static bool TryParseOpacity(string? text, out OpacityRule rule, out string error)
    {
        var tokens = string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var fractions = new float[tokens.Length];
        if (tokens.Length == 0)
        {
            rule = default;
            error = "Malformed window rule.";
            return false;
        }

        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens.Length > 2
                || !float.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out fractions[i])
                || !float.IsFinite(fractions[i]))
            {
                rule = default;
                error = "Malformed window rule.";
                return false;
            }
        }

        rule = fractions.Length switch
        {
            1 => new OpacityRule(fractions[0], fractions[0], fractions[0]),
            _ => new OpacityRule(fractions[0], fractions[1], 1f),
        };
        error = "";
        return true;
    }

    private static FullscreenRule? RequestedFullscreen(bool? fullscreen, bool? maximize)
    {
        if (maximize == true)
            return new FullscreenRule(FullscreenMode.Maximized);

        if (fullscreen == true)
            return new FullscreenRule(FullscreenMode.Fullscreen);

        return null;
    }

    private static bool TryParseSize(string text, out Size size)
    {
        size = default;
        var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var height)
            || width < 1
            || height < 1)
            return false;

        size = new Size(width, height);
        return true;
    }

    private static bool TryParseEnabled(string text, out bool enabled)
    {
        if (text.Equals("true", StringComparison.OrdinalIgnoreCase)
            || text.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || text.Equals("on", StringComparison.OrdinalIgnoreCase)
            || text == "1")
        {
            enabled = true;
            return true;
        }

        if (text.Equals("false", StringComparison.OrdinalIgnoreCase)
            || text.Equals("no", StringComparison.OrdinalIgnoreCase)
            || text.Equals("off", StringComparison.OrdinalIgnoreCase)
            || text == "0")
        {
            enabled = false;
            return true;
        }

        enabled = false;
        return false;
    }

    private static bool TryParseMatch(
        string text,
        ref string? classPattern,
        ref string? titlePattern,
        ref string? initialTitlePattern,
        ref string? initialClassPattern,
        ref string? exePattern,
        ref string? aumidPattern,
        out string error)
    {
        var colon = text.IndexOf(':');
        if (colon <= 0)
        {
            error = "Malformed window rule.";
            return false;
        }

        var key = text[..colon];
        var value = text[(colon + 1)..];
        if (value.Length == 0 || !IsPattern(value))
        {
            error = value.Length == 0 ? "Malformed window rule." : "Invalid window rule pattern.";
            return false;
        }

        switch (key)
        {
            case "class":
                classPattern = value;
                break;
            case "initialClass":
            case "initial_class":
                initialClassPattern = value;
                break;
            case "title":
                titlePattern = value;
                break;
            case "initialTitle":
            case "initial_title":
                initialTitlePattern = value;
                break;
            case "exe":
                exePattern = value;
                break;
            case "aumid":
                aumidPattern = value;
                break;
            default:
                error = $"Unsupported window rule match '{key}'.{DidYouMean.For(key, MatchFields)}";
                return false;
        }

        error = "";
        return true;
    }

    // The match fields, split at each comma that starts a new field, so a pattern such as title:a{1,2} keeps its comma.
    // An empty piece, from a trailing or doubled comma, is dropped.
    private static List<string> SplitFields(string text)
    {
        var fields = new List<string>();
        foreach (var piece in text.Split(','))
        {
            if (piece.Trim().Length == 0)
                continue;

            if (fields.Count > 0 && !FieldStart().IsMatch(piece))
                fields[^1] += "," + piece;
            else
                fields.Add(piece);
        }

        return fields.Select(field => field.Trim()).Where(field => field.Length > 0).ToList();
    }

    [GeneratedRegex(@"\A\s*[A-Za-z_]+:")]
    private static partial Regex FieldStart();

    private static bool Matches(WindowRule rule, WindowTraits traits) =>
        Matches(rule.ClassPattern, traits.Class)
        && Matches(rule.TitlePattern, traits.Title)
        && Matches(rule.InitialTitlePattern, traits.InitialTitle ?? traits.Title)
        && Matches(rule.InitialClassPattern, traits.InitialClass ?? traits.Class)
        && Matches(rule.ExePattern, traits.Exe)
        && Matches(rule.AumidPattern, traits.Aumid);

    // Winland matches a rule's pattern against the whole field, and a
    // "negative:" prefix passes only when the rest of the pattern does not match.
    private const string NegativePrefix = "negative:";

    private static bool Matches(string? pattern, string text)
    {
        if (pattern is null)
            return true;

        var negative = pattern.StartsWith(NegativePrefix, StringComparison.Ordinal);
        try
        {
            return WholeMatch.Of(Body(pattern, negative)).IsMatch(text) != negative;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool IsPattern(string pattern)
    {
        try
        {
            _ = WholeMatch.Of(Body(pattern, pattern.StartsWith(NegativePrefix, StringComparison.Ordinal)));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string Body(string pattern, bool negative) =>
        negative ? pattern[NegativePrefix.Length..] : pattern;

    private static bool Adopts(WindowTraits traits) =>
        Surface(traits)
        && traits.Titled
        && !traits.Tool
        && !traits.Taskbar
        && !traits.ExclusiveFullscreen;

    // What even a rule that forces adoption needs: a window someone can see and activate, not a tooltip, an
    // untitled tool window such as a menu, or a no-activate popup a program shows over its own window.
    private static bool Surface(WindowTraits traits) =>
        traits.Visible
        && traits.TopLevel
        && (traits.Titled || !traits.Tool)
        && !traits.Tooltip
        && !traits.Cloaked
        && !traits.Overlay;
}
