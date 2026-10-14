namespace Winland.Core.Chords;

/// <summary>
/// The conditional bind for bare Escape. It is invisible until <see cref="BindConditions.Escape"/> is asked,
/// so an open panel that has focus still hears Escape. Winland does not know why the shell asked.
/// </summary>
public static class EscapeBinds
{
    /// <summary>Bare Escape.</summary>
    public static Chord Chord { get; } = new(ChordModifiers.None, ChordKey.Escape);

    /// <summary>The global the shell runs when the claim is on. The name is <c>winoma:escape</c>.</summary>
    public static Bind Create() =>
        new(Chord, new Dispatch.Global(GlobalName.Winoma("escape")), Hold: false, BindConditions.Escape);
}
