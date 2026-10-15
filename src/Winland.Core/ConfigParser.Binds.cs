using static Winland.OrdinalText;

namespace Winland;

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
            var minimum = hasDescription ? 4 : 3;
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

            if (!TryParseModifiers(parts[0], out var modifiers, out var modifierError))
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
            if (dispatcher.Length > 0 && !Dispatchers.IsKnown(dispatcher))
            {
                Warn(lineNumber, $"Unknown dispatcher '{dispatcher}'.");
                return;
            }

            var arguments = !mouse && parts.Count > index + 1 ? parts[index + 1] : "";
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

            _binds.Add(new Bind(modifiers, key, dispatcher, arguments, flags, description, _submap, _submapReset));
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

            if (!TryParseModifiers(parts[0], out var modifiers, out var modifierError))
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

        private static bool TryParseModifiers(string text, out Modifier modifiers, out string error)
        {
            modifiers = Modifier.None;
            error = "";
            if (text.Length == 0)
                return true;

            foreach (var token in text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TryParseModifier(token, out var one))
                {
                    error = $"Invalid modifier '{token}'.";
                    return false;
                }

                modifiers |= one;
            }

            return true;
        }

        private static bool TryParseModifier(string token, out Modifier modifier)
        {
            if (Is(token, "SUPER") || Is(token, "WIN") || Is(token, "LOGO") || Is(token, "MOD4"))
            {
                modifier = Modifier.Super;
                return true;
            }

            if (Is(token, "ALT") || Is(token, "MOD1"))
            {
                modifier = Modifier.Alt;
                return true;
            }

            if (Is(token, "CTRL") || Is(token, "CONTROL"))
            {
                modifier = Modifier.Ctrl;
                return true;
            }

            if (Is(token, "SHIFT"))
            {
                modifier = Modifier.Shift;
                return true;
            }

            modifier = Modifier.None;
            return false;
        }
    }
}
