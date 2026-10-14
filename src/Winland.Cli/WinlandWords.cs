namespace Winland.Cli;

/// <summary>One word <c>winland</c> understands.</summary>
public enum WinlandWord
{
    /// <summary>The line named nothing <c>winland</c> runs.</summary>
    None,

    /// <summary>Print the workspaces.</summary>
    Workspaces,

    /// <summary>Print the windows.</summary>
    Clients,

    /// <summary>Print the window that has the keyboard.</summary>
    ActiveWindow,

    /// <summary>Run a window-manager command.</summary>
    Dispatch,

    /// <summary>Print the words.</summary>
    Help,
}

/// <summary>What a <c>winland</c> line asked for.</summary>
/// <param name="Word">The command.</param>
/// <param name="Json">True when the line asked for JSON.</param>
/// <param name="Command">The command <see cref="WinlandWord.Dispatch"/> runs. Empty otherwise.</param>
/// <param name="Args">The words after that command.</param>
/// <param name="Error">Why the line was refused. Empty when it can run.</param>
public readonly record struct WinlandRequest(WinlandWord Word, bool Json, string Command, IReadOnlyList<string> Args, string Error);

/// <summary>Reads a <c>winland</c> line. It does not talk to the window manager.</summary>
public static class WinlandWords
{
    /// <summary>The words <c>winland</c> prints when a line names none.</summary>
    public const string Usage = """
        winland workspaces
        winland clients
        winland activewindow
        winland dispatch <command> [args]
        -j prints JSON
        """;

    /// <summary>Reads <paramref name="args"/>.</summary>
    /// <param name="args">The words after <c>winland</c>.</param>
    /// <returns>The request. <see cref="WinlandRequest.Error"/> is set when the line cannot run.</returns>
    public static WinlandRequest Read(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var json = false;
        var words = new List<string>(args.Count);
        foreach (var arg in args)
        {
            if (arg is "-j" or "--json")
            {
                json = true;
            }
            else
            {
                words.Add(arg);
            }
        }

        if (words.Count == 0)
        {
            return new WinlandRequest(WinlandWord.None, json, "", [], Usage);
        }

        if (words[0] is "-h" or "--help" or "help")
        {
            return new WinlandRequest(WinlandWord.Help, json, "", [], "");
        }

        var word = words[0];
        if (word.Equals("workspaces", StringComparison.OrdinalIgnoreCase))
        {
            return One(WinlandWord.Workspaces, json, words, "workspaces");
        }

        if (word.Equals("clients", StringComparison.OrdinalIgnoreCase))
        {
            return One(WinlandWord.Clients, json, words, "clients");
        }

        if (word.Equals("activewindow", StringComparison.OrdinalIgnoreCase))
        {
            return One(WinlandWord.ActiveWindow, json, words, "activewindow");
        }

        if (word.Equals("dispatch", StringComparison.OrdinalIgnoreCase))
        {
            if (words.Count < 2)
            {
                return new WinlandRequest(WinlandWord.None, json, "", [], "dispatch needs a command.");
            }

            var rest = new string[words.Count - 2];
            for (var i = 2; i < words.Count; i++)
            {
                rest[i - 2] = words[i];
            }

            return new WinlandRequest(WinlandWord.Dispatch, json, words[1], rest, "");
        }

        return new WinlandRequest(WinlandWord.None, json, "", [], "unknown command " + word);
    }

    private static WinlandRequest One(WinlandWord word, bool json, List<string> words, string name) =>
        words.Count == 1
            ? new WinlandRequest(word, json, "", [], "")
            : new WinlandRequest(WinlandWord.None, json, "", [], name + " takes no arguments.");
}
