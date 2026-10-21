namespace Winland.Core;

public enum GestureDirection
{
    Swipe,
    Left,
    Right,
    Up,
    Down,
    Vertical,
    Horizontal,
}

/// <summary>One <c>gesture</c> line. Only <c>workspace</c> is kept; any other action warns and is skipped.</summary>
public sealed record TrackpadGesture(
    int Fingers,
    GestureDirection Direction,
    Modifier Modifiers,
    double Scale,
    string Action,
    bool DisableInhibit = false)
{
    public const string WorkspaceAction = "workspace";

    public bool IsWorkspace => Action == WorkspaceAction;

    internal static bool TryDirection(string text, out GestureDirection direction)
    {
        switch (text.ToLowerInvariant())
        {
            case "swipe":
                direction = GestureDirection.Swipe;
                return true;
            case "left" or "l":
                direction = GestureDirection.Left;
                return true;
            case "right" or "r":
                direction = GestureDirection.Right;
                return true;
            case "up" or "u" or "top" or "t":
                direction = GestureDirection.Up;
                return true;
            case "down" or "d" or "bottom" or "b":
                direction = GestureDirection.Down;
                return true;
            case "horizontal" or "horiz":
                direction = GestureDirection.Horizontal;
                return true;
            case "vertical" or "vert":
                direction = GestureDirection.Vertical;
                return true;
            default:
                direction = default;
                return false;
        }
    }

    internal static GestureDirection Axis(GestureDirection direction) => direction switch
    {
        GestureDirection.Up or GestureDirection.Down or GestureDirection.Vertical => GestureDirection.Vertical,
        GestureDirection.Left or GestureDirection.Right or GestureDirection.Horizontal => GestureDirection.Horizontal,
        _ => GestureDirection.Swipe,
    };

    /// <summary>A gesture already configured hides a new one with the same fingers and modifiers on an overlapping direction.</summary>
    internal bool Shadows(TrackpadGesture next)
    {
        if (Fingers != next.Fingers || Modifiers != next.Modifiers)
            return false;

        var axis = Axis(next.Direction);
        return Direction == axis
            || Direction == next.Direction
            || ((axis is GestureDirection.Vertical or GestureDirection.Horizontal) && Direction == GestureDirection.Swipe);
    }

    /// <summary>The distance one update moves along this direction, before the delta scale.</summary>
    internal static double Along(GestureDirection direction, double x, double y) => direction switch
    {
        GestureDirection.Left => -x,
        GestureDirection.Right or GestureDirection.Horizontal => x,
        GestureDirection.Up => -y,
        GestureDirection.Down or GestureDirection.Vertical => y,
        _ => Math.Sqrt((x * x) + (y * y)),
    };
}
