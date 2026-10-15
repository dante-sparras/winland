namespace Winland;

public static partial class ConfigParser
{
    public static ConfigDocument Parse(string text, IConfigSource? source = null, KeyLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new Parser(source, layout ?? KeyLayout.UnitedStates).ParseRoot(text);
    }

    private sealed partial class Parser(IConfigSource? sourceReader, KeyLayout layout)
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
        private readonly List<TrackpadGesture> _gestures = [];
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
                Gestures = [.. _gestures],
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

            if (lhs.StartsWith("gesture", StringComparison.Ordinal) && !lhs.Contains(':'))
            {
                HandleGesture(lhs, rhs, lineNumber);
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

            if (TryAnimationLine(lhs, rhs, lineNumber))
                return;

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

        private readonly record struct OpenCategory(string Name, int Line, string? Source, string? Key = null);
    }
}
