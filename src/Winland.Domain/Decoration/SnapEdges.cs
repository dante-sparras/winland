namespace Winland.Domain;

internal readonly record struct SnapEdges(bool Left, bool Top, bool Right, bool Bottom)
{
    public static SnapEdges All { get; } = new(true, true, true, true);

    public static SnapEdges FromGrab(bool left, bool top) => new(left, top, !left, !top);
}
