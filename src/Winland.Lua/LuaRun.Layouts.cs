using KeraLua;

namespace Winland.Lua;

// hl.layout.register: a custom layout written in Lua. Registering one makes the line "lualayout = lua:NAME", so
// general:layout and a workspace rule can select it, and the core asks for its boxes through the runtime, which runs
// recalculate and layout_msg here, on the Lua's own thread, and sends the boxes back as layoutplace (ADR 0013).
internal sealed partial class LuaRun
{
    // LUA_TIMEOUT_LAYOUT_CALLBACK_MS of Winland, in seconds.
    internal const double LayoutLimit = 0.05;

    // Each registered layout's provider table, by its name with the lua: prefix.
    private readonly Dictionary<string, int> _layouts = new(StringComparer.Ordinal);
    private int _makeLayoutContext;

    private void RegisterLayouts()
    {
        _lua.NewTable();
        Function("register", "hl.layout.register", LayoutRegister);
        _lua.SetField(-2, "layout");
    }

    // hl.layout.register(name, { recalculate = function(ctx) ... end, layout_msg = function(ctx, msg) ... end }).
    private int LayoutRegister(IntPtr state)
    {
        if (!_lua.IsString(1))
        {
            Warn($"layout.register: bad argument 1: expected string, got {TypeName(1)}");
            return 0;
        }

        if (!_lua.IsTable(2))
        {
            Warn($"hl.layout.register: bad argument #2 (table expected, got {_lua.TypeName(_lua.Type(2))})");
            return 0;
        }

        var name = _lua.ToString(1, false);
        if (!name.StartsWith(LayoutRequest.Prefix, StringComparison.Ordinal))
            name = LayoutRequest.Prefix + name;

        if (name == LayoutRequest.Prefix)
        {
            Warn("hl.layout.register: layout name cannot be empty");
            return 0;
        }

        _lua.GetField(2, "recalculate");
        var recalculates = _lua.IsFunction(-1);
        _lua.Pop(1);
        if (!recalculates)
        {
            Warn("hl.layout.register: provider table must define recalculate(ctx)");
            return 0;
        }

        // A name with a space would not survive the config line, and no layout name Winland lists has one.
        if (name.Any(char.IsWhiteSpace) || _layouts.ContainsKey(name))
        {
            Warn(_layouts.ContainsKey(name)
                ? $"hl.layout.register: layout '{name}' is already registered"
                : $"hl.layout.register: layout name '{name}' cannot have a space in it");
            return 0;
        }

        _lua.PushCopy(2);
        _layouts[name] = _lua.Ref(LuaRegistry.Index);
        Emit($"{LayoutRequest.Keyword} = {name}");
        return 0;
    }

    // The type a check names, as Winland names it: a number reads as a string there.
    private string TypeName(int index) => _lua.Type(index) switch
    {
        LuaType.String or LuaType.Number => "string",
        LuaType.None or LuaType.Nil => "nil",
        LuaType.Boolean => "bool",
        LuaType.Function => "function",
        LuaType.Table => "table",
        _ => "?",
    };

    /// <summary>
    /// Runs a layout's <c>layout_msg</c> with the request's message. True when the layout is to be laid out again,
    /// which it is unless the callback failed. A message the layout rejects is logged, since the core answered it.
    /// </summary>
    public bool LayoutMessage(LayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_phase != Phase.Running || request.Message is not string message || !_layouts.TryGetValue(request.Name, out var provider))
            return false;

        var top = _lua.GetTop();
        _lua.RawGetInteger(LuaRegistry.Index, provider);
        _lua.GetField(-1, "layout_msg");
        _lua.Remove(-2);
        if (!_lua.IsFunction(-1) || !PushContext(request))
        {
            _lua.SetTop(top);
            return true;
        }

        _lua.Pop(1);
        _lua.PushString(message);
        if (!Guarded(LayoutLimit, $"lua layout {request.Name} layout_msg callback", results: 1, arguments: 2))
        {
            _lua.SetTop(top);
            return false;
        }

        if (_lua.IsBoolean(-1) && !_lua.ToBoolean(-1))
            _world?.Log($"lua: lua layout {request.Name} rejected layoutmsg");
        else if (_lua.Type(-1) is LuaType.String or LuaType.Number)
            _world?.Log("lua: " + _lua.ToString(-1, false));

        _lua.SetTop(top);
        return true;
    }

    /// <summary>
    /// Runs a layout's <c>recalculate</c> for a request and gives the <c>layoutplace</c> command that answers it: the
    /// boxes its targets placed, or that it failed, so the Layout falls back to its grid. Null when this config has no
    /// such layout, or there is nothing to lay out.
    /// </summary>
    public string? Arrange(LayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_phase != Phase.Running || request.Targets.Count == 0 || !_layouts.TryGetValue(request.Name, out var provider))
            return null;

        var top = _lua.GetTop();
        _lua.RawGetInteger(LuaRegistry.Index, provider);
        _lua.GetField(-1, "recalculate");
        _lua.Remove(-2);
        if (!_lua.IsFunction(-1) || !PushContext(request))
        {
            _lua.SetTop(top);
            return request.Answer(null);
        }

        // The table of placed boxes goes below the function, to be read once it returns.
        _lua.Insert(-3);
        if (!Guarded(LayoutLimit, $"lua layout {request.Name} recalculate callback", arguments: 1))
        {
            _lua.SetTop(top);
            return request.Answer(null);
        }

        var placed = new Dictionary<int, LayoutBox>();
        for (var i = 1; i <= request.Targets.Count; i++)
        {
            _lua.RawGetInteger(-1, i);
            if (_lua.IsTable(-1) && BoxField(-1, "x") is double x && BoxField(-1, "y") is double y && BoxField(-1, "w") is double w && BoxField(-1, "h") is double h)
                placed[i - 1] = new LayoutBox(x, y, w, h);

            _lua.Pop(1);
        }

        _lua.SetTop(top);
        return request.Answer(placed);
    }

    // Pushes the ctx of a request and the table its targets place their boxes in. False when it could not be made.
    private bool PushContext(LayoutRequest request)
    {
        _lua.RawGetInteger(LuaRegistry.Index, _makeLayoutContext);
        PushBox(request.Area);
        _lua.CreateTable(request.Targets.Count, 0);
        for (var i = 0; i < request.Targets.Count; i++)
        {
            _lua.CreateTable(0, 2);
            _lua.PushString(Address(request.Targets[i].Window));
            _lua.SetField(-2, "address");
            PushBox(request.Targets[i].Box);
            _lua.SetField(-2, "box");
            _lua.RawSetInteger(-2, i + 1);
        }

        if (_lua.PCall(2, 2, 0) == LuaStatus.OK)
            return true;

        _lua.Pop(1);
        return false;
    }

    private void PushBox(Rectangle box)
    {
        _lua.CreateTable(0, 4);
        _lua.PushNumber(box.X);
        _lua.SetField(-2, "x");
        _lua.PushNumber(box.Y);
        _lua.SetField(-2, "y");
        _lua.PushNumber(box.Width);
        _lua.SetField(-2, "w");
        _lua.PushNumber(box.Height);
        _lua.SetField(-2, "h");
    }

    private double? BoxField(int table, string field)
    {
        _lua.GetField(table, field);
        double? value = _lua.IsNumber(-1) ? _lua.ToNumber(-1) : null;
        _lua.Pop(1);
        return value is double number && double.IsFinite(number) ? number : null;
    }
}
