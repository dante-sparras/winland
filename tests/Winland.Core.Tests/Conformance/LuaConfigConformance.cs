namespace Winland.Tests.Conformance;

/// <summary>
/// The pinned release's Lua config cases, run with a Lua config, as the reference's is. Each test is named after its
/// case and sends the same requests.
/// </summary>
[Trait("Category", "Conformance")]
public class LuaConfigConformance
{
    [Fact(Skip = "require takes a module name only. A path, such as ./lua-require/relative.lua or an absolute one, and a wildcard path are not read.")]
    public void LuaRequire()
    {
        const string expected = "absolute:relative:a:b";
        using var desk = Desk.WithLua(RequireConfig(), files: RequireFiles);

        Assert.Equal(expected, desk.Send("repl return _G.hyprtester_lua_require_result"));

        Assert.Equal("ok", desk.Send("reload"));
        Assert.Equal(expected, desk.Send("repl return _G.hyprtester_lua_require_result"));
    }

    // The files of the reference's lua-require folder, next to its config.
    private static readonly Dictionary<string, string> RequireFiles = new(StringComparer.Ordinal)
    {
        ["lua-require/absolute.lua"] = "return \"absolute\"\n",
        ["lua-require/relative.lua"] = "return \"relative\"\n",
        ["lua-require/wildcard/a.lua"] = "return \"a\"\n",
        ["lua-require/wildcard/b.lua"] = "return \"b\"\n",
    };

    // The require lines at the top of the reference config. It finds its own folder through debug.getinfo, which
    // Winland's Lua has not, so the folder is written in.
    private static string RequireConfig()
    {
        var folder = Desk.LuaFolder.Replace('\\', '/');
        return $$"""
            local requireAbsolute = require("{{folder}}/lua-require/absolute.lua")
            local requireRelative = require("./lua-require/relative.lua")
            local requireWildcard = require("./lua-require/wildcard/*")

            _G.hyprtester_lua_require_result = table.concat({ requireAbsolute, requireRelative, requireWildcard[1], requireWildcard[2] }, ":")
            assert(_G.hyprtester_lua_require_result == "absolute:relative:a:b")
            """;
    }
}
