namespace Winland;

/// <summary>
/// A comma in a value the Lua config writes into a line whose fields are split at commas. The Lua writes this
/// private-use character in its place, and the parser turns it back into a comma after the split.
/// </summary>
public static class LineText
{
    public const char Comma = '';

    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace(',', Comma);
    }

    public static string Unescape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace(Comma, ',');
    }
}
