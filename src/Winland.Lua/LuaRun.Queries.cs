using System.Text.Json;
using System.Text.RegularExpressions;
using KeraLua;

namespace Winland.Lua;

// The hl.get_* queries and the window, workspace, monitor and group objects, which Objects.lua makes from Winland's
// query replies. A reply is read once and kept until a dispatch or a setting runs or the call ends, so the fields a
// handler reads agree with each other and with what its own dispatches did.
internal sealed partial class LuaRun
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Lazy<string> ObjectsCode = new(() =>
    {
        using var stream = typeof(LuaRun).Assembly.GetManifestResourceStream("Winland.Lua.Objects.lua")
            ?? throw new InvalidOperationException("Objects.lua is not in the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    private readonly Dictionary<string, int> _replies = new(StringComparer.Ordinal);
    private int _makeWindow;
    private int _makeWorkspace;
    private int _makeMonitor;
    private int _makeSubscription;
    private int _selectorOf;

    // Runs Objects.lua, which sets the hl.get_* functions, and keeps the constructors it returns.
    private void RegisterObjects()
    {
        if (_lua.LoadString(ObjectsCode.Value, "=objects") != LuaStatus.OK)
            throw new InvalidOperationException(_lua.ToString(-1));

        _lua.GetGlobal("hl");
        _lua.NewTable();
        Function("fetch", "hl", Fetch);
        Function("userdata", "hl", MakeUserData);
        Function("note", "hl", Note);
        Function("matches", "hl", Matches);
        Function("history", "hl", History);
        Function("unsubscribe", "hl", Unsubscribe);
        Function("option", "hl", Option);
        Function("run", "hl", RunCommand);
        Function("bindinfo", "hl", BindInfo);
        Function("bindenable", "hl", BindEnable);
        Function("bindremove", "hl", BindRemove);
        Function("timerinfo", "hl", TimerInfo);
        Function("timerset", "hl", TimerSet);
        Function("ruleinfo", "hl", RuleInfo);
        Function("ruleset", "hl", RuleSet);
        Function("running", "hl", state =>
        {
            _lua.PushBoolean(_world is not null);
            return 1;
        });
        _lua.PushString(BuildStamp.Version);
        if (_lua.PCall(3, 1, 0) != LuaStatus.OK)
            throw new InvalidOperationException(_lua.ToString(-1));

        _makeWindow = Field("window");
        _makeWorkspace = Field("workspace");
        _makeMonitor = Field("monitor");
        _makeSubscription = Field("subscription");
        _selectorOf = Field("selector");
        _makeKeybind = Field("keybind");
        _makeTimer = Field("timer");
        _makeWindowRule = Field("windowRule");
        _makeWorkspaceRule = Field("workspaceRule");
        _makeLayoutContext = Field("layoutContext");
        _lua.Pop(1);

        int Field(string name)
        {
            _lua.GetField(-1, name);
            return _lua.Ref(LuaRegistry.Index);
        }
    }

    // fetch(command): the reply to a query as Lua values, or nil before Winland runs or for a reply that is not JSON.
    private int Fetch(IntPtr state)
    {
        var command = _lua.IsString(1) ? _lua.ToString(1, false) : "";
        if (_world is null || command.Length == 0)
        {
            _lua.PushNil();
            return 1;
        }

        if (!_replies.TryGetValue(command, out var reference))
        {
            PushReply(Command(command));
            reference = _lua.Ref(LuaRegistry.Index);
            _replies[command] = reference;
        }

        _lua.RawGetInteger(LuaRegistry.Index, reference);
        return 1;
    }

    // run(command): runs a command as the pipe does and gives its reply. While the config loads it waits until Winland
    // runs, as hl.dispatch does.
    private int RunCommand(IntPtr state)
    {
        var command = _lua.IsString(1) ? _lua.ToString(1, false) : "";
        if (command.Length == 0)
            return 0;

        if (_world is null)
        {
            _queued.Add(command);
            _lua.PushString("ok");
            return 1;
        }

        var reply = Command(command);
        Forget();
        _lua.PushString(reply);
        return 1;
    }

    // Forgets the replies read so far: what they say may have changed.
    private void Forget()
    {
        foreach (var reference in _replies.Values)
            _lua.Unref(LuaRegistry.Index, reference);

        _replies.Clear();
    }

    private void PushReply(string reply)
    {
        try
        {
            using var document = JsonDocument.Parse(reply);
            PushJson(document.RootElement);
        }
        catch (JsonException)
        {
            _lua.PushNil();
        }
    }

    // A JSON value as Lua: an object a table of its fields, an array a list, a whole number an integer.
    private void PushJson(JsonElement element)
    {
        _ = _lua.CheckStack(4);
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                _lua.NewTable();
                foreach (var property in element.EnumerateObject())
                {
                    PushJson(property.Value);
                    _lua.SetField(-2, property.Name);
                }

                break;
            case JsonValueKind.Array:
                _lua.CreateTable(element.GetArrayLength(), 0);
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    PushJson(item);
                    _lua.RawSetInteger(-2, ++index);
                }

                break;
            case JsonValueKind.String:
                _lua.PushString(element.GetString());
                break;
            case JsonValueKind.Number when element.TryGetInt64(out var integer):
                _lua.PushInteger(integer);
                break;
            case JsonValueKind.Number:
                _lua.PushNumber(element.GetDouble());
                break;
            case JsonValueKind.True:
                _lua.PushBoolean(true);
                break;
            case JsonValueKind.False:
                _lua.PushBoolean(false);
                break;
            default:
                _lua.PushNil();
                break;
        }
    }

    // userdata(metatable): a new object of that kind.
    private int MakeUserData(IntPtr state)
    {
        if (!_lua.IsTable(1))
            return 0;

        _ = _lua.NewUserData(0);
        _lua.PushCopy(1);
        _lua.SetMetaTable(-2);
        return 1;
    }

    // note(message, level): a warning at the Lua code that many calls up from the function that notes it.
    private int Note(IntPtr state)
    {
        var message = _lua.IsString(1) ? _lua.ToString(1, false) : "";
        var level = _lua.IsInteger(2) ? (int)_lua.ToInteger(2) : 1;
        Warn(message, level);
        return 0;
    }

    // matches(text, pattern): whether the regular expression matches all of the text, as a window selector does.
    private int Matches(IntPtr state)
    {
        var text = _lua.IsString(1) ? _lua.ToString(1, false) : "";
        var pattern = _lua.IsString(2) ? _lua.ToString(2, false) : "";
        bool matched;
        try
        {
            matched = Regex.IsMatch(text, $"\\A(?:{pattern})\\z", RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (Exception exception) when (exception is ArgumentException or RegexMatchTimeoutException)
        {
            matched = false;
        }

        _lua.PushBoolean(matched);
        return 1;
    }

    // option(path): the option "general.gaps_in" or "general:gaps_in" names, as getoption takes it.
    private int Option(IntPtr state)
    {
        var path = _lua.IsString(1) ? _lua.ToString(1, false) : "";
        _lua.PushString(OptionName(path.Replace(':', '.')));
        return 1;
    }

    // The selector a dispatcher's table takes for the object at index, or null when it is no object.
    private string? SelectorOf(int index)
    {
        index = _lua.AbsIndex(index);
        _lua.RawGetInteger(LuaRegistry.Index, _selectorOf);
        _lua.PushCopy(index);
        if (_lua.PCall(1, 1, 0) != LuaStatus.OK)
        {
            _lua.Pop(1);
            return null;
        }

        var selector = _lua.IsString(-1) ? _lua.ToString(-1, false) : null;
        _lua.Pop(1);
        return selector;
    }
}
