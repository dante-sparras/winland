namespace Winland.Application.Tests;

/// <summary>Monitors as the tests report them. Every test file sees <c>Display</c> through a global using.</summary>
internal static class TestMonitors
{
    /// <summary>A Monitor whose bounds are <paramref name="area"/>, at scale 1, with no reserved area.</summary>
    public static Monitor Display(string name, string description, Rectangle area) =>
        new(name, description, area.X, area.Y, area.Width, area.Height, 1, true, area);

    /// <summary>A Monitor at the given bounds. Its work area is the whole of them unless <paramref name="workArea"/> says otherwise.</summary>
    public static Monitor Display(
        string name,
        string description,
        int x,
        int y,
        int width,
        int height,
        double scale = 1,
        Rectangle? workArea = null,
        bool enabled = true) =>
        new(name, description, x, y, width, height, scale, enabled, workArea ?? new Rectangle(x, y, width, height));

    /// <summary>A Monitor on the top row at <paramref name="x"/>, described by its name.</summary>
    public static Monitor Display(string name, int x, int width, int height) =>
        new(name, name, x, 0, width, height, 1, true, new Rectangle(x, 0, width, height));
}
