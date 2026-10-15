namespace Winland;

public static class ConfigParser
{
    public static ConfigDocument Parse(string text, IConfigSource? source = null, KeyLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new Parser(source, layout ?? KeyLayout.UnitedStates).ParseRoot(text);
    }

    private sealed class Parser(IConfigSource? sourceReader, KeyLayout layout)
    {
        private static readonly HashSet<string> KeptHandlers = new(StringComparer.Ordinal)
        {
            "exec",
            "exec-once",
            "execr",
            "execr-once",
            "exec-shutdown",
            "monitor",
            "windowrule",
            "windowrulev2",
        };

        private readonly List<Setting> _settings = [];
        private readonly List<BindDirective> _binds = [];
        private readonly List<ConfigWarning> _warnings = [];
        private readonly List<WindowRule> _windowRules = [];
        private readonly List<WorkspaceRule> _workspaceRules = [];
        private readonly List<EnvironmentAssignment> _environment = [];
        private readonly List<string> _sources = [];
        private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal);
        private readonly List<OpenCategory> _categories = [];
        private readonly HashSet<string> _openSources = new(StringComparer.Ordinal);
        private readonly List<bool> _ifFailed = [];
        private string? _source;
        private string? _submap;
        private string? _submapReset;
        private bool _suppressWarnings;

        public ConfigDocument ParseRoot(string text)
        {
            _source = null;
            ParseLines(text);
            WarnUnclosed(_categories.Count);
            return new ConfigDocument([.. _settings], [.. _binds], [.. _warnings])
            {
                WindowRules = [.. _windowRules],
                WorkspaceRules = _workspaceRules.Count == 0 ? WorkspaceRule.None : [.. _workspaceRules],
                Sources = [.. _sources],
                Environment = _environment.Count == 0 ? EnvironmentAssignment.None : [.. _environment],
            };
        }

        private void ParseLines(string text)
        {
            foreach (var (lineNumber, raw) in LogicalLines(text))
                ParseLine(lineNumber, raw);
        }

        private List<(int Line, string Text)> LogicalLines(string text)
        {
            var physical = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            var lines = new List<(int, string)>(physical.Length);
            for (var i = 0; i < physical.Length; i++)
            {
                var start = i + 1;
                var current = physical[i];
                var dangling = false;
                while (current.Length > 0 && current[^1] == '\\')
                {
                    var stem = current[..^1].TrimEnd(' ', '\t');
                    if (i + 1 >= physical.Length)
                    {
                        Warn(start, "Line ends with a backslash.");
                        dangling = true;
                        break;
                    }

                    i++;
                    current = stem + physical[i];
                }

                if (!dangling)
                    lines.Add((start, current));
            }

            return lines;
        }

