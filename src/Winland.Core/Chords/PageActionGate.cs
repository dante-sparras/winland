namespace Winland.Core.Chords;

/// <summary>The program that has the keyboard, as the desktop's hook asks for it on a key.</summary>
public interface IFrontProgram
{
    /// <summary>The executable of the window that has the keyboard: a file name or a full path. Null or empty when it cannot be told.</summary>
    string? Program { get; }
}

/// <summary>
/// A command that acts on the page in front of a browser, and is Winoma's only while Chrome, Edge, or Brave has the keyboard.
/// Anywhere else its key goes to the program. Firefox is not one of them: Winoma installs no extension there.
/// </summary>
public static class PageActions
{
    private static readonly string[] Browsers = ["chrome", "msedge", "brave"];

    /// <summary>True when <paramref name="command"/> is a Page action.</summary>
    /// <param name="command">A resolved chord command.</param>
    /// <returns>True for a Page action.</returns>
    public static bool Is(ShellCommand? command) =>
        command is ShellCommand.Host { Action: HostAction.CopyPageAddress or HostAction.DownloadPageVideo };

    /// <summary>True when <paramref name="program"/> is Chrome, Edge, or Brave, which a Web app window of theirs also is.</summary>
    /// <param name="program">An executable file name or path.</param>
    /// <returns>True for one of the three.</returns>
    public static bool TakesPageActions(string? program) =>
        !string.IsNullOrWhiteSpace(program)
        && Array.Exists(Browsers, browser => string.Equals(Path.GetFileNameWithoutExtension(program), browser, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The chord table with one gate in front of it: a Page action resolves only while <see cref="PageActions.TakesPageActions"/> holds
/// for the program in front. Every other chord, and every chord on every other program, resolves as the inner table does.
/// The program is asked only for a Page action's key, so no other key pays for it.
/// </summary>
public sealed class PageActionGate : IChordTable
{
    private readonly IChordTable _inner;
    private readonly IFrontProgram _front;

    /// <summary>Gates <paramref name="inner"/> on <paramref name="front"/>.</summary>
    /// <param name="inner">The whole chord table.</param>
    /// <param name="front">The program that has the keyboard.</param>
    public PageActionGate(IChordTable inner, IFrontProgram front)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(front);
        _inner = inner;
        _front = front;
    }

    /// <inheritdoc />
    public ShellCommand? Resolve(Chord chord)
    {
        var command = _inner.Resolve(chord);
        return PageActions.Is(command) && !PageActions.TakesPageActions(_front.Program) ? null : command;
    }

    /// <inheritdoc />
    public bool IsHold(Chord chord) => _inner.IsHold(chord);

    /// <inheritdoc />
    public ShellCommand? ResolveHold(Chord chord) => _inner.ResolveHold(chord);
}
