namespace Winland;

internal enum ConfigDirectiveKind
{
    If,
    EndIf,
    NoError,
}

internal readonly record struct ConfigDirective(ConfigDirectiveKind Kind, string Argument, bool Negated, bool Enabled);

internal static class ConfigDirectives
{
    public static bool TryParse(string raw, out ConfigDirective directive)
    {
        directive = default;
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

        var parts = body.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        if (parts[0] == "endif")
        {
            directive = new ConfigDirective(ConfigDirectiveKind.EndIf, "", false, false);
            return true;
        }

        if (parts[0] == "if")
        {
            var argument = parts.Length > 1 ? parts[1] : "";
            var negated = argument.Length > 0 && argument[0] == '!';
            if (negated)
                argument = argument[1..];

            directive = new ConfigDirective(ConfigDirectiveKind.If, argument, negated, false);
            return true;
        }

        if (parts[0] == "noerror")
        {
            var word = parts.Length > 1 ? parts[1] : "";
            var enabled = word is "" or "true" or "yes" or "enable" or "enabled" or "set";
            directive = new ConfigDirective(ConfigDirectiveKind.NoError, word, false, enabled);
            return true;
        }

        return false;
    }
}
