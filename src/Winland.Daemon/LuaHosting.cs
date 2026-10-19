using Winland.Lua;
using Winland.Windows;

namespace Winland;

/// <summary>
/// The daemon's side of a Lua config: the Lua each read of the config leaves, held until the config it came with is
/// in use, the world its runtime reaches, and the commands the Lua answers on the pipe (ADR 0013).
/// </summary>
static class LuaHosting
{
    private static readonly Lock Gate = new();
    private static LuaSession? _latest;

    /// <summary>Runs a Lua config and keeps its Lua for <see cref="Take"/>. A Lua that was never taken is let go.</summary>
    public static LuaTranslation Load(string code, string path, Func<string, string?> read)
    {
        var load = LuaConfig.Load(code, path, read);
        LuaSession? previous;
        lock (Gate)
        {
            previous = _latest;
            _latest = load.Session;
        }

        previous?.Dispose();
        return load.Translation;
    }

    /// <summary>The Lua of the config read last, or null when that was not a Lua config.</summary>
    public static LuaSession? Take()
    {
        lock (Gate)
        {
            var session = _latest;
            _latest = null;
            return session;
        }
    }

    /// <summary>Forgets the Lua a read left, when the config read last was not a Lua config.</summary>
    public static void Clear() => Take()?.Dispose();

    /// <summary>
    /// <c>eval</c> and <c>repl</c> run Lua, and with a Lua config <c>dispatch</c> with a call in it is
    /// <c>eval 'hl.dispatch(...)'</c>, as in the pinned release. Anything else is the core's.
    /// </summary>
    public static string? Answer(LuaRuntime runtime, string line)
    {
        var text = line.Trim();

        // The flags winlandctl puts before a command, such as "j/", are not the Lua's.
        var slash = text.IndexOf('/', StringComparison.Ordinal);
        if (slash > 0 && slash <= 3 && text[..slash].All(char.IsAsciiLetterLower))
            text = text[(slash + 1)..];

        var space = text.IndexOf(' ', StringComparison.Ordinal);
        var verb = space < 0 ? text : text[..space];
        var rest = space < 0 ? "" : text[(space + 1)..].Trim();
        switch (verb)
        {
            case "eval" or "repl":
                return runtime.Running ? runtime.Eval(rest, verb == "repl") : "eval is only supported with a Lua config";
            case "dispatch" when runtime.Running && rest.Contains('(', StringComparison.Ordinal):
                return runtime.Eval($"return hl.dispatch({rest})", repl: false);
            default:
                return null;
        }
    }
}

/// <summary>What a running Lua config reaches: the commands of the pipe, once the window host runs, and the log.</summary>
sealed class LuaWorld : ILuaWorld
{
    public WindowHost? Host { get; set; }

    public string Command(string command) => Host is WindowHost host ? host.Command(command) : "error: winland is not running yet";

    public void Log(string message) => Console.Error.WriteLine(message);
}
