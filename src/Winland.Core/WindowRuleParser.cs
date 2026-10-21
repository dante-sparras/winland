using System.Globalization;
using System.Text.RegularExpressions;

namespace Winland;

internal static partial class WindowRuleParser
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    internal static bool TryParse(string keyword, string value, out WindowRule? rule, out string error)
    {
        rule = null;
        var comma = value.IndexOf(',');
        if (comma <= 0 || comma == value.Length - 1)
        {
            error = "Malformed window rule.";
            return false;
        }

        if (!TryParseEffect(value[..comma].Trim(), out var effect, out var workspace, out var enabled, out var size, out var scrollingWidth, out var opacity, out var fullscreen, out var placement, out var group, out var props, out error))
            return false;

        string? classPattern = null;
        string? titlePattern = null;
        string? initialTitlePattern = null;
        string? initialClassPattern = null;
        string? exePattern = null;
        string? aumidPattern = null;
        List<RuleMatch>? dynamic = null;
        if (keyword == "windowrule")
        {
            var pattern = value[(comma + 1)..].Trim();
            if (pattern.Length == 0 || !IsPattern(pattern))
            {
                error = pattern.Length == 0 ? "Malformed window rule." : "Invalid window rule pattern.";
                return false;
            }

            classPattern = pattern;
        }
        else
        {
            foreach (var split in value[(comma + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var part = split;
                if (TryParseDynamicMatch(part, out var match))
                {
                    (dynamic ??= []).Add(match);
                    continue;
                }

                if (!TryParseMatch(part, ref classPattern, ref titlePattern, ref initialTitlePattern, ref initialClassPattern, ref exePattern, ref aumidPattern, out error))
                    return false;
            }

            // Winland never applies a rule that has nothing to match on.
            if (classPattern is null && titlePattern is null && initialTitlePattern is null && initialClassPattern is null && exePattern is null && aumidPattern is null && dynamic is null)
            {
                error = "Malformed window rule.";
                return false;
            }
        }

        rule = new WindowRule(effect, workspace, classPattern, titlePattern, exePattern, aumidPattern, enabled, size, scrollingWidth, opacity, fullscreen, placement, TextOr(effect, group, out var ruleText), props, dynamic, ruleText, initialTitlePattern, initialClassPattern);
        return true;
    }

    internal static bool TryParseBare(string text, out WindowRule? rule, out string error)
    {
        rule = null;
        if (!TryParseEffect(text, out var effect, out var workspace, out var enabled, out var size, out var scrollingWidth, out var opacity, out var fullscreen, out var placement, out var group, out var props, out error))
            return false;

        rule = new WindowRule(effect, workspace, null, null, null, Enabled: enabled, Size: size, ScrollingWidth: scrollingWidth, Opacity: opacity, Fullscreen: fullscreen, Placement: placement, Group: TextOr(effect, group, out var ruleText), Props: props, Text: ruleText);
        return true;
    }

    // A tag or content rule's argument is its text, and any other rule's is its group options.
    private static string? TextOr(WindowRuleEffect effect, string? argument, out string? text)
    {
        text = effect is WindowRuleEffect.Tag or WindowRuleEffect.Content ? argument : null;
        return text is null ? argument : null;
    }

    internal static WindowDecision Decide(WindowTraits traits, IReadOnlyList<WindowRule> rules, RuleSubject? subject = null)
    {
        bool? adopt = null;
        bool? floating = null;
        string? workspace = null;
        bool? noInitialFocus = null;
        bool? noFocus = null;
        bool? stayFocused = null;
        Size? ruleMin = null;
        Size? ruleMax = null;
        ExpressionPair? minExpression = null;
        ExpressionPair? maxExpression = null;
        var suppressMax = false;
        double? scrollingWidth = null;
        OpacityRule? opacity = null;
        bool? opaque = null;
        bool? fullscreen = null;
        bool? maximize = null;
        FullscreenRule? fullscreenState = null;
        bool? pin = null;
        var placement = default(MapPlacement);
        var group = GroupRuleFlags.None;
        Dictionary<string, object>? props = null;
        var content = 0;
        foreach (var rule in rules)
        {
            if (!Matches(rule, traits) || !MatchesState(rule, subject))
                continue;

            if (rule.Effect == WindowRuleEffect.Prop && rule.Props is not null)
            {
                props ??= new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var (key, value) in rule.Props)
                    props[key] = value;
                FocusBorderColor(rule, props);
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
                case WindowRuleEffect.StayFocused:
                    stayFocused = rule.Enabled;
                    break;
                case WindowRuleEffect.MinSize:
                    ruleMin = rule.Size;
                    minExpression = rule.Size is null ? rule.Placement?.Expression : null;
                    break;
                case WindowRuleEffect.MaxSize:
                    ruleMax = rule.Size;
                    maxExpression = rule.Size is null ? rule.Placement?.Expression : null;
                    break;
                case WindowRuleEffect.NoMaxSize:
                    suppressMax = rule.Enabled;
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
                case WindowRuleEffect.FullscreenState:
                    fullscreenState = rule.Fullscreen;
                    break;
                case WindowRuleEffect.Pin:
                    pin = rule.Enabled;
                    break;
                case WindowRuleEffect.Group:
                    group = GroupRules.Parse(rule.Group);
                    break;
                case WindowRuleEffect.Content:
                    content = ContentTypes.FromString(rule.Text ?? "");
                    break;
                default:
                    placement = Place(placement, rule);
                    break;
            }
        }

        var sizes = new SizeRule(ruleMin, ruleMax, suppressMax);
        var limits = sizes.Floating(traits.MinSize, traits.MaxSize);
        // A fullscreen or maximize rule names the internal mode, and the client mode copies it. sync_fullscreen false
        // leaves the client mode at none. A fullscreen_state rule names both modes and is left as parsed.
        var requested = RequestedFullscreen(fullscreen, maximize, fullscreenState);
        if (fullscreenState is null
            && requested is FullscreenRule open
            && props is not null
            && props.TryGetValue("sync_fullscreen", out var sync)
            && sync is false)
            requested = open with { Client = FullscreenMode.None };

        return new WindowDecision(
            adopt ?? Adopts(traits),
            floating ?? traits.Dialog,
            workspace,
            noInitialFocus ?? false,
            noFocus ?? false,
            stayFocused ?? false,
            limits.Min,
            limits.Max,
            scrollingWidth,
            opacity,
            opaque ?? false,
            requested,
            pin ?? false,
            placement,
            group,
            sizes,
            props,
            content,
            minExpression,
            maxExpression,
            fullscreenState is not null);
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
        out string? group,
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
        group = null;
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
            case "noinitialfocus":
            case "no_initial_focus":
                effect = WindowRuleEffect.NoInitialFocus;
                break;
            case "nofocus":
            case "no_focus":
                effect = WindowRuleEffect.NoFocus;
                break;
            case "stayfocused":
            case "stay_focused":
                effect = WindowRuleEffect.StayFocused;
                break;
            case "opaque":
                effect = WindowRuleEffect.Opaque;
                break;
            case "pin":
                effect = WindowRuleEffect.Pin;
                break;
            case "group":
                if (!GroupRules.IsValid(argument))
                {
                    error = "Malformed window rule.";
                    return false;
                }

                effect = WindowRuleEffect.Group;
                group = argument ?? "";
                error = "";
                return true;
            case "tag":
            case "content":
                if (argument is null)
                {
                    error = "Malformed window rule.";
                    return false;
                }

                // The tag or content type rides in the group argument until the rule is built.
                effect = name == "tag" ? WindowRuleEffect.Tag : WindowRuleEffect.Content;
                group = argument;
                error = "";
                return true;
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
            case "no_max_size":
                if (argument is null)
                    enabled = true;
                else if (!TryParseEnabled(argument, out enabled))
                {
                    error = "Malformed window rule.";
                    return false;
                }

                effect = WindowRuleEffect.NoMaxSize;
                error = "";
                return true;
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
                    fullscreen = new FullscreenRule(mode, mode);
                }

                error = "";
                return true;
            case "fullscreen_state":
                if (argument is null || !TryParseFullscreenState(argument, out fullscreen))
                {
                    error = "Malformed window rule.";
                    return false;
                }

                effect = WindowRuleEffect.FullscreenState;
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
                    error = $"Unsupported window rule '{name}'.";
                return false;
        }

        if (effect is WindowRuleEffect.NoInitialFocus or WindowRuleEffect.NoFocus or WindowRuleEffect.StayFocused or WindowRuleEffect.Opaque or WindowRuleEffect.Pin)
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

    private static bool TryParseOpacity(string? text, out OpacityRule rule, out string error)
    {
        var active = OpacityFactor.Identity;
        var inactive = OpacityFactor.Identity;
        var fullscreen = OpacityFactor.Identity;
        var index = 0;
        var tokens = string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (token == "opacity")
                continue;

            if (token == "override")
            {
                if (index == 1)
                    active = active with { Overridden = true };
                else if (index == 2)
                    inactive = inactive with { Overridden = true };
                else if (index == 3)
                    fullscreen = fullscreen with { Overridden = true };

                continue;
            }

            if (index >= 3
                || !float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !float.IsFinite(value))
            {
                rule = default;
                error = "Malformed window rule.";
                return false;
            }

            var factor = new OpacityFactor(value, false);
            if (index == 0)
                active = factor;
            else if (index == 1)
                inactive = factor;
            else
                fullscreen = factor;

            index++;
        }

        if (index == 1)
        {
            inactive = active;
            fullscreen = active;
        }

        rule = new OpacityRule(active, inactive, fullscreen);
        error = "";
        return true;
    }

    private static FullscreenRule? RequestedFullscreen(bool? fullscreen, bool? maximize, FullscreenRule? state)
    {
        if (state is FullscreenRule requested)
            return requested;

        if (maximize == true)
            return new FullscreenRule(FullscreenMode.Maximized, FullscreenMode.Maximized);

        if (fullscreen == true)
            return new FullscreenRule(FullscreenMode.Fullscreen, FullscreenMode.Fullscreen);

        return null;
    }

    private static bool TryParseFullscreenState(string text, out FullscreenRule? rule)
    {
        rule = null;
        var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2
            || !int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var internalMode))
            return false;

        var clientMode = 0;
        if (parts.Length == 2
            && !int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out clientMode))
            return false;

        rule = new FullscreenRule(ClampMode(internalMode), ClampMode(clientMode));
        return true;
    }

    private static FullscreenMode ClampMode(int mode)
    {
        if (mode < 0)
            return FullscreenMode.None;
        if (mode > 2)
            return FullscreenMode.Fullscreen;
        return (FullscreenMode)mode;
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
        if (key == "xwayland")
        {
            error = "Window rules that match on xwayland are not honored.";
            return false;
        }

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
                error = $"Unsupported window rule match '{key}'.";
                return false;
        }

        error = "";
        return true;
    }

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
            return Regex.IsMatch(text, Anchored(pattern, negative), RegexOptions.CultureInvariant, MatchTimeout) != negative;
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
            _ = new Regex(Anchored(pattern, pattern.StartsWith(NegativePrefix, StringComparison.Ordinal)), RegexOptions.CultureInvariant, MatchTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string Anchored(string pattern, bool negative) =>
        $"\\A(?:{(negative ? pattern[NegativePrefix.Length..] : pattern)})\\z";

    private static bool Adopts(WindowTraits traits) =>
        traits.Visible
        && traits.Titled
        && traits.TopLevel
        && !traits.Tool
        && !traits.Tooltip
        && !traits.Taskbar
        && !traits.Cloaked
        && !traits.ExclusiveFullscreen
        && !traits.Overlay;
}
