using Winland;

namespace Winland.Tests;

/// <summary>Where a new floating Client opens: its own size, centered in the floating work area.</summary>
internal static class NewFloat
{
    public static Rectangle Centered(Rectangle area, Rectangle bounds) =>
        new(area.X + ((area.Width - bounds.Width) / 2), area.Y + ((area.Height - bounds.Height) / 2), bounds.Width, bounds.Height);
}
