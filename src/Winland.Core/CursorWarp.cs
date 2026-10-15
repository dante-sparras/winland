namespace Winland;

public readonly record struct WarpTo(Point Position);

public static class CursorWarp
{
    public static WarpTo? Place(int mode, bool noWarps, bool persistent, Rectangle bounds, Point? relative)
    {
        if (mode <= 0)
            return null;

        if (noWarps && mode < 2)
            return null;

        if (persistent
            && relative is Point offset
            && offset.X > 0
            && offset.Y > 0
            && offset.X < bounds.Width
            && offset.Y < bounds.Height)
            return new WarpTo(new Point(bounds.X + offset.X, bounds.Y + offset.Y));

        return new WarpTo(new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2)));
    }

    public static bool Names(Monitor monitor, string selector)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (string.IsNullOrWhiteSpace(selector))
            return false;

        var text = selector.Trim();
        const string Description = "desc:";
        if (text.StartsWith(Description, StringComparison.OrdinalIgnoreCase))
        {
            var description = text[Description.Length..];
            return description.Length > 0 && monitor.Description.StartsWith(description, StringComparison.Ordinal);
        }

        return monitor.Name.Equals(text, StringComparison.OrdinalIgnoreCase)
            || monitor.Description.Equals(text, StringComparison.Ordinal);
    }
}
