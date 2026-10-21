namespace Winland.Domain;

/// <summary>The DWM corner preference for one Client, and the region to cut when no preference matches the radius.</summary>
public readonly record struct CornerChoice(CornerPreference Preference, CornerRegion? Region)
{
    public static CornerChoice Square { get; } = new(CornerPreference.DoNotRound, null);
}
