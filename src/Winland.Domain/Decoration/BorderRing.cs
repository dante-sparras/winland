namespace Winland.Domain;

/// <summary>
/// The border Winland draws around one Client, outside its frame: <see cref="Thickness"/> pixels wide,
/// in one <see cref="Color"/>, with the inner corners at <see cref="Radius"/> pixels to follow the window's own
/// and the outer corners rounded to match, and <see cref="Alpha"/> times the color's alpha.
/// </summary>
public sealed record BorderRing(BorderColor Color, int Thickness, int Radius, byte Alpha)
{
    public bool Equals(BorderRing? other) =>
        other is not null
        && Color == other.Color
        && Thickness == other.Thickness
        && Radius == other.Radius
        && Alpha == other.Alpha;

    public override int GetHashCode() => HashCode.Combine(Color, Thickness, Radius, Alpha);

    /// <summary>
    /// The outer corner radius: the inner radius plus the thickness. A square window has square outer corners too.
    /// </summary>
    public double OuterRadius => Radius <= 0 ? 0 : Radius + Thickness;
}
