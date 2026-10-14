namespace Winland.Core.Chords;

/// <summary>
/// The Chords Windows will not release. Windows answers <c>Ctrl+Alt+Delete</c> before any program sees the keys,
/// so a line that binds it would never run.
/// <para>
/// <c>Super+Ctrl+Delete</c> is not kept, and the internal display Chord is on it. Whether <c>Super+Ctrl+Alt+Delete</c> reaches Winoma
/// is not proven: it holds <c>Ctrl+Alt+Delete</c>, which Windows answers first, and only Dante pressing it once with the log open can say.
/// Until then it is not kept here, and the mirror Chord has no default key (ticket 19).
/// </para>
/// </summary>
public static class WindowsReserve
{
    /// <summary>The sentence a refused line is logged with, after the chord as the person wrote it.</summary>
    public const string Reason = "Windows keeps Ctrl+Alt+Delete, and no program can bind it";

    private static readonly Chord SecureAttention = new(ChordModifiers.Ctrl | ChordModifiers.Alt, ChordKey.Delete);

    /// <summary>True when Windows keeps <paramref name="chord"/> and nothing can bind it.</summary>
    /// <param name="chord">The chord a file or a manifest names.</param>
    /// <returns>True for <c>Ctrl+Alt+Delete</c>.</returns>
    public static bool Keeps(Chord chord) => chord == SecureAttention;
}
