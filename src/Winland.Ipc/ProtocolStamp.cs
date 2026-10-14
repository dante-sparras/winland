using System.Security.Cryptography;
using System.Text;

namespace Winland.Ipc;

/// <summary>
/// The handshake stamp: a hash of the protocol, not of the git commit.
/// Two builds that speak the same contract connect. A change to a message kind or its shape refuses.
/// </summary>
public static class ProtocolStamp
{
    /// <summary>
    /// The contract files, in order. Each one names a message kind or the shape of a line.
    /// The build hashes these and no other file in <c>Winland.Ipc</c>.
    /// </summary>
    public static readonly string[] Files =
    [
        "Handshake.cs",
        "JsonLine.cs",
        "LineCodec.cs",
        "ShellPush.cs",
        "Wire.cs",
        "WmPicture.cs",
    ];

    /// <summary>The lowercase hex SHA-256 of <paramref name="texts"/>, in order, with line endings folded to <c>\n</c>.</summary>
    /// <param name="texts">The contract sources, in <see cref="Files"/> order.</param>
    /// <returns>64 hex characters.</returns>
    public static string Hash(IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var bytes = Encoding.UTF8.GetBytes(Canonical(texts));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>The bytes the hash covers, as text: each source with <c>\n</c> line endings, then one more <c>\n</c>.</summary>
    /// <param name="texts">The contract sources, in order.</param>
    /// <returns>The canonical text.</returns>
    public static string Canonical(IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var builder = new StringBuilder();
        foreach (var text in texts)
        {
            builder.Append(Normalize(text ?? ""));
            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Folds <c>\r\n</c> and a lone <c>\r</c> to <c>\n</c>, so the stamp does not depend on the checkout's line endings.</summary>
    /// <param name="text">One source file.</param>
    /// <returns>The text with <c>\n</c> line endings.</returns>
    public static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
