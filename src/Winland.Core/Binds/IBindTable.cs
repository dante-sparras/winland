namespace Winland.Core.Binds;

/// <summary>The binds Winland is currently honoring.</summary>
public interface IBindTable
{
    /// <summary>Where <paramref name="chord"/> goes, or null when Winland does not own it.</summary>
    /// <param name="chord">A chord.</param>
    /// <returns>The dispatch, or null.</returns>
    Dispatch? Resolve(Chord chord);

    /// <summary>True when the chord is held: both key edges are delivered.</summary>
    /// <param name="chord">A chord.</param>
    /// <returns>True when the bind is a hold.</returns>
    bool IsHold(Chord chord);

    /// <summary>Where a held <paramref name="chord"/> goes on each edge, or null when it is not a hold.</summary>
    /// <param name="chord">A chord.</param>
    /// <returns>The dispatch, or null.</returns>
    Dispatch? ResolveHold(Chord chord);
}
