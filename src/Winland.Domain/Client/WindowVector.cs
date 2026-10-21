using System.Globalization;

namespace Winland.Domain;

/// <summary>
/// The argument of <c>movewindowpixel</c> and <c>resizewindowpixel</c>: two numbers added to an origin,
/// or <c>exact</c> and two numbers that replace it. A trailing <c>%</c> is a percentage of the origin,
/// or of the Monitor when exact. Text without two numbers keeps the origin.
/// </summary>
internal static class WindowVector
{
    public static Point Resolve(string text, Point origin, Size monitor)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
            return origin;

        var exact = tokens[0] == "exact";
        if (exact && tokens.Length < 3)
            return origin;

        var x = exact ? tokens[1] : tokens[0];
        var y = exact ? tokens[2] : tokens[1];
        if (!TryNumber(x, out var xValue, out var xPercent) || !TryNumber(y, out var yValue, out var yPercent))
            return origin;

        if (exact)
        {
            return new Point(
                xPercent ? (int)(xValue * 0.01 * monitor.Width) : (int)xValue,
                yPercent ? (int)(yValue * 0.01 * monitor.Height) : (int)yValue);
        }

        return new Point(
            xPercent ? (int)((xValue * 0.01 * origin.X) + origin.X) : (int)xValue + origin.X,
            yPercent ? (int)((yValue * 0.01 * origin.Y) + origin.Y) : (int)yValue + origin.Y);
    }

    private static bool TryNumber(string token, out double value, out bool percent)
    {
        percent = token.Contains('%', StringComparison.Ordinal);
        if (percent)
            token = token[..^1];

        value = 0;
        if (token.Length == 0)
            return false;

        var start = token[0] == '-' ? 1 : 0;
        if (start == token.Length)
            return false;

        var dots = 0;
        for (var i = start; i < token.Length; i++)
        {
            if (token[i] == '.')
                dots++;
            else if (!char.IsAsciiDigit(token[i]))
                return false;
        }

        return dots <= 1 && double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
