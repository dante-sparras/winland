namespace Winland;

/// <summary>
/// A warning the config notification leaves out: a feature that is not honored, a feature with no Windows meaning,
/// or a window rule Winland does not apply. An unknown keyword is shown. <c>configerrors</c> and the event stream
/// still list every warning.
/// </summary>
internal static class ConfigWarnings
{
    internal static bool IsOnlyLogged(string warning) =>
        warning.Contains("not honored", StringComparison.Ordinal)
        || warning.Contains("no Windows meaning", StringComparison.Ordinal)
        || warning.StartsWith("Unsupported window rule", StringComparison.Ordinal);
}
