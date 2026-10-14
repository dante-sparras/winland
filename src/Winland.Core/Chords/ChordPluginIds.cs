namespace Winland.Core.Chords;

/// <summary>The <c>plugin:</c> command ids in <c>bindings.json</c>, read by their shape. Whether a plugin declares the name is the shell's question.</summary>
public static class ChordPluginIds
{
    /// <summary>The prefix of a plugin command id.</summary>
    public const string Prefix = "plugin:";

    /// <summary>The id stored for <paramref name="name"/> on <paramref name="plugin"/>.</summary>
    /// <param name="plugin">Plugin id.</param>
    /// <param name="name">Command name.</param>
    /// <returns><c>plugin:winoma.clock:toggle</c>.</returns>
    public static string Of(string plugin, string name) => Prefix + plugin + ":" + name;

    /// <summary>Reads <c>plugin:&lt;id&gt;:&lt;name&gt;</c>. The name is after the last colon.</summary>
    /// <param name="id">A command id.</param>
    /// <param name="plugin">The plugin id.</param>
    /// <param name="name">The command name.</param>
    /// <returns>False when <paramref name="id"/> is not a plugin command.</returns>
    public static bool TryParse(string? id, out string plugin, out string name)
    {
        plugin = "";
        name = "";
        if (id is null || !id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = id[Prefix.Length..];
        var colon = rest.LastIndexOf(':');
        if (colon <= 0 || colon == rest.Length - 1)
        {
            return false;
        }

        var left = rest[..colon];
        var right = rest[(colon + 1)..];
        if (!IsWord(left) || !IsName(right))
        {
            return false;
        }

        plugin = left;
        name = right;
        return true;
    }

    /// <summary>True when <paramref name="name"/> can be a command name: letters, digits, <c>-</c>, <c>_</c>, and <c>.</c>.</summary>
    /// <param name="name">A candidate name.</param>
    /// <returns>True for a usable name.</returns>
    public static bool IsName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))
            {
                return false;
            }
        }

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
}
