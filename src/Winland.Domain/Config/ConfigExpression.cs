using System.Globalization;

namespace Winland.Domain;

internal static class ConfigExpression
{
    public static bool TryApply(ref string text, IReadOnlyDictionary<string, string> variables, out string error)
    {
        error = "";
        var value = text;
        while (TryFindExpression(value, out var slashStart, out var braceStart, out var slashCount))
        {
            var end = value.IndexOf("}}", braceStart + 2, StringComparison.Ordinal);
            if (end < 0)
                break;

            if (!TryEvaluate(value[(braceStart + 2)..end], variables, out var result))
            {
                error = "Invalid expression.";
                return false;
            }

            var kept = new string('\\', slashCount / 2);
            value = string.Concat(value.AsSpan(0, slashStart), kept, result, value.AsSpan(end + 2));
        }

        text = UnescapeBraces(value);
        return true;
    }

    private static bool TryFindExpression(string value, out int slashStart, out int braceStart, out int slashCount)
    {
        slashStart = 0;
        braceStart = 0;
        slashCount = 0;
        var search = 0;
        while (search < value.Length)
        {
            var found = value.IndexOf("{{", search, StringComparison.Ordinal);
            if (found < 0)
                return false;

            var slashes = 0;
            var index = found - 1;
            while (index >= 0 && value[index] == '\\')
            {
                slashes++;
                index--;
            }

            if (slashes % 2 == 0)
            {
                slashCount = slashes;
                slashStart = found - slashes;
                braceStart = found;
                return true;
            }

            search = found + 2;
        }

        return false;
    }

    private static bool TryEvaluate(string body, IReadOnlyDictionary<string, string> variables, out string formatted)
    {
        formatted = "";
        var parts = body.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[1] is not ("+" or "-" or "*" or "/"))
            return false;

        if (!TryNumber(parts[0], variables, out var left) || !TryNumber(parts[2], variables, out var right))
            return false;

        var result = parts[1][0] switch
        {
            '+' => left + right,
            '-' => left - right,
            '*' => left * right,
            '/' => left / right,
            _ => 0d,
        };
        formatted = result.ToString("G", CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryNumber(string token, IReadOnlyDictionary<string, string> variables, out double number)
    {
        if (variables.TryGetValue(token, out var value))
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static string UnescapeBraces(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\')
            {
                builder.Append(value[i]);
                continue;
            }

            var start = i;
            while (i < value.Length && value[i] == '\\')
                i++;

            var count = i - start;
            if (i < value.Length && value[i] is '{' or '}')
            {
                builder.Append('\\', count / 2);
                builder.Append(value[i]);
                continue;
            }

            builder.Append('\\', count);
            if (i < value.Length)
                i--;
        }

        return builder.ToString();
    }
}
