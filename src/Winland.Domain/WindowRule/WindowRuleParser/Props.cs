using System.Globalization;

namespace Winland.Domain;

internal static partial class WindowRuleParser
{
    /// <summary>
    /// Reads a property effect, such as <c>border_size 4</c> or <c>no_blur</c>. False with an empty error means the name
    /// is not a property effect, and false with an error means its argument does not parse.
    /// </summary>
    private static bool TryParsePropRule(string name, string? argument, out IReadOnlyDictionary<string, object>? props, out string error)
    {
        props = null;
        error = "";
        if (WindowProps.IsRuleEffect(name, PropKind.Bool))
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

    // One color sets the active border. Two colors set the active and the inactive borders.
    // An angle, or any further color, is not a border.
    private static bool TryParseBorderColor(string? argument, out IReadOnlyDictionary<string, object>? props, out string error)
    {
        props = null;
        var tokens = (argument ?? "").Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            error = $"border_color rule \"{argument}\" has no colors";
            return false;
        }

        if (tokens.Length > 2)
        {
            error = $"border_color rule \"{argument}\" has invalid color";
            return false;
        }

        foreach (var token in tokens)
        {
            if (token.Contains("deg", StringComparison.Ordinal))
            {
                error = $"border_color rule \"{argument}\" has invalid angle \"{token}\"";
                return false;
            }
        }

        if (!BorderColor.TryParse(tokens[0], out var active, out _))
        {
            error = $"border_color rule \"{argument}\" has invalid color \"{tokens[0]}\"";
            return false;
        }

        var values = new Dictionary<string, object>(StringComparer.Ordinal) { ["active_border_color"] = active };
        if (tokens.Length == 2)
        {
            if (!BorderColor.TryParse(tokens[1], out var inactive, out _))
            {
                error = $"border_color rule \"{argument}\" has invalid color \"{tokens[1]}\"";
                return false;
            }

            values["inactive_border_color"] = inactive;
        }

        props = values;
        error = "";
        return true;
    }
}
