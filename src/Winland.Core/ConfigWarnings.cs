namespace Winland;

/// <summary>
/// A warning the error bar leaves out: a feature that is not honored, a feature with no Windows meaning, or a window
/// rule Winland does not apply. An unknown keyword is shown.
/// </summary>
internal static class ConfigWarnings
{
    internal static bool SkipsTheErrorBar(string warning) =>
        warning.Contains("not honored", StringComparison.Ordinal)
        || warning.Contains("no Windows meaning", StringComparison.Ordinal)
        || warning.StartsWith("Unsupported window rule", StringComparison.Ordinal);
}
