namespace Winland.Domain;

/// <summary>One opacity fraction from a window rule. <see cref="Overridden"/> replaces the configured fraction; otherwise the two multiply.</summary>
public readonly record struct OpacityFactor(float Value, bool Overridden)
{
    public static OpacityFactor Identity { get; } = new(1f, false);

    public float Apply(float configured) => Overridden ? Value : Value * configured;
}
