using System.Text.Json;

namespace Winland.Ipc;

/// <summary>The request, reply, and event lines. Each is one JSON object.</summary>
public static class Wire
{
    /// <summary>A query.</summary>
    /// <param name="name"><c>workspaces</c>, <c>clients</c>, or <c>activewindow</c>.</param>
    /// <param name="json">True when the answer should be JSON.</param>
    /// <returns>The line.</returns>
    public static string Query(string name, bool json) => JsonLine.Object(("kind", "query"), ("name", name), ("json", json));

    /// <summary>A command.</summary>
    /// <param name="command">The command word.</param>
    /// <param name="args">The words after it.</param>
    /// <returns>The line.</returns>
    public static string Dispatch(string command, IReadOnlyList<string> args) =>
        JsonLine.Object(("kind", "dispatch"), ("command", command), ("args", args as string[] ?? [.. args]));

    /// <summary>A reply that names no body.</summary>
    /// <returns>The line.</returns>
    public static string Ok() => JsonLine.Object(("kind", "ok"));

    /// <summary>A reply that says why a command did not run.</summary>
    /// <param name="message">The sentence.</param>
    /// <returns>The line.</returns>
    public static string Error(string message) => JsonLine.Object(("kind", "error"), ("message", message));

    /// <summary>A query's answer.</summary>
    /// <param name="body">Text or JSON, as the query asked.</param>
    /// <returns>The line.</returns>
    public static string Result(string body) => JsonLine.Object(("kind", "result"), ("body", body));

    /// <summary>A global bind's key went down or came up.</summary>
    /// <param name="name">The opaque name.</param>
    /// <param name="down">True when the key went down.</param>
    /// <param name="hold">True when the bind is a hold.</param>
    /// <returns>The line.</returns>
    public static string Bind(string name, bool down, bool hold) =>
        JsonLine.Object(("kind", "bind"), ("name", name), ("down", down), ("hold", hold));

    /// <summary>The picker took a key.</summary>
    /// <param name="key">The id the shell pushed for that key.</param>
    /// <returns>The line.</returns>
    public static string Picker(int key) => JsonLine.Object(("kind", "picker"), ("key", key));

    /// <summary>The window manager's scene changed. <paramref name="body"/> is the JSON the shell paints from.</summary>
    /// <param name="kind"><c>workspace</c>, <c>focus</c>, <c>windows</c>, or <c>placed</c>.</param>
    /// <param name="body">The JSON document, or empty when the shell should query.</param>
    /// <returns>The line.</returns>
    public static string Scene(string kind, string body) => JsonLine.Object(("kind", kind), ("body", body));

    /// <summary>Reads a reply's error sentence.</summary>
    /// <param name="line">A reply.</param>
    /// <param name="message">The sentence, when the reply is an error.</param>
    /// <returns>True when the reply is <c>error</c>.</returns>
    public static bool TryError(string? line, out string message)
    {
        message = "";
        if (!JsonLine.TryParse(line, out var document))
        {
            return false;
        }

        using (document)
        {
            if (!JsonLine.Kind(document.RootElement).Equals("error", StringComparison.Ordinal))
            {
                return false;
            }

            message = JsonLine.Text(document.RootElement, "message");
            return true;
        }
    }

    /// <summary>Reads a query reply's body.</summary>
    /// <param name="line">A reply.</param>
    /// <param name="body">The body, when the reply is a result.</param>
    /// <returns>True when the reply is <c>result</c>.</returns>
    public static bool TryResult(string? line, out string body)
    {
        body = "";
        if (!JsonLine.TryParse(line, out var document))
        {
            return false;
        }

        using (document)
        {
            if (!JsonLine.Kind(document.RootElement).Equals("result", StringComparison.Ordinal))
            {
                return false;
            }

            body = JsonLine.Text(document.RootElement, "body");
            return true;
        }
    }

    /// <summary>Reads an event line.</summary>
    /// <param name="line">One event.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="root">The object. The caller disposes the document only through <paramref name="document"/>.</param>
    /// <param name="document">The document.</param>
    /// <returns>False when the line is not an object.</returns>
    public static bool TryEvent(string? line, out string kind, out JsonElement root, out JsonDocument? document)
    {
        kind = "";
        root = default;
        document = null;
        if (!JsonLine.TryParse(line, out var parsed))
        {
            return false;
        }

        document = parsed;
        root = parsed.RootElement;
        kind = JsonLine.Kind(root);
        return kind.Length > 0;
    }
}
