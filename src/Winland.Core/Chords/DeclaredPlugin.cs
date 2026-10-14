namespace Winland.Core.Chords;

/// <summary>
/// A plugin as far as <c>bindings.json</c> needs it. The shell maps a manifest onto this.
/// The window manager passes none, and reads a <c>plugin:</c> line by its shape.
/// </summary>
/// <param name="Id">Plugin id.</param>
/// <param name="HasPanel">True when the plugin declares a panel.</param>
/// <param name="ClonedFrom">The built-in id this plugin stands in for, or null when it does not.</param>
/// <param name="Commands">The commands the plugin declares.</param>
/// <param name="Chords">The chords a missing <c>bindings.json</c> gains from this plugin.</param>
public sealed record DeclaredPlugin(
    string Id,
    bool HasPanel,
    string? ClonedFrom,
    IReadOnlyList<DeclaredCommand> Commands,
    IReadOnlyList<DeclaredChord> Chords);

/// <summary>One command a plugin declares.</summary>
/// <param name="Name">The word after the plugin id.</param>
/// <param name="Hold">True when a chord bound to it is held.</param>
public sealed record DeclaredCommand(string Name, bool Hold);

/// <summary>A chord a plugin binds by default.</summary>
/// <param name="Chord">Chord text, such as <c>Super+Ctrl+X</c>.</param>
/// <param name="Command">A command name from the same plugin.</param>
public sealed record DeclaredChord(string Chord, string Command);
