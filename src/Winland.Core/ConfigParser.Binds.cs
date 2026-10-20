using static Winland.OrdinalText;

namespace Winland;

public static partial class ConfigParser
{
    private sealed partial class Parser
    {
        private void HandleBind(string lhs, string rhs, int lineNumber)
        {
            if (!TryParseFlags(lhs["bind".Length..], out var flags, out var hasDescription, out var hasDevice, out var flagError))
            {
                Warn(lineNumber, flagError);
                return;
            }

            var mouse = flags.HasFlag(BindFlag.Mouse);
            var minimum = 3 + (hasDescription ? 1 : 0) + (hasDevice ? 1 : 0);
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
                description = LineText.Unescape(parts[2]);
                index = 3;
            }

            IReadOnlyList<string>? devices = null;
            if (hasDevice)
            {
                if (!DeviceField.TryRead(parts[index], out var inclusive, out var names))
                {
                    Warn(lineNumber, "Bind device is invalid.");
                    return;
                }

                if (inclusive)
                    flags |= BindFlag.DeviceInclusive;
                devices = names;
                index++;
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

            if (key.Equals("catchall", StringComparison.Ordinal) && _submap is null)
            {
                Warn(lineNumber, "Catchall binds are only allowed in a submap.");
                return;
            }

            if (flags.HasFlag(BindFlag.DontInhibit))
            {
                Warn(lineNumber, "Bind flag 'p' is not honored.");
                flags &= ~BindFlag.DontInhibit;
            }

            _binds.Add(new Bind(modifiers, key, dispatcher, arguments, flags, description, _submap, _submapReset, devices));
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

        private static bool TryParseFlags(string flags, out BindFlag parsed, out bool hasDescription, out bool hasDevice, out string error)
        {
            parsed = BindFlag.None;
            hasDescription = false;
            hasDevice = false;
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
                    case 't':
                        parsed |= BindFlag.Transparent;
                        break;
                    case 'i':
                        parsed |= BindFlag.IgnoreModifiers;
                        break;
                    case 's':
                        parsed |= BindFlag.MultiKey;
                        break;
                    case 'o':
                        parsed |= BindFlag.LongPress;
                        break;
                    case 'd':
                        hasDescription = true;
                        break;
                    case 'p':
                        parsed |= BindFlag.DontInhibit;
                        break;
                    case 'c':
                        parsed |= BindFlag.Click | BindFlag.Release;
                        break;
                    case 'g':
                        parsed |= BindFlag.Drag | BindFlag.Release;
                        break;
                    case 'u':
                        parsed |= BindFlag.SubmapUniversal;
                        break;
                    case 'a':
                        parsed |= BindFlag.AutoConsuming;
                        break;
                    case 'k':
                        hasDevice = true;
                        break;
                    default:
                        error = $"Invalid bind flag '{flag}'.";
                        return false;
                }
            }

            var release = parsed.HasFlag(BindFlag.Release);
            var repeat = parsed.HasFlag(BindFlag.Repeat);
            if ((parsed.HasFlag(BindFlag.LongPress) || release) && repeat)
            {
                error = "Bind flags conflict.";
                return false;
            }

            if (parsed.HasFlag(BindFlag.Mouse) && (repeat || release || parsed.HasFlag(BindFlag.Locked)))
            {
                error = "Bind flags conflict.";
                return false;
            }

            if (parsed.HasFlag(BindFlag.Click) && parsed.HasFlag(BindFlag.Drag))
            {
                error = "Bind flags conflict.";
                return false;
            }

            error = "";
            return true;
        }
    }
}
