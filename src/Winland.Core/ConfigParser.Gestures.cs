using System.Globalization;

namespace Winland;

public static partial class ConfigParser
{
    private sealed partial class Parser
    {
        private static readonly HashSet<string> UnhonoredGestureActions = new(StringComparer.Ordinal)
        {
            "dispatcher",
            "resize",
            "move",
            "close",
            "float",
            "fullscreen",
            "cursorZoom",
            "scrollMove",
        };

        private void HandleGesture(string lhs, string rhs, int lineNumber)
        {
            var disableInhibit = false;
            foreach (var flag in lhs["gesture".Length..])
            {
                if (flag != 'p')
                {
                    Warn(lineNumber, $"Invalid gesture flag '{flag}'.");
                    return;
                }

                disableInhibit = true;
            }

            var parts = SplitArgs(rhs, null);
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var fingers) || fingers is <= 1 or >= 10)
            {
                Warn(lineNumber, $"Invalid value {parts[0]} for finger count.");
                return;
            }

            var directionText = parts.Count > 1 ? parts[1] : "";
            if (!TrackpadGesture.TryDirection(directionText, out var direction))
            {
                Warn(lineNumber, $"Invalid direction: {directionText}.");
                return;
            }

            var index = 2;
            var modifiers = Modifier.None;
            var scale = 1d;
            while (index < parts.Count)
            {
                var part = parts[index];
                if (part.StartsWith("mod:", StringComparison.Ordinal))
                {
                    if (!ModifierNames.TryParse(part[4..], out modifiers, out var modifierError))
                    {
                        Warn(lineNumber, modifierError);
                        return;
                    }

                    index++;
                    continue;
                }

                if (part.StartsWith("scale:", StringComparison.Ordinal))
                {
                    var text = part[6..].Trim();
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) || !double.IsFinite(scale))
                    {
                        Warn(lineNumber, $"Invalid delta scale: {text}.");
                        return;
                    }

                    scale = Math.Clamp(scale, 0.1, 10);
                    index++;
                    continue;
                }

                break;
            }

            var action = index < parts.Count ? parts[index] : "";
            var argument = action == TrackpadGesture.SpecialAction && index + 1 < parts.Count ? parts[index + 1].Trim() : "";
            var gesture = new TrackpadGesture(fingers, direction, modifiers, scale, action, disableInhibit, argument);
            if (action == "unset")
            {
                var removed = _gestures.FindIndex(old => old with { Action = "unset", Argument = "" } == gesture);
                if (removed < 0)
                    Warn(lineNumber, "Can't remove a non-existent gesture.");
                else
                    _gestures.RemoveAt(removed);
                return;
            }

            if (!gesture.IsHonored && !UnhonoredGestureActions.Contains(action))
            {
                Warn(lineNumber, $"Invalid gesture: {action}.");
                return;
            }

            if (_gestures.Find(old => old.Shadows(gesture)) is { } shadow)
            {
                Warn(lineNumber, $"Gesture will be overshadowed by a previous gesture. Previous {Name(shadow.Direction)} shadows new {Name(direction)}.");
                return;
            }

            _gestures.Add(gesture);
            if (disableInhibit)
                Warn(lineNumber, "Gesture flag 'p' is not honored.");
            if (TrackpadGesture.IsPinch(direction))
                Warn(lineNumber, "Pinch gestures are not honored.");
            else if (!gesture.IsHonored)
                Warn(lineNumber, $"Gesture action '{action}' is not honored.");
        }

        private static string Name(GestureDirection direction) => direction switch
        {
            GestureDirection.PinchIn => "PINCH_IN",
            GestureDirection.PinchOut => "PINCH_OUT",
            _ => direction.ToString().ToUpperInvariant(),
        };
    }
}
