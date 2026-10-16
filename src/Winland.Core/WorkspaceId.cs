namespace Winland;

public readonly record struct WorkspaceId(string Name)
{
    public const int SpecialStart = -99;

    public static WorkspaceId Initial { get; } = new("1");

    /// <summary>The unnamed special workspace, addressed as <c>special</c> or <c>special:</c>.</summary>
    public static WorkspaceId DefaultSpecial { get; } = new("special:special");

    public bool IsSpecial => Name.StartsWith("special:", StringComparison.Ordinal);

    public bool TryGetNumber(out int number)
    {
        if (int.TryParse(Name, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number)
            && number > 0
            && number.ToString(System.Globalization.CultureInfo.InvariantCulture) == Name)
            return true;

        number = 0;
        return false;
    }

    public static WorkspaceId FromNumber(int number)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        return new(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

public readonly record struct MonitorId(int Value)
{
    public static MonitorId Primary { get; } = new(0);
}

public sealed record Workspace(WorkspaceId Id, MonitorId Monitor)
{
    public WindowId? LastWindow { get; init; }

    public bool Persistent { get; init; }

    public string? DisplayName { get; init; }

    /// <summary>The name of the Layout that tiles this Workspace, as <c>general:layout</c> spells it.</summary>
    public string TiledLayout { get; init; } = "dwindle";
}
