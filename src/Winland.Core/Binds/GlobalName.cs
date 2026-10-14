namespace Winland.Core.Binds;

/// <summary>
/// Names for <see cref="Dispatch.Global"/>. Winland stores the string and does not look inside it,
/// except here, where a name is built or split. A plugin command is <c>winshell:&lt;plugin&gt;:&lt;command&gt;</c>.
/// Anything the shell does itself is <c>winoma:&lt;id&gt;</c>, and <c>id</c> is the shell's own word.
/// </summary>
public static class GlobalName
{
    /// <summary>The prefix of a plugin command delivered to the shell.</summary>
    public const string WinshellPrefix = "winshell:";

    /// <summary>The prefix of a command the shell runs itself.</summary>
    public const string WinomaPrefix = "winoma:";

    /// <summary>The global name for <paramref name="command"/> on <paramref name="plugin"/>.</summary>
    /// <param name="plugin">Plugin id, such as <c>winoma.dictation</c>.</param>
    /// <param name="command">Command name, such as <c>toggle</c>.</param>
    /// <returns><c>winshell:winoma.dictation:toggle</c>.</returns>
    public static string Plugin(string plugin, string command) => WinshellPrefix + plugin + ":" + command;

    /// <summary>The global name for a command the shell runs itself.</summary>
    /// <param name="id">The shell's command id, such as <c>lock</c> or <c>panel:winoma.audio</c>.</param>
    /// <returns><c>winoma:</c> followed by <paramref name="id"/>.</returns>
    public static string Winoma(string id) => WinomaPrefix + id;

    /// <summary>
    /// Reads <c>winshell:&lt;plugin&gt;:&lt;command&gt;</c>. The command is the text after the last colon.
    /// The plugin id may itself contain colons. Neither part may be empty or hold whitespace,
    /// and the command is letters, digits, <c>-</c>, <c>_</c>, and <c>.</c>.
    /// </summary>
    /// <param name="name">A global name.</param>
    /// <param name="plugin">The plugin id.</param>
    /// <param name="command">The command name.</param>
    /// <returns>False when <paramref name="name"/> is not a plugin global.</returns>
    public static bool TryPlugin(string? name, out string plugin, out string command)
    {
        plugin = "";
        command = "";
        if (name is null || !name.StartsWith(WinshellPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = name[WinshellPrefix.Length..];
        var colon = rest.LastIndexOf(':');
        if (colon <= 0 || colon == rest.Length - 1)
        {
            return false;
        }

        var left = rest[..colon];
        var right = rest[(colon + 1)..];
        if (!IsWord(left) || !IsCommand(right))
        {
            return false;
        }

        plugin = left;
        command = right;
        return true;
    }

    /// <summary>Reads <c>winoma:&lt;id&gt;</c>. The id is everything after the prefix and may contain colons.</summary>
    /// <param name="name">A global name.</param>
    /// <param name="id">The shell's command id.</param>
    /// <returns>False when <paramref name="name"/> is not a shell global, or the id is empty or holds whitespace.</returns>
    public static bool TryWinoma(string? name, out string id)
    {
        id = "";
        if (name is null || !name.StartsWith(WinomaPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = name[WinomaPrefix.Length..];
        if (!IsWord(rest))
        {
            return false;
        }

        id = rest;
        return true;
    }

    private static bool IsWord(string text)
    {
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        return text.Length > 0;
    }

    private static bool IsCommand(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
