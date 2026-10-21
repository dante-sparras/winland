namespace Winland.Domain;

/// <summary>Active, inactive and fullscreen fractions from one <c>opacity</c> rule.</summary>
public readonly record struct OpacityRule(OpacityFactor Active, OpacityFactor Inactive, OpacityFactor Fullscreen);
