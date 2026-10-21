namespace Winland.Application;

[Flags]
public enum CommandFlags
{
    None = 0,
    Json = 1 << 0,
    Relayout = 1 << 1,
    All = 1 << 2,
}

internal static class CommandFlagReader
{
    internal static CommandFlags Strip(ref string text)
    {
        if (text.StartsWith("[[BATCH]]", StringComparison.Ordinal))
            return CommandFlags.None;

        var slash = text.IndexOf('/');
        if (slash < 0)
            return CommandFlags.None;

        for (var i = 0; i < slash; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                return CommandFlags.None;
        }

        var flags = CommandFlags.None;
        for (var i = 0; i < slash; i++)
        {
            flags |= text[i] switch
            {
                'j' => CommandFlags.Json,
                'r' => CommandFlags.Relayout,
                'a' => CommandFlags.All,
                _ => CommandFlags.None,
            };
        }

        text = text[(slash + 1)..].TrimStart();
        return flags;
    }
}
