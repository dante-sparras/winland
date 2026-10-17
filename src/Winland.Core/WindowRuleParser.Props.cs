using System.Globalization;

namespace Winland;

internal static partial class WindowRuleParser
{
    // Window rule effects that set a window property and nothing else, as the pinned release's dynamic effects do.
    // Those Winland already gives an effect of their own, such as no_focus or opacity, keep it.
    private static readonly HashSet<string> BoolProps = new(StringComparer.Ordinal)
    {
        "allows_input", "dim_around", "decorate", "focus_on_activate", "keep_aspect_ratio", "nearest_neighbor", "no_anim",
        "no_blur", "no_dim", "no_follow_mouse", "no_shadow", "no_glow", "no_wobble", "no_shortcuts_inhibit", "force_rgbx",
        "sync_fullscreen", "immediate", "xray", "render_unfocused", "no_screen_share", "no_vrr", "no_auto_hdr", "persistent_size",
    };

    private static readonly HashSet<string> FloatProps = new(StringComparer.Ordinal) { "rounding_power", "scroll_mouse", "scroll_touchpad" };

    /// <summary>
    /// Reads a property effect, such as <c>border_size 4</c> or <c>no_blur</c>. False with an empty error means the name
    /// is not a property effect, and false with an error means its argument does not parse.
    /// </summary>
    private static bool TryParsePropRule(string name, string? argument, out IReadOnlyDictionary<string, object>? props, out string error)
    {
        props = null;
        error = "";
        if (BoolProps.Contains(name))
        {
            props = new Dictionary<string, object>(StringComparer.Ordinal) { [name] = argument is null || WindowProps.Truthy(argument) };
            return true;
        }

        if (name is "border_size" or "rounding")
        {
            if (argument is null || !long.TryParse(argument, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
            {
                error = $"{name} rule \"{argument}\" failed with: bad input";
                return false;
            }

            props = new Dictionary<string, object>(StringComparer.Ordinal) { [name] = Math.Max(0, whole) };
            return true;
        }

        if (FloatProps.Contains(name))
        {
            if (argument is null || !WindowProps.TryFloat(argument, out var value))
            {
                error = $"{name} rule \"{argument}\" failed with: bad input";
                return false;
            }

            props = new Dictionary<string, object>(StringComparer.Ordinal) { [name] = value };
            return true;
        }

        if (name == "animation")
        {
            if (argument is null)
            {
                error = "Malformed window rule.";
                return false;
            }

            props = new Dictionary<string, object>(StringComparer.Ordinal) { [name] = argument };
            return true;
        }

        if (name == "border_color")
            return TryParseBorderColor(argument, out props, out error);

        return false;
    }

    // The pinned release's border_color: two colors are active and inactive, and otherwise the colors up to the first
    // angle are the active gradient and the rest the inactive one.
    private static bool TryParseBorderColor(string? argument, out IReadOnlyDictionary<string, object>? props, out string error)
    {
        props = null;
        var tokens = (argument ?? "").Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var active = new List<BorderColor>();
        var inactive = new List<BorderColor>();
        int? activeAngle = null;
        var inactiveAngle = 0;
        if (tokens.Length == 2 && !tokens[1].Contains("deg", StringComparison.Ordinal))
        {
            if (!BorderColor.TryParse(tokens[0], out var first, out _) || !BorderColor.TryParse(tokens[1], out var second, out _))
            {
                error = $"border_color rule \"{argument}\" has invalid color";
                return false;
            }

            active.Add(first);
            inactive.Add(second);
        }
        else
        {
            foreach (var token in tokens)
            {
                if (token.Contains("deg", StringComparison.Ordinal))
                {
                    var degrees = token.EndsWith("deg", StringComparison.Ordinal) ? token[..^3] : token;
                    if (!int.TryParse(degrees, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var angle))
                    {
                        error = $"border_color rule \"{argument}\" has invalid angle \"{token}\"";
                        return false;
                    }

                    if (activeAngle is null)
                        activeAngle = angle;
                    else
                        inactiveAngle = angle;
                    continue;
                }

                if (!BorderColor.TryParse(token, out var color, out _))
                {
                    error = $"border_color rule \"{argument}\" has invalid color \"{token}\"";
                    return false;
                }

                (activeAngle is null ? active : inactive).Add(color);
            }
        }

        if (active.Count > 10 || inactive.Count > 10)
        {
            error = $"border_color rule \"{argument}\" has more than 10 colors in one gradient";
            return false;
        }

        if (active.Count == 0 && inactive.Count == 0)
        {
            error = $"border_color rule \"{argument}\" has no colors";
            return false;
        }

        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        if (active.Count > 0)
            values["active_border_color"] = new BorderGradient(active, activeAngle ?? 0);
        if (inactive.Count > 0)
            values["inactive_border_color"] = new BorderGradient(inactive, inactiveAngle);
        props = values;
        error = "";
        return true;
    }
}
