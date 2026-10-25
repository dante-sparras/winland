using static Winland.Domain.OrdinalText;

namespace Winland.Domain;

public static partial class ConfigParser
{
    private sealed partial class Parser
    {
        private void HandleBind(string lhs, string rhs, int lineNumber)
        {
            if (!TryParseFlags(lhs["bind".Length..], out var flags, out var hasDescription, out var flagError))
            {
                Warn(lineNumber, flagError);
                return;
            }

            var mouse = flags.HasFlag(BindFlag.Mouse);
            var minimum = 3 + (hasDescription ? 1 : 0);
            var parts = mouse ? SplitArgs(rhs, null) : SplitArgs(rhs, minimum + 1);
            if (parts.Count < minimum)
            {
                Warn(lineNumber, "Bind has too few arguments.");
                return;
            }

            if (mouse && parts.Count > minimum)
            {
                Warn(lineNumber, "Bind has too many arguments.");
                return;
            }

            if (!ModifierNames.TryParse(parts[0], out var modifiers, out var modifierError))
            {
                Warn(lineNumber, modifierError);
                return;
            }

            var key = KeyNames.Resolve(parts[1], layout);
            if (key.Length == 0)
            {
                Warn(lineNumber, "Bind is missing a key.");
                return;
            }

            string? description = null;
            var index = 2;
            if (hasDescription)
            {
                description = parts[2];
                index = 3;
            }

            var dispatcher = parts[index];
            var arguments = !mouse && parts.Count > index + 1 ? parts[index + 1] : "";
            if (mouse)
            {
                // A mouse bind has no argument field. Its action carries one, as in resizewindow 1.
                (dispatcher, arguments) = SplitAction(dispatcher);
            }
            else if (Is(dispatcher, "mouse"))
            {
                // The mouse dispatcher is the bindm action in an ordinary bind. On a key it drags while the key is held.
                (dispatcher, arguments) = SplitAction(arguments.Trim());
                if (!Is(dispatcher, "movewindow") && !Is(dispatcher, "resizewindow"))
                {
                    Warn(lineNumber, "The mouse dispatcher takes movewindow or resizewindow.");
                    return;
                }

                if ((flags & (BindFlag.Repeat | BindFlag.Release | BindFlag.Locked)) != 0)
                {
                    Warn(lineNumber, "Bind flags conflict.");
                    return;
                }

                flags |= BindFlag.Mouse;
            }

            if (dispatcher.Length > 0 && !Dispatchers.IsKnown(dispatcher))
            {
                Warn(lineNumber, $"Unknown dispatcher '{dispatcher}'.");
                return;
            }

            _binds.Add(new Bind(modifiers, key, dispatcher, arguments, flags, description));
        }

        private static (string Dispatcher, string Arguments) SplitAction(string action)
        {
            var space = action.IndexOfAny([' ', '\t']);
            return space > 0 ? (action[..space], action[(space + 1)..].Trim()) : (action, "");
        }

        private void HandleUnbind(string rhs, int lineNumber)
        {
            var parts = SplitArgs(rhs, null);
            if (parts.Count == 1 && parts[0] == "all")
            {
                _binds.Add(new UnbindAll());
                return;
            }

            if (parts.Count < 2 || parts[1].Length == 0)
            {
                Warn(lineNumber, "Unbind has too few arguments.");
                return;
            }

            if (!ModifierNames.TryParse(parts[0], out var modifiers, out var modifierError))
            {
                Warn(lineNumber, modifierError);
                return;
            }

            _binds.Add(new Unbind(modifiers, KeyNames.Resolve(parts[1], layout)));
        }

        private static bool TryParseFlags(string flags, out BindFlag parsed, out bool hasDescription, out string error)
        {
            parsed = BindFlag.None;
            hasDescription = false;
            foreach (var flag in flags)
            {
                switch (flag)
                {
                    case 'l':
                        parsed |= BindFlag.Locked;
                        break;
                    case 'r':
                        parsed |= BindFlag.Release;
                        break;
                    case 'e':
                        parsed |= BindFlag.Repeat;
                        break;
                    case 'm':
                        parsed |= BindFlag.Mouse;
                        break;
                    case 'n':
                        parsed |= BindFlag.NonConsuming;
                        break;
                    case 'd':
                        hasDescription = true;
                        break;
                    default:
                        error = $"Invalid bind flag '{flag}'.";
                        return false;
                }
            }

            var release = parsed.HasFlag(BindFlag.Release);
            var repeat = parsed.HasFlag(BindFlag.Repeat);
            if (release && repeat)
            {
                error = "Bind flags conflict.";
                return false;
            }

            if (parsed.HasFlag(BindFlag.Mouse) && (repeat || release || parsed.HasFlag(BindFlag.Locked)))
            {
                error = "Bind flags conflict.";
                return false;
            }

            error = "";
            return true;
        }
    }
}
