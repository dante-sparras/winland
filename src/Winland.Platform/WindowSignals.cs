namespace Winland.Platform;

/// <summary>Bumps when a top-level window is shown, hidden, destroyed, cloaked, or uncloaked.</summary>
internal static class WindowSignals
{
    private static int _generation;

    /// <summary>The current generation. The bar reads windows again when this changes.</summary>
    public static int Generation => Volatile.Read(ref _generation);

    /// <summary>Records one window event.</summary>
    public static void Bump() => Interlocked.Increment(ref _generation);
}
