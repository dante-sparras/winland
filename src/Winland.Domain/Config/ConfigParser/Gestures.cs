using System.Globalization;

namespace Winland.Domain;

public static partial class ConfigParser
{
    private sealed partial class Parser
    {
        private void HandleGesture(string lhs, string rhs, int lineNumber)
        {
            if (lhs != "gesture")
            {
                Warn(lineNumber, $"Invalid gesture flag '{lhs["gesture".Length]}'.");
                return;
            }

            var parts = SplitArgs(rhs, null);
            if (parts.Count != 3)
            {
                Warn(lineNumber, "A gesture is <fingers>, horizontal or vertical, workspace.");
                return;
            }

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var fingers) || fingers is <= 1 or >= 10)
            {
                Warn(lineNumber, $"Invalid value {parts[0]} for finger count.");
                return;
            }

            if (!TrackpadGesture.TryAxis(parts[1], out var axis))
            {
                Warn(lineNumber, $"Invalid direction: {parts[1]}.");
                return;
            }

            if (parts[2] != "workspace")
            {
                Warn(lineNumber, $"Invalid gesture: {parts[2]}.");
                return;
            }

            _gestures.Add(new TrackpadGesture(fingers, axis));
        }
    }
}
