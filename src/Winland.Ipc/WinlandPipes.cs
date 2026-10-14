using System.Diagnostics;

namespace Winland.Ipc;

/// <summary>
/// The two per-user pipes between Winland and the shell. A pipe name is one for the whole machine, so each name carries
/// the Windows session: a second person signed in at the same time has pipes of their own. <c>CurrentUserOnly</c> keeps
/// another user in the same session from opening them.
/// </summary>
public static class WinlandPipes
{
    /// <summary>Request and reply: commands, queries, and the shell's state pushes. One line each way.</summary>
    public const string Request = "Winland.Rpc";

    /// <summary>Events Winland sends and does not ask about: workspace, focus, windows, and global-bind key edges.</summary>
    public const string Events = "Winland.Events";

    /// <summary>The request pipe for this Windows session.</summary>
    public static string RequestName => ForSession(Request);

    /// <summary>The event pipe for this Windows session.</summary>
    public static string EventName => ForSession(Events);

    /// <summary>A pipe name for this Windows session.</summary>
    /// <param name="name">The pipe's name without a session.</param>
    /// <returns><paramref name="name"/> with this session's number after a dot.</returns>
    public static string ForSession(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name + "." + SessionId();
    }

    private static int SessionId()
    {
        using var self = Process.GetCurrentProcess();
        return self.SessionId;
    }
}
