using System.Globalization;
using static Winland.OrdinalText;

namespace Winland;

/// <summary>The arguments of <c>resizeactive</c>: two whole pixel numbers, after an optional <c>exact</c>.</summary>
internal static class ResizeArguments
{
    public static bool TryParse(string arguments, out int x, out int y, out bool exact)
    {
        x = 0;
        y = 0;
        exact = false;
        var tokens = arguments.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var index = 0;
        if (tokens.Length > 0 && Is(tokens[0], "exact"))
        {
            exact = true;
            index = 1;
        }

        if (tokens.Length - index < 2)
            return false;

        return int.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
            && int.TryParse(tokens[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
    }
}
