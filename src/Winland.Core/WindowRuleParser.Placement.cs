using System.Globalization;

namespace Winland.Core;

internal static partial class WindowRuleParser
{
    /// <summary>
    /// Parses <c>move</c>, <c>size</c>, <c>center</c>, <c>monitor</c> and <c>no_close_for</c>.
    /// The error is empty when the name is none of them.
    /// </summary>
    private static bool TryParsePlacement(
        string name,
        string? argument,
        out WindowRuleEffect effect,
        out bool enabled,
        out RulePlacement? placement,
        out string error)
    {
        effect = default;
        enabled = true;
        placement = null;
        error = "";
        switch (name)
        {
            case "move":
            case "size":
                if (argument is null || !ExpressionPair.TryParse(argument, out var pair))
                    break;

                effect = name == "move" ? WindowRuleEffect.Move : WindowRuleEffect.Size;
                placement = new RulePlacement(Expression: pair);
                return true;
            case "center":
                if (argument is not null && !TryParseEnabled(argument, out enabled))
                    break;

                effect = WindowRuleEffect.Center;
                return true;
            case "monitor":
                if (argument is null)
                    break;

                effect = WindowRuleEffect.Monitor;
                placement = new RulePlacement(Monitor: argument);
                return true;
            case "no_close_for":
                if (argument is null
                    || !long.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds)
                    || milliseconds < 0)
                    break;

                effect = WindowRuleEffect.NoCloseFor;
                placement = new RulePlacement(Milliseconds: milliseconds);
                return true;
            default:
                return false;
        }

        error = "Malformed window rule.";
        return false;
    }

    // move and center clear each other, as the last matching one wins.
    private static MapPlacement Place(MapPlacement current, WindowRule rule) => rule.Effect switch
    {
        WindowRuleEffect.Move => current with { Move = rule.Placement?.Expression, Center = false },
        WindowRuleEffect.Size => current with { Size = rule.Placement?.Expression },
        WindowRuleEffect.Center => current with { Center = rule.Enabled, Move = null },
        WindowRuleEffect.Monitor => current with { Monitor = rule.Placement?.Monitor },
        WindowRuleEffect.NoCloseFor => current with { NoCloseFor = rule.Placement?.Milliseconds ?? 0 },
        _ => current,
    };
}
