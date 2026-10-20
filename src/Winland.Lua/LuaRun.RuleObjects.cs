using System.Globalization;
using KeraLua;

namespace Winland.Lua;

// The objects hl.window_rule and hl.workspace_rule return. Each is one rule: a later call with the same name adds to
// the window rule, and a later workspace rule for a workspace that already has an enabled rule merges into it.
// set_enabled turns that rule off or on. A layer rule stays the object whose methods warn.
internal sealed partial class LuaRun
{
    private sealed class LuaWindowRule(int id)
    {
        public int Id { get; } = id;

        public bool Enabled { get; set; } = true;

        public Dictionary<string, string> Matches { get; } = new(StringComparer.Ordinal);

        public List<string> Effects { get; } = [];

        public List<int> Lines { get; } = [];

        public bool Published { get; set; }
    }

    private sealed class LuaWorkspaceRule(int id, string workspace)
    {
        public int Id { get; } = id;

        public string Workspace { get; } = workspace;

        public bool Enabled { get; set; } = true;

        public List<(string Key, string Part)> Fields { get; } = [];

        public int Line { get; set; } = -1;
    }

    private readonly Dictionary<int, LuaWindowRule> _luaWindowRules = [];
    private readonly Dictionary<string, LuaWindowRule> _windowRulesByName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, LuaWorkspaceRule> _luaWorkspaceRules = [];
    private int _nextWindowRule;
    private int _nextWorkspaceRule;
    private int _makeWindowRule;
    private int _makeWorkspaceRule;

    // hl.window_rule({ name, enabled, match, effect = value, ... }). A name already used adds to that rule.
    private int AddWindowRule(IntPtr state)
    {
        const string Label = "hl.window_rule";
        if (!_lua.IsTable(1))
        {
            Warn("hl.window_rule: argument must be a table");
            return 0;
        }

        var name = StringField(1, "name") ?? "";
        if (name.Length == 0 || !_windowRulesByName.TryGetValue(name, out var rule))
        {
            rule = new LuaWindowRule(++_nextWindowRule);
            _luaWindowRules[rule.Id] = rule;
            if (name.Length > 0)
                _windowRulesByName[name] = rule;
        }

        // A missing enabled is on, including a later call, which turns the rule back on.
        rule.Enabled = Bool(1, "enabled") ?? true;
        ReadMatches(Label, rule.Matches);
        ReadEffects(Label, rule.Effects);
        if (rule.Matches.Count == 0)
        {
            Warn($"{Label}: a rule with nothing to match on is never applied");
            return PushRule(_makeWindowRule, rule.Id);
        }

        PublishWindow(rule);
        return PushRule(_makeWindowRule, rule.Id);
    }

    // hl.workspace_rule({ workspace, enabled, ... }). An enabled rule for that workspace takes the new fields.
    private int AddWorkspaceRule(IntPtr state)
    {
        const string Label = "hl.workspace_rule";
        if (!_lua.IsTable(1))
        {
            Warn("hl.workspace_rule: argument must be a table");
            return 0;
        }

        if (StringField(1, "workspace") is not string workspace)
        {
            Warn("hl.workspace_rule: 'workspace' field is required and must be a string");
            return 0;
        }

        workspace = LineText.Escape(workspace);

        foreach (var key in Keys(1))
        {
            if (key is not ("workspace" or "enabled" or "layout_opts") && !WorkspaceFields.Any(field => field.Field == key))
                Warn($"hl.workspace_rule: unknown field '{key}'");
        }

        var fields = ReadWorkspaceFields(Label);
        var existing = EnabledWorkspace(workspace);
        if (existing is not null)
        {
            // The rule already in force keeps whether it is on. The call's enabled is not copied.
            MergeFields(existing, fields);
            PublishWorkspace(existing);
            return PushRule(_makeWorkspaceRule, existing.Id);
        }

        var rule = new LuaWorkspaceRule(++_nextWorkspaceRule, workspace) { Enabled = Bool(1, "enabled") ?? true };
        MergeFields(rule, fields);
        _luaWorkspaceRules[rule.Id] = rule;
        PublishWorkspace(rule);
        return PushRule(_makeWorkspaceRule, rule.Id);
    }