        private void ParseLine(int lineNumber, string raw)
        {
            if (ConfigDirectives.TryParse(raw, out var directive))
            {
                ApplyDirective(directive, lineNumber);
                return;
            }

            if (Skipping)
                return;

            var line = CutComment(raw);
            if (line.Length == 0)
                return;

            var equals = line.IndexOf('=');
            if (equals < 0)
            {
                ParseBrace(lineNumber, line);
                return;
            }

            var lhs = line[..equals].Trim();
            var rhs = line[(equals + 1)..].Trim();
            if (lhs.Length == 0)
            {
                Warn(lineNumber, "Malformed line.");
                return;
            }

            var definingVariable = lhs[0] == '$';
            if (!TryExpand(ref rhs) || (!definingVariable && !TryExpand(ref lhs)))
            {
                Warn(lineNumber, "Variable expansion did not finish.");
                return;
            }

            if (!ConfigExpression.TryApply(ref rhs, _variables, out var expressionError))
            {
                Warn(lineNumber, expressionError);
                return;
            }

            if (definingVariable)
            {
                if (lhs.Length == 1)
                    Warn(lineNumber, "Malformed line.");
                else
                    _variables[lhs[1..]] = rhs;
                return;
            }

            if (lhs == "source")
            {
                HandleSource(rhs, lineNumber);
                return;
            }

            if (lhs == "submap")
            {
                var parts = SplitArgs(rhs, null);
                var name = parts.Count > 0 ? parts[0] : "";
                var reset = parts.Count > 1 ? parts[1] : "";
                _submap = name.Length == 0 || name == "reset" ? null : name;
                _submapReset = reset.Length == 0 ? null : reset;
                return;
            }

            if (lhs == "unbind")
            {
                HandleUnbind(rhs, lineNumber);
                return;
            }

            if (lhs.StartsWith("bind", StringComparison.Ordinal) && !lhs.Contains(':'))
            {
                HandleBind(lhs, rhs, lineNumber);
                return;
            }

            if (lhs == "monitor")
            {
                _settings.Add(new Setting(lhs, rhs));
                RememberMonitor(rhs, lineNumber);
                return;
            }

            if (lhs == "workspace")
            {
                _settings.Add(new Setting(lhs, rhs));
                RememberWorkspace(rhs, lineNumber);
                return;
            }

            if (lhs == "env")
            {
                _settings.Add(new Setting(lhs, rhs));
                if (!EnvironmentParser.TryParse(rhs, out var assignment, out var error) || assignment is null)
                    Warn(lineNumber, error);
                else
                    _environment.Add(assignment);
                return;
            }

            if (KeptHandlers.Contains(lhs))
            {
                _settings.Add(new Setting(lhs, rhs));
                if (lhs is "windowrule" or "windowrulev2")
                    RememberRule(lhs, rhs, lineNumber);
                else if (IsLaunchKeyword(lhs))
                {
                    foreach (var warning in LaunchParser.Parse(rhs, allowsElevation: true, raw: false).Warnings)
                        Warn(lineNumber, warning);
                }

                return;
            }

            if (lhs == "plugin")
            {
                _settings.Add(new Setting("plugin", rhs));
                return;
            }

            var keyword = KeyedName.TryInline(lhs, out var inline) ? inline : Qualify(lhs);
            if (PluginKeywords.TryApply(keyword, rhs, out var pluginWarnings))
            {
                _settings.Add(new Setting(keyword, rhs));
                foreach (var warning in pluginWarnings)
                    Warn(lineNumber, warning);
                return;
            }
            if (ConfigGaps.IsGap(keyword) || !HonoredKeywords.Contains(keyword))
            {
                Warn(lineNumber, $"Unsupported keyword '{keyword}'.");
                return;
            }

            _settings.Add(new Setting(keyword, rhs));
            if (keyword == LayoutOptions.LayoutKeyword && !LayoutOptions.HonorsLayout(rhs))
                Warn(lineNumber, $"Layout '{rhs}' is not honored.");
            else if (IsBorderColor(keyword) && BorderColor.TryParse(rhs, out _, out var gradient) && gradient)
                Warn(lineNumber, "Gradient borders are not honored.");
            else if (keyword == "input:accel_profile" && rhs.Equals("custom", StringComparison.OrdinalIgnoreCase))
                Warn(lineNumber, "Custom acceleration profiles are not honored.");
        }

        private void ParseBrace(int lineNumber, string line)
        {
            if (line.Contains('}'))
            {
                if (line != "}" || _categories.Count == 0)
                {
                    Warn(lineNumber, "Malformed line.");
                    return;
                }

                _categories.RemoveAt(_categories.Count - 1);
                return;
            }

            if (!line.EndsWith('{'))
            {
                Warn(lineNumber, "Malformed line.");
                return;
            }

            var name = line[..^1].Trim();
            if (name.Length == 0)
            {
                Warn(lineNumber, "Malformed line.");
                return;
            }

            if (KeyedName.TrySplit(name, out var category, out var key))
                _categories.Add(new OpenCategory(category, lineNumber, _source, key));
            else
                _categories.Add(new OpenCategory(name, lineNumber, _source));
        }

        private void HandleSource(string path, int lineNumber)
        {
            if (path.Length == 0)
            {
                Warn(lineNumber, "Malformed line.");
                return;
            }

            path = SourcePath.Expand(path);
            if (SourcePath.HasGlob(path))
            {
                var matches = sourceReader?.Match(path) ?? [];
                if (matches.Count == 0)
                {
                    Warn(lineNumber, $"Could not read source '{path}'.");
                    return;
                }

                foreach (var match in matches)
                    IncludeSource(match, lineNumber);
                return;
            }

            IncludeSource(path, lineNumber);
        }

        private void IncludeSource(string path, int lineNumber)
        {
            _sources.Add(path);
            if (sourceReader?.Read(path) is not string text)
            {
                Warn(lineNumber, $"Could not read source '{path}'.");
                return;
            }

            if (!_openSources.Add(path))
            {
                Warn(lineNumber, $"Source '{path}' includes itself.");
                return;
            }

            var depth = _categories.Count;
            var previous = _source;
            _source = path;
            try
            {
                ParseLines(text);
            }
            finally
            {
                _source = previous;
                _openSources.Remove(path);
            }

            WarnUnclosed(_categories.Count - depth);
        }

        private void WarnUnclosed(int extra)
        {
            while (extra > 0)
            {
                var category = _categories[^1];
                var label = category.Key is null ? category.Name : $"{category.Name}[{category.Key}]";
                _warnings.Add(new ConfigWarning(category.Line, $"Unclosed category '{label}'.", category.Source));
                _categories.RemoveAt(_categories.Count - 1);
                extra--;
            }
        }

