namespace Winland.Domain;

/// <summary>What the hook does with one press, release or move while button scrolling is on.</summary>
public readonly record struct ButtonScrollStep(bool Swallow, bool Click, int WheelX, int WheelY)
{
    public static ButtonScrollStep Pass { get; } = new(false, false, 0, 0);

    public static ButtonScrollStep Taken { get; } = new(true, false, 0, 0);
}