    // ruleinfo(kind, id): whether the rule is on, or nil when that id is not a rule.
    private int RuleInfo(IntPtr state)
    {
        var kind = _lua.IsString(1) ? _lua.ToString(1, false) : "";
        if (!_lua.IsInteger(2) || !RuleEnabled(kind, (int)_lua.ToInteger(2), out var enabled))
        {
            _lua.PushNil();
            return 1;
        }

        _lua.PushBoolean(enabled);
        return 1;
    }

    // ruleset(kind, id, enabled): set_enabled. The caller has already checked that enabled is a boolean.
    private int RuleSet(IntPtr state)
    {
        var kind = _lua.IsString(1) ? _lua.ToString(1, false) : "";
        if (!_lua.IsInteger(2))
            return 0;

        var id = (int)_lua.ToInteger(2);
        var enabled = _lua.ToBoolean(3);
        if (kind == "window" && _luaWindowRules.TryGetValue(id, out var window))
            SetWindowEnabled(window, enabled);
        else if (kind == "workspace" && _luaWorkspaceRules.TryGetValue(id, out var workspace))
            SetWorkspaceEnabled(workspace, enabled);

        return 0;
    }

    private bool RuleEnabled(string kind, int id, out bool enabled)
    {
        if (kind == "window" && _luaWindowRules.TryGetValue(id, out var window))
        {
            enabled = window.Enabled;
            return true;
        }

        if (kind == "workspace" && _luaWorkspaceRules.TryGetValue(id, out var workspace))
        {
            enabled = workspace.Enabled;
            return true;
        }

        enabled = false;
        return false;
    }

    private int PushRule(int maker, int id)
    {
        _lua.RawGetInteger(LuaRegistry.Index, maker);
        _lua.PushInteger(id);
        if (_lua.PCall(1, 1, 0) != LuaStatus.OK)
        {
            NoteError(_lua.ToString(-1));
            _lua.Pop(1);
            return 0;
        }

        return 1;
    }

    private void ReadMatches(string label, Dictionary<string, string> matches)
    {
        _lua.GetField(1, "match");
        try
        {
            if (!_lua.IsTable(-1))
                return;

            var table = _lua.AbsIndex(-1);
            foreach (var key in Keys(table))
            {
                _lua.GetField(table, key);
                var value = _lua.Type(-1) switch
                {
                    LuaType.Boolean => _lua.ToBoolean(-1) ? "true" : "false",
                    LuaType.Number => NumberText(-1),
                    LuaType.String => _lua.ToString(-1, false),
                    _ => null,
                };
                _lua.Pop(1);
                if (value is null)
                {
                    Warn($"{label}: match value for '{key}' must be string, bool, or number");
                    continue;
                }

                matches[key] = key + ":" + LineText.Escape(value);
            }
        }
        finally
        {
            _lua.Pop(1);
        }
    }

    private void ReadEffects(string label, List<string> effects)
    {
        foreach (var key in Keys(1))
        {
            if (key is "name" or "enabled" or "match")
                continue;

            if (RuleEffect(label, key) is string effect)
                effects.Add(effect);
        }
    }

    private void PublishWindow(LuaWindowRule rule)
    {
        if (rule.Matches.Count == 0 || rule.Effects.Count == 0)
            return;

        if (_phase == Phase.Running)
        {
            if (rule.Published)
                Emit($"windowrulev2 = drop, ruleid:{Id(rule.Id)}");

            foreach (var effect in rule.Effects)
                Emit(WindowLine(rule, effect));

            rule.Published = true;
            return;
        }

        for (var i = 0; i < rule.Lines.Count; i++)
        {
            var index = rule.Lines[i];
            _lines[index] = (WindowLine(rule, rule.Effects[i]), _lines[index].Origin);
        }

        for (var i = rule.Lines.Count; i < rule.Effects.Count; i++)
        {
            var index = _lines.Count;
            Emit(WindowLine(rule, rule.Effects[i]));
            rule.Lines.Add(index);
        }

        rule.Published = true;
    }

    private void SetWindowEnabled(LuaWindowRule rule, bool enabled)
    {
        if (rule.Enabled == enabled)
            return;

        rule.Enabled = enabled;
        if (!rule.Published)
            return;

        if (_phase != Phase.Running)
        {
            for (var i = 0; i < rule.Lines.Count; i++)
            {
                var index = rule.Lines[i];
                _lines[index] = (WindowLine(rule, rule.Effects[i]), _lines[index].Origin);
            }

            return;
        }

        Emit($"windowrulev2 = ruleon:{(enabled ? "1" : "0")}, ruleid:{Id(rule.Id)}");
    }

