namespace Winland.Core.Chords;

/// <summary>The chords Winoma owns, whether they came from the built-in map or from <c>bindings.json</c>.</summary>
public interface IChordTable
{
    /// <summary>The command for <paramref name="chord"/>, or null when Winoma does not own it.</summary>
    ShellCommand? Resolve(Chord chord);

    /// <summary>True when the chord is held: its command hears the key go down and come up.</summary>
    bool IsHold(Chord chord);

    /// <summary>The command a held <paramref name="chord"/> sends on each edge, or null when it is not a hold.</summary>
    ShellCommand? ResolveHold(Chord chord);
}
