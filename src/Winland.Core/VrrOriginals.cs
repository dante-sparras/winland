namespace Winland;

/// <summary>One program's graphics-preference string from before Winland changed it. Null means the value was absent.</summary>
public sealed record VrrExecutableOriginal(string Executable, string? Value);

/// <summary>Graphics preferences Winland changed, so a later start can put them back.</summary>
public sealed record VrrOriginals
{
    /// <summary>True when the account-wide value was changed. <see cref="GlobalValue"/> null means it was absent.</summary>
    public bool Global { get; init; }

    public string? GlobalValue { get; init; }

    public IReadOnlyList<VrrExecutableOriginal> Executables { get; init; } = [];

    public bool Any => Global || Executables.Count > 0;
}
