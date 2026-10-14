namespace Winland.Core.Shell;

/// <summary>Which focus is being asked for.</summary>
public enum FocusKind
{
    /// <summary>A direction focus, an activate of an ordinary window, or releasing focus to the shell.</summary>
    Workspace,

    /// <summary>An elevated window. The helper performs this when it is running. The desktop performs it when the helper is not.</summary>
    Helper,
}

/// <summary>One step in a focus plan.</summary>
/// <param name="Join">True when this step may join another thread's input.</param>
public readonly record struct FocusAttempt(bool Join);

/// <summary>
/// The attempts a focus runs.
/// The next attempt runs only after the previous one left another window in front.
/// The performer clears the foreground lock before that later attempt.
/// Super is the chord ledger's account. Windows is not asked whether Super is down.
/// </summary>
public static class FocusPlan
{
    /// <summary>
    /// The attempts for one focus.
    /// A helper focus ignores <paramref name="superHeld"/> and never joins.
    /// </summary>
    /// <param name="kind">Which focus is being asked for.</param>
    /// <param name="superHeld">True while the chord ledger records a Super finger.</param>
    /// <returns>The attempts, in order.</returns>
    public static IReadOnlyList<FocusAttempt> For(FocusKind kind, bool superHeld) => kind switch
    {
        FocusKind.Workspace when superHeld => Pair(join: false),
        FocusKind.Workspace => Pair(join: true),
        FocusKind.Helper => Pair(join: false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static FocusAttempt[] Pair(bool join) => [new(join), new(join)];
}
