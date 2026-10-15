namespace Winland;

/// <summary>
/// The dwindle layout messages that are also dispatchers: <c>togglesplit</c> runs the same as
/// <c>layoutmsg togglesplit</c>.
/// </summary>
internal static class DwindleMessages
{
    public const string ToggleSplit = "togglesplit";

    public const string SwapSplit = "swapsplit";

    public const string RotateSplit = "rotatesplit";

    public const string SplitRatio = "splitratio";

    public static IReadOnlyList<string> Names { get; } = [ToggleSplit, SwapSplit, RotateSplit, SplitRatio];

    public static bool Contains(string name)
    {
        foreach (var message in Names)
        {
            if (OrdinalText.Is(name, message))
                return true;
        }

        return false;
    }
}
