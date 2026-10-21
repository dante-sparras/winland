namespace Winland.Domain;

/// <summary>
/// A Workspace address. A numbered Workspace is that number on one Monitor. A special workspace
/// belongs to no Monitor, and its <see cref="Monitor"/> stays 0.
/// </summary>
public readonly record struct WorkspaceId(string Name, int Monitor = 0)
{
    public const int SpecialStart = -99;

    public static WorkspaceId Initial { get; } = new("1");

    /// <summary>The unnamed special workspace, addressed as <c>special</c> or <c>special:</c>.</summary>
    public static WorkspaceId DefaultSpecial { get; } = new("special:special");

    public bool IsSpecial => Name.StartsWith("special:", StringComparison.Ordinal);

    /// <summary>The highest Workspace number, as Winland keeps a number in 32 unsigned bits.</summary>
    public const long MaxNumber = uint.MaxValue;

    public bool TryGetNumber(out long number)
    {
        if (long.TryParse(Name, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number)
            && number > 0
            && number <= MaxNumber
            && number.ToString(System.Globalization.CultureInfo.InvariantCulture) == Name)
            return true;

        number = 0;
        return false;
    }

    public static WorkspaceId FromNumber(long number) => FromNumber(number, MonitorId.Primary);

    public static WorkspaceId FromNumber(long number, MonitorId monitor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, MaxNumber);
        return new(number.ToString(System.Globalization.CultureInfo.InvariantCulture), monitor.Value);
    }
}
