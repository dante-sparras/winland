namespace Winland.Domain;

/// <summary>One <c>gesture</c> line: a swipe of this many fingers along an axis switches Workspace.</summary>
public sealed record TrackpadGesture(int Fingers, GestureAxis Axis)
{
    internal static bool TryAxis(string text, out GestureAxis axis)
    {
        switch (text.ToLowerInvariant())
        {
            case "horizontal":
                axis = GestureAxis.Horizontal;
                return true;
            case "vertical":
                axis = GestureAxis.Vertical;
                return true;
            default:
                axis = default;
                return false;
        }
    }
}

public enum GestureAxis
{
    Horizontal,
    Vertical,
}
