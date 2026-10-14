namespace Winland.Ipc;

/// <summary>
/// The first line on each pipe. The client says which protocol it speaks. The server accepts the same protocol and refuses another,
/// and both sides log a refusal. A refused client is not a peer: the server closes, and the client connects again only
/// when a Winland that speaks the same protocol is listening.
/// </summary>
public static class Handshake
{
    /// <summary>The line a client sends first.</summary>
    /// <param name="stamp">The client's protocol stamp.</param>
    /// <returns>One JSON object.</returns>
    public static string Hello(string stamp) => JsonLine.Object(("kind", "hello"), ("stamp", stamp));

    /// <summary>The line a server sends when the stamps agree.</summary>
    /// <param name="stamp">The server's protocol stamp.</param>
    /// <returns>One JSON object.</returns>
    public static string Accept(string stamp) => JsonLine.Object(("kind", "hello"), ("ok", true), ("stamp", stamp));

    /// <summary>The line a server sends when the stamps differ. It closes after this.</summary>
    /// <param name="stamp">The server's protocol stamp.</param>
    /// <returns>One JSON object.</returns>
    public static string Refuse(string stamp) => JsonLine.Object(
        ("kind", "hello"),
        ("ok", false),
        ("stamp", stamp),
        ("reason", "stamp"));

    /// <summary>What the server logs when it refuses.</summary>
    /// <param name="client">The stamp the client sent.</param>
    /// <param name="server">This protocol.</param>
    /// <returns>The sentence.</returns>
    public static string ServerRefusal(string client, string server) =>
        "refused a client that speaks another protocol: client " + client + " server " + server;

    /// <summary>What the client logs when it is refused.</summary>
    /// <param name="client">This protocol.</param>
    /// <param name="server">The stamp the server sent.</param>
    /// <returns>The sentence.</returns>
    public static string ClientRefusal(string client, string server) =>
        "refused by Winland speaking another protocol: client " + client + " server " + server;

    /// <summary>
    /// Decides a hello. A line that is not a hello is a refusal too: the peer is not this protocol.
    /// </summary>
    /// <param name="expected">The stamp this side was built with.</param>
    /// <param name="line">The peer's first line.</param>
    /// <param name="peer">The stamp the line carried. Empty when the line is not a hello.</param>
    /// <returns>True when <paramref name="peer"/> equals <paramref name="expected"/>.</returns>
    public static bool Accepts(string expected, string? line, out string peer)
    {
        peer = "";
        if (!JsonLine.TryParse(line, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!JsonLine.Kind(root).Equals("hello", StringComparison.Ordinal))
            {
                return false;
            }

            peer = JsonLine.Text(root, "stamp");
            return peer.Length > 0 && string.Equals(peer, expected, StringComparison.Ordinal);
        }
    }

    /// <summary>Reads the server's answer.</summary>
    /// <param name="line">The server's first line.</param>
    /// <param name="ok">True when the server accepted.</param>
    /// <param name="stamp">The server's stamp. Empty when the line is not a hello.</param>
    /// <returns>True when the line is a hello, accepted or refused.</returns>
    public static bool TryAnswer(string? line, out bool ok, out string stamp)
    {
        ok = false;
        stamp = "";
        if (!JsonLine.TryParse(line, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!JsonLine.Kind(root).Equals("hello", StringComparison.Ordinal))
            {
                return false;
            }

            ok = JsonLine.Flag(root, "ok");
            stamp = JsonLine.Text(root, "stamp");
            return true;
        }
    }
}
