namespace Winland.Domain;

public readonly record struct MonitorId(int Value)
{
    public static MonitorId Primary { get; } = new(0);
}
