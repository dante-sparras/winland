namespace Winland.Domain;

/// <summary>Which opacity applies to one Client. Fullscreen wins over active and inactive.</summary>
public readonly record struct OpacityInput(bool Active, bool Fullscreen = false);
