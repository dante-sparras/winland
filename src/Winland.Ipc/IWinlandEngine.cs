namespace Winland.Ipc;

/// <summary>
/// What the window manager does with a line a client sent. The pipe server calls this on its own thread, never inside
/// the keyboard hook. A call may take as long as the command takes. The hook reads only the cache <see cref="Apply"/> filled.
/// </summary>
public interface IWinlandEngine
{
    /// <summary>Replaces the cached shell state. The hook's next key reads it.</summary>
    /// <param name="state">The push.</param>
    void Apply(ShellPush state);

    /// <summary>Answers <c>workspaces</c>, <c>clients</c>, or <c>activewindow</c>.</summary>
    /// <param name="name">The query name.</param>
    /// <param name="json">True when the body is JSON. False is the text a person reads.</param>
    /// <returns>The body. An unknown name is an empty body and the server reports the error itself only when this throws.</returns>
    string Query(string name, bool json);

    /// <summary>Runs a command.</summary>
    /// <param name="command">The command word, such as <c>workspace.jump.3</c> or <c>workspace</c>.</param>
    /// <param name="args">The words after the command. Empty when the command is the whole id.</param>
    /// <returns>Null when the command ran. A sentence when it did not.</returns>
    string? Dispatch(string command, IReadOnlyList<string> args);
}
