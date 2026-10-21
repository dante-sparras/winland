namespace Winland.Domain;

/// <summary>A tiled Client's minimum and maximum size, when they differ from its floating ones.</summary>
internal readonly record struct TiledLimits(Size? Min, Size? Max);
