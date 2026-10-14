namespace Winland.Core.Binds;

/// <summary>Where a bind sends its chord.</summary>
public abstract record Dispatch
{
    /// <summary>Winland runs <paramref name="Command"/> itself.</summary>
    /// <param name="Command">A window, workspace, or layout command.</param>
    public sealed record Local(WinlandCommand Command) : Dispatch;

    /// <summary>
    /// The shell named <paramref name="Name"/> hears the key.
    /// Winland does not interpret the name. <see cref="GlobalName"/> builds the two names this desktop uses.
    /// </summary>
    /// <param name="Name">An opaque shortcut name, such as <c>winshell:winoma.dictation:toggle</c>.</param>
    public sealed record Global(string Name) : Dispatch;
}
