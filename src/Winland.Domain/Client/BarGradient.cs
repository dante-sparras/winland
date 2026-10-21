namespace Winland.Domain;

/// <summary>The colors of a tab bar gradient, as the config lists them. Two are equal when their colors are.</summary>
public sealed record BarGradient(IReadOnlyList<BorderColor> Colors)
{
    public bool Equals(BarGradient? other) => other is not null && Colors.SequenceEqual(other.Colors);

    public override int GetHashCode() => Colors.Count == 0 ? 0 : HashCode.Combine(Colors.Count, Colors[0]);
}
