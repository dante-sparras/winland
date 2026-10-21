namespace Winland.Domain;

/// <summary>Corners cut from the window: a radius in physical pixels and the curve of <see cref="DecorationOptions.RoundingPower"/>.</summary>
public readonly record struct CornerRegion(int Radius, float Power);
