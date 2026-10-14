namespace Winland.Core.Shell;

/// <summary>The window that has the keyboard, as far as a chord that opens File Explorer in a terminal's directory needs it.</summary>
/// <param name="ClassName">The window's class name, or empty when no window has the keyboard.</param>
/// <param name="TerminalDirectory">
/// Reads the directory the terminal is in, from outside it. Null when the terminal gave no answer.
/// Called only when <paramref name="ClassName"/> is a terminal's, and off no thread in particular, since reading another process can wait.
/// </param>
public sealed record FrontWindow(string ClassName, Func<string?> TerminalDirectory);
