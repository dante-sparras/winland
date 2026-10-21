using System.Globalization;

namespace Winland.Domain;

public readonly record struct EdgeInsets(int Top, int Right, int Bottom, int Left)
{
    public static EdgeInsets Zero { get; } = new(0, 0, 0, 0);

    public static EdgeInsets Uniform(int value) => new(value, value, value, value);

    public Rectangle Apply(Rectangle area)
    {
        var width = Math.Max(0, area.Width - Left - Right);
        var height = Math.Max(0, area.Height - Top - Bottom);
        return new Rectangle(area.X + Left, area.Y + Top, width, height);
    }
}
