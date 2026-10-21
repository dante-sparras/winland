namespace Winland.Domain;

/// <summary>The values one node of the animation tree resolves to, after inheritance.</summary>
public sealed record AnimationLeaf(bool Enabled, double Speed, string Curve, string Style);
