namespace Winland.Domain;

internal static class ConfigDirectives
{
    /// <summary>Reads a <c># hyprlang noerror</c> comment, which turns warnings off or on for the lines after it.</summary>
    public static bool TryParseNoError(string raw, out bool suppress)
    {
        suppress = false;
        var line = raw.Trim();
        if (line.Length == 0 || line[0] != '#')
            return false;

        var comment = line[1..].Trim();
        const string marker = "hyprlang";
        if (!comment.StartsWith(marker, StringComparison.Ordinal))
            return false;

        if (comment.Length > marker.Length && !char.IsWhiteSpace(comment[marker.Length]))
            return false;

        var body = comment[marker.Length..].Trim();
        var hash = body.IndexOf('#');
        if (hash >= 0)
            body = body[..hash].Trim();

        var parts = body.Split([' ', '	'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] != "noerror")
            return false;

        var word = parts.Length > 1 ? parts[1] : "";
        suppress = word is "" or "true" or "yes" or "enable" or "enabled" or "set";
        return true;
    }
}