        private void ApplyDirective(ConfigDirective directive, int lineNumber)
        {
            switch (directive.Kind)
            {
                case ConfigDirectiveKind.EndIf:
                    if (_ifFailed.Count == 0)
                        Warn(lineNumber, "Stray endif.");
                    else
                        _ifFailed.RemoveAt(_ifFailed.Count - 1);
                    break;
                case ConfigDirectiveKind.If:
                    if (directive.Argument.Length == 0)
                    {
                        Warn(lineNumber, "Malformed conditional.");
                        break;
                    }

                    var truthy = IsTruthy(directive.Argument);
                    _ifFailed.Add(directive.Negated ? truthy : !truthy);
                    break;
                case ConfigDirectiveKind.NoError:
                    _suppressWarnings = directive.Enabled;
                    break;
            }
        }

        private bool IsTruthy(string name)
        {
            var environment = Environment.GetEnvironmentVariable(name);
            if (environment is not null)
                return environment.Length > 0;

            return _variables.TryGetValue(name, out var value) && value.Length > 0;
        }

        private bool Skipping => _ifFailed.Contains(true);

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

        private string Qualify(string lhs)
        {
            if (_categories.Count == 0)
                return lhs;

            var parts = new string[_categories.Count + 1];
            for (var i = 0; i < _categories.Count; i++)
                parts[i] = _categories[i].Name;
            parts[^1] = lhs;
            return string.Join(':', parts);
        }

        private bool TryExpand(ref string text)
        {
            for (var iteration = 0; iteration < 100; iteration++)
            {
                var any = false;
                foreach (var variable in _variables.OrderByDescending(variable => variable.Key.Length))
                {
                    var token = "$" + variable.Key;
                    if (text.IndexOf(token, StringComparison.Ordinal) < 0)
                        continue;

                    text = text.Replace(token, variable.Value, StringComparison.Ordinal);
                    any = true;
                }

                if (!any)
                    return true;
            }

            return false;
        }

        private void RememberMonitor(string value, int lineNumber)
        {
            if (!MonitorRules.TryParse(value, out _, out var warnings) && warnings.Count == 0)
                Warn(lineNumber, "Malformed monitor rule.");

            foreach (var warning in warnings)
                Warn(lineNumber, warning);
        }

        private void RememberWorkspace(string value, int lineNumber)
        {
            var warnings = new List<string>();
            var rule = WorkspaceRules.Parse(value, warnings);
            foreach (var warning in warnings)
                Warn(lineNumber, warning);

            if (rule is not null)
                WorkspaceRules.ReplaceOrAdd(_workspaceRules, rule);
        }

        private void RememberRule(string keyword, string value, int lineNumber)
        {
            if (!WindowRuleParser.TryParse(keyword, value, out var rule, out var error) || rule is null)
                Warn(lineNumber, error);
            else
                _windowRules.Add(rule);
        }

        private void Warn(int line, string message)
        {
            if (_suppressWarnings)
                return;

            _warnings.Add(new ConfigWarning(line, message, _source));
        }

        private static bool IsBorderColor(string keyword) =>
            keyword is "general:col.active_border" or "general:col.inactive_border";

        private static bool IsLaunchKeyword(string keyword) =>
            keyword is "exec" or "exec-once" or "execr" or "execr-once" or "exec-shutdown";

        private static string CutComment(string line)
        {
            line = line.Trim();
            if (line.Length == 0 || line[0] == '#')
                return "";

            var searchFrom = 0;
            while (true)
            {
                var hash = line.IndexOf('#', searchFrom);
                if (hash < 0)
                    break;

                if (hash + 1 < line.Length && line[hash + 1] == '#')
                {
                    line = string.Concat(line.AsSpan(0, hash + 1), line.AsSpan(hash + 2));
                    searchFrom = hash + 1;
                    continue;
                }

                line = line[..hash];
                break;
            }

            return line.Trim();
        }

        private static List<string> SplitArgs(string value, int? maxFields)
        {
            var parts = new List<string>();
            var start = 0;
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] != ',')
                    continue;

                if (maxFields is int max && parts.Count == max - 1)
                    break;

                parts.Add(value[start..i].Trim());
                start = i + 1;
            }

            parts.Add(value[start..].Trim());
            return parts;
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

        private static bool Is(string token, string expected) =>
            token.Equals(expected, StringComparison.OrdinalIgnoreCase);

        private readonly record struct OpenCategory(string Name, int Line, string? Source, string? Key = null);
    }
}
