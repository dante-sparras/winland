using Winland.Lua;
using Winland.Windows;

namespace Winland;

/// <summary>
/// The daemon's side of a Lua config: the Lua each read of the config leaves, held until the config it came with is
/// in use, and the world its runtime reaches (ADR 0013).
/// </summary>
static class LuaHosting
{
    private static readonly Lock Gate = new();
    private static LuaSession? _latest;

    /// <summary>Runs a Lua config and keeps its Lua for <see cref="Take"/>. A Lua that was never taken is let go.</summary>
    public static LuaTranslation Load(string code, string path, IConfigSource source)
    {
        var load = LuaConfig.Load(code, path, source);
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
}

/// <summary>What a running Lua config reaches: the commands of the pipe, once the window host runs, and the log.</summary>
sealed class LuaWorld : ILuaWorld
{
    public WindowHost? Host { get; set; }

    public string Command(string command) => Host is WindowHost host ? host.Command(command) : "error: winland is not running yet";

    public void Log(string message) => Console.Error.WriteLine(message);

    public bool? IsKeyDown(string key, int code) => Winland.Windows.KeyState.IsDown(key, code);
}
