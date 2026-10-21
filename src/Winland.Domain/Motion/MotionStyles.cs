using System.Globalization;

namespace Winland.Domain;

/// <summary>How far a Workspace's windows travel and whether they fade, for one animation style.</summary>
internal readonly record struct WorkspaceShift(int Dx, int Dy, bool Fades);

/// <summary>The rectangles the window and workspace animation styles start or end on.</summary>
internal static class MotionStyles
{
    private const int PopinFloor = 5;

    /// <summary>
    /// The rectangle a window opens from, or closes to, for a <c>windows</c> style:
    /// <c>slide [top|bottom|left|right]</c>, <c>gnomed</c>, or <c>popin [N%]</c>, which is also the default.
    /// </summary>
    internal static Rectangle Window(string style, Rectangle goal, Rectangle monitor)
    {
        var words = Words(style);
        var first = words.Length > 0 ? words[0] : "";
        if (first == "slide")
            return Slide(goal, monitor, words.Length > 1 ? words[1] : "");
        if (first is "gnomed" or "gnome")
            return new Rectangle(goal.X, goal.Y + (goal.Height / 2), goal.Width, 0);

        return Popin(goal, PopinFraction(style));
    }

    /// <summary>
    /// The offset and fade of a Workspace that is shown (<paramref name="entering"/>) or hidden, for a
    /// <c>workspaces</c> style. Entering windows start at the offset; leaving windows end at it.
    /// </summary>
    internal static WorkspaceShift Workspace(string style, bool entering, bool left, bool special, Rectangle monitor, int gap = 0)
    {
        var words = Words(style);
        var vertical = style.StartsWith("slidevert", StringComparison.Ordinal) || style.StartsWith("slidefadevert", StringComparison.Ordinal);
        if (words.Length > 1)
        {
            switch (words[1])
            {
                case "top":
                    (left, vertical) = (false, true);
                    break;
                case "bottom":
                    (left, vertical) = (true, true);
                    break;
                case "left":
                    (left, vertical) = (false, false);
                    break;
                case "right":
                    (left, vertical) = (true, false);
                    break;
            }
        }

        var percent = 100.0;
        var last = words.Length > 0 ? words[^1] : "";
        if (last.EndsWith('%') && AnimationBook.TryLastPercent(last, wholeWhenNoSpace: true, out var parsed))
            percent = parsed;

        var fades = special;
        double distance;
        if (style.StartsWith("slidefade", StringComparison.Ordinal))
        {
            fades = true;
            distance = (vertical ? monitor.Height : monitor.Width) * (percent / 100);
        }
        else if (style == "fade")
        {
            return new WorkspaceShift(0, 0, Fades: true);
        }
        else
        {
            // A slide travels the monitor and gaps_workspaces; a slidefade the monitor alone.
            distance = ((vertical ? monitor.Height : monitor.Width) + gap) * (percent / 100);
        }

        var sign = entering == left ? 1 : -1;
        var travel = (int)Math.Round(sign * distance, MidpointRounding.AwayFromZero);
        return vertical ? new WorkspaceShift(0, travel, fades) : new WorkspaceShift(travel, 0, fades);
    }

    private static Rectangle Popin(Rectangle goal, double fraction)
    {
        var width = Math.Clamp((int)(goal.Width * fraction), Math.Min(PopinFloor, goal.Width), goal.Width);
        var height = Math.Clamp((int)(goal.Height * fraction), Math.Min(PopinFloor, goal.Height), goal.Height);
        return new Rectangle(
            goal.X + ((goal.Width - width) / 2),
            goal.Y + ((goal.Height - height) / 2),
            width,
            height);
    }

    private static Rectangle Slide(Rectangle goal, Rectangle monitor, string side)
    {
        var below = goal with { Y = monitor.Y + monitor.Height };
        var leftward = goal with { X = goal.X - goal.Width };
        var rightward = goal with { X = goal.X + goal.Width };
        var above = goal with { Y = monitor.Y - goal.Height };
        if (side.Length > 0)
        {
            return side switch
            {
                "bottom" => below,
                "left" => leftward,
                "right" => rightward,
                _ => above,
            };
        }

        // The window leaves by the monitor edge nearest its middle.
        var middleX = goal.X + (goal.Width / 2.0);
        var middleY = goal.Y + (goal.Height / 2.0);
        var top = middleY - monitor.Y;
        var right = monitor.X + monitor.Width - middleX;
        var bottom = monitor.Y + monitor.Height - middleY;
        var leftEdge = middleX - monitor.X;
        var nearest = Math.Min(Math.Min(top, right), Math.Min(bottom, leftEdge));
        if (nearest == bottom)
            return below;
        if (nearest == leftEdge)
            return leftward;
        if (nearest == right)
            return rightward;

        return above;
    }

    // A style that starts with % has no fraction, and neither does one without a space before it.
    private static double PopinFraction(string style)
    {
        if (style.StartsWith('%') || !style.Contains('%', StringComparison.Ordinal))
            return 0;

        return AnimationBook.TryLastPercent(style, wholeWhenNoSpace: false, out var percent)
            ? percent / 100.0
            : 0;
    }

    private static string[] Words(string style) =>
        style.ToLower(CultureInfo.InvariantCulture).Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
