using System.Text;

namespace Winland.Core;

/// <summary>
/// The device field of a <c>bind</c> line with the <c>k</c> flag. <c>+</c> matches only the named devices and
/// <c>-</c> matches every device but those, as Winland's inclusive flag does. Names are separated by a
/// private-use character.
/// </summary>
internal static class DeviceField
{
    public const char Separator = '\uE001';

    public static string Write(bool inclusive, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var result = new StringBuilder();
        result.Append(inclusive ? '+' : '-');
        for (var i = 0; i < names.Count; i++)
        {
            if (i > 0)
                result.Append(Separator);
            result.Append(names[i]);
        }

        return result.ToString();
    }

    public static bool TryRead(string field, out bool inclusive, out string[] names)
    {
        inclusive = false;
        names = [];
        if (field.Length == 0 || (field[0] != '+' && field[0] != '-'))
            return false;

        inclusive = field[0] == '+';
        var body = field[1..];
        if (body.Length == 0)
            return true;

        var parts = body.Split(Separator);
        var list = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (part.Length > 0)
                list.Add(part);
        }

        names = [.. list];
        return true;
    }
}
