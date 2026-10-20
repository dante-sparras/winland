namespace Winland;

/// <summary>
/// Tells the config warnings that a keyword or feature is a Gap from the ones the pinned release would also report as
/// errors. A Gap notice names a Gap keyword, gives a Gap's reason, or says that what it names is not honored or has no
/// Windows meaning, as every Gap warning does.
/// </summary>
internal static class ConfigWarnings
{
    private const string UnsupportedKeyword = "Unsupported keyword '";

    internal static bool IsGapNotice(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.StartsWith(UnsupportedKeyword, StringComparison.Ordinal))
        {
            var end = message.IndexOf('\'', UnsupportedKeyword.Length);
            return end > UnsupportedKeyword.Length && ConfigGaps.IsGap(message[UnsupportedKeyword.Length..end]);
        }

        return message.Contains("not honored", StringComparison.Ordinal)
            || message.Contains("no Windows meaning", StringComparison.Ordinal)
            || message.StartsWith("Unsupported window rule", StringComparison.Ordinal)
            || ConfigGaps.IsReason(message);
    }
}
