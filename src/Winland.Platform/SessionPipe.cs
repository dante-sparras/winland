using System.Diagnostics;

namespace Winland.Platform;

/// <summary>
/// A pipe name for this Windows session. The name is one for the whole PC, so a pipe that belongs to one desktop
/// carries the session's number. The text matches the shell's session pipes.
/// </summary>
internal static class SessionPipe
{
    /// <summary><paramref name="name"/> with this session's number after a dot.</summary>
    /// <param name="name">The pipe's name without a session.</param>
    /// <returns>The session's pipe name.</returns>
    internal static string For(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var self = Process.GetCurrentProcess();
        return name + "." + self.SessionId;
    }
}
