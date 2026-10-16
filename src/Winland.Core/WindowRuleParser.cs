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

        if (!TryParseEffect(value[..comma].Trim(), out var effect, out var workspace, out var enabled, out var size, out var scrollingWidth, out var opacity, out var fullscreen, out var idle, out var placement, out var group, out error))
            return false;

        string? classPattern = null;
        string? titlePattern = null;
        string? exePattern = null;
        string? aumidPattern = null;
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
            foreach (var part in value[(comma + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TryParseMatch(part, ref classPattern, ref titlePattern, ref exePattern, ref aumidPattern, out error))
                    return false;
            }
        }

        rule = new WindowRule(effect, workspace, classPattern, titlePattern, exePattern, aumidPattern, enabled, size, scrollingWidth, opacity, fullscreen, idle, placement, group);
        return true;
    }

    internal static bool TryParseBare(string text, out WindowRule? rule, out string error)
    {
        rule = null;
        if (!TryParseEffect(text, out var effect, out var workspace, out var enabled, out var size, out _, out var opacity, out var fullscreen, out var idle, out var placement, out var group, out error))
            return false;

        rule = new WindowRule(effect, workspace, null, null, null, Enabled: enabled, Size: size, Opacity: opacity, Fullscreen: fullscreen, IdleInhibit: idle, Placement: placement, Group: group);
        return true;
    }

    internal static WindowDecision Decide(WindowTraits traits, IReadOnlyList<WindowRule> rules)
    {
        bool? adopt = null;
        bool? floating = null;
        string? workspace = null;
        bool? noInitialFocus = null;
        bool? noFocus = null;
        bool? stayFocused = null;
        var min = traits.MinSize;
        var max = traits.MaxSize;
        var maxFromRule = false;
        var suppressMax = false;
        double? scrollingWidth = null;
        OpacityRule? opacity = null;
        bool? opaque = null;
        bool? fullscreen = null;
        bool? maximize = null;
        FullscreenRule? fullscreenState = null;
        bool? pin = null;
        IdleInhibitMode? idle = null;
        var placement = default(MapPlacement);
        var group = GroupRuleFlags.None;
        foreach (var rule in rules)
        {
            if (!Matches(rule, traits))
                continue;

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
                    min = rule.Size;
                    break;
                case WindowRuleEffect.MaxSize:
                    max = rule.Size;
                    maxFromRule = true;
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
                case WindowRuleEffect.IdleInhibit:
                    idle = rule.IdleInhibit;
                    break;
                case WindowRuleEffect.Group:
                    group = GroupRules.Parse(rule.Group);
                    break;
                default:
                    placement = Place(placement, rule);
                    break;
            }
        }

        if (suppressMax && !maxFromRule)
            max = null;

        return new WindowDecision(
            adopt ?? Adopts(traits),
            floating ?? traits.Dialog,
            workspace,
            noInitialFocus ?? false,
            noFocus ?? false,
            stayFocused ?? false,
            min,
            max,
            scrollingWidth,
            opacity,
            opaque ?? false,
            RequestedFullscreen(fullscreen, maximize, fullscreenState),
            pin ?? false,
            idle ?? IdleInhibitMode.None,
            placement,
            group);
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
        out IdleInhibitMode idle,
        out RulePlacement? placement,
        out string? group,
        out string error)
    {
        idle = IdleInhibitMode.None;
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
            case "workspace":
                if (argument is null)
                {
                    error = "Malformed window rule.";
                    return false;
                }

                effect = WindowRuleEffect.Workspace;
                workspace = argument;
                error = "";
                return true;
            case "min_size":
            case "max_size":
                if (argument is null || !TryParseSize(argument, out var parsed))
                {
                    error = "Malformed window rule.";
                    return false;
                }

                effect = name == "min_size" ? WindowRuleEffect.MinSize : WindowRuleEffect.MaxSize;
                size = parsed;
                error = "";
                return true;
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
            case "idle_inhibit":
            case "idleinhibit":
                if (argument is null || !IdleInhibit.TryParse(argument, out idle))
                {
                    error = $"idle_inhibit rule has unknown mode \"{argument}\"";
                    return false;
                }

                effect = WindowRuleEffect.IdleInhibit;
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
            case "initialClass":
            case "initial_class":
                classPattern = value;
                break;
            case "title":
            case "initialTitle":
            case "initial_title":
                titlePattern = value;
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
        && Matches(rule.ExePattern, traits.Exe)
        && Matches(rule.AumidPattern, traits.Aumid);

    // The pinned release matches a rule's pattern against the whole field, and a
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
