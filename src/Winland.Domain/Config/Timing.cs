namespace Winland.Domain;

/// <summary>How long one animation runs and how it eases.</summary>
public readonly record struct Timing(Easing Curve, double Milliseconds);