    private static string WindowLine(LuaWindowRule rule, string effect) =>
        $"windowrulev2 = {effect}{MatchSuffix(rule)}, ruleid:{Id(rule.Id)}, ruleon:{(rule.Enabled ? "1" : "0")}";

    private static string MatchSuffix(LuaWindowRule rule)
    {
        if (rule.Matches.Count == 0)
            return "";

        var keys = new string[rule.Matches.Count];
        rule.Matches.Keys.CopyTo(keys, 0);
        Array.Sort(keys, StringComparer.Ordinal);
        var parts = new string[keys.Length];
        for (var i = 0; i < keys.Length; i++)
            parts[i] = rule.Matches[keys[i]];

        return ", " + string.Join(", ", parts);
    }

    private List<(string Key, string Part)> ReadWorkspaceFields(string label)
    {
        var fields = new List<(string Key, string Part)>();
        foreach (var (field, rule) in WorkspaceFields)
        {
            if (!Present(1, field))
                continue;

            string? value;
            if (rule[0] == '!')
            {
                value = Bool(1, field) is bool flag ? (!flag ? "true" : "false") : null;
                if (value is null)
                    Warn($"{label}: field '{field}': value must be a bool");
            }
            else if (field.EndsWith("gaps", StringComparison.Ordinal) || field.StartsWith("gaps_", StringComparison.Ordinal))
            {
                value = CssGap(1, field, label) is { } gaps ? $"{gaps.Top} {gaps.Right} {gaps.Bottom} {gaps.Left}" : null;
            }
            else
            {
                value = Part(1, field, label, field is "monitor" or "on_created_empty" or "default_name" or "layout" or "animation");
            }

            if (value is not null)
            {
                var name = rule.TrimStart('!');
                fields.Add((name, name + ":" + value));
            }
        }

        foreach (var part in LayoutOpts(label))
        {
            var split = part.IndexOf(':', "layoutopt:".Length);
            var key = split < 0 ? part : part[..split];
            fields.Add((key, part));
        }

        return fields;
    }

    private LuaWorkspaceRule? EnabledWorkspace(string workspace)
    {
        LuaWorkspaceRule? found = null;
        foreach (var rule in _luaWorkspaceRules.Values)
        {
            if (!rule.Enabled || rule.Workspace != workspace)
                continue;

            if (found is null || rule.Id < found.Id)
                found = rule;
        }

        return found;
    }

    private static void MergeFields(LuaWorkspaceRule rule, List<(string Key, string Part)> incoming)
    {
        foreach (var field in incoming)
        {
            var index = rule.Fields.FindIndex(held => held.Key == field.Key);
            if (index >= 0)
                rule.Fields[index] = field;
            else
                rule.Fields.Add(field);
        }
    }

    private void PublishWorkspace(LuaWorkspaceRule rule)
    {
        var text = WorkspaceLine(rule);
        if (_phase != Phase.Running && rule.Line >= 0)
        {
            _lines[rule.Line] = (text, _lines[rule.Line].Origin);
            return;
        }

        if (_phase != Phase.Running)
        {
            rule.Line = _lines.Count;
            Emit(text);
            return;
        }

        Emit(text);
    }

    private void SetWorkspaceEnabled(LuaWorkspaceRule rule, bool enabled)
    {
        if (rule.Enabled == enabled)
            return;

        rule.Enabled = enabled;
        PublishWorkspace(rule);
    }

    private static string WorkspaceLine(LuaWorkspaceRule rule)
    {
        var tail = $"ruleid:{Id(rule.Id)}, ruleon:{(rule.Enabled ? "1" : "0")}";
        if (rule.Fields.Count == 0)
            return $"workspace = {rule.Workspace}, {tail}";

        var parts = new string[rule.Fields.Count];
        for (var i = 0; i < parts.Length; i++)
            parts[i] = rule.Fields[i].Part;

        return $"workspace = {rule.Workspace}, {string.Join(", ", parts)}, {tail}";
    }

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);
}
