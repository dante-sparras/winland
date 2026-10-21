namespace Winland.Tests.Conformance;

/// <summary>
/// Winland's Lua config cases, run with a Lua config, as the reference's is. Each test is named after its
/// case and sends the same requests.
/// </summary>
[Trait("Category", "Conformance")]
public class LuaConfigConformance
{
    [Fact]
    public void LuaRequire()
    {
        const string expected = "absolute:relative:a:b";
        using var desk = Desk.WithLua(RequireConfig(), files: RequireFiles);

        Assert.Equal(expected, desk.Send("repl return _G.hyprtester_lua_require_result"));

        Assert.Equal("ok", desk.Send("reload"));
        Assert.Equal(expected, desk.Send("repl return _G.hyprtester_lua_require_result"));
    }

    [Fact]
    public void LuaSpecialWorkspaceDeactivationEventNil()
    {
        const string special = "lua_special_active_nil";
        using var desk = Desk.WithLua("");

        Ok(desk, "eval do _G.hyprtester_special_active_event_count = 0; _G.hyprtester_special_active_subscription = hl.on('workspace.special_active', function(workspace, "
            + "monitor) _G.hyprtester_special_active_event_count = _G.hyprtester_special_active_event_count + 1; _G.hyprtester_special_active_workspace_type = "
            + "type(workspace); _G.hyprtester_special_active_monitor_type = type(monitor) end) end");

        Ok(desk, $"dispatch hl.dsp.workspace.toggle_special('{special}')");
        Assert.Equal("1", desk.Send("repl _G.hyprtester_special_active_event_count"));
        Assert.Equal("userdata", desk.Send("repl _G.hyprtester_special_active_workspace_type"));
        Assert.Equal("userdata", desk.Send("repl _G.hyprtester_special_active_monitor_type"));

        Ok(desk, $"dispatch hl.dsp.workspace.toggle_special('{special}')");
        Assert.Equal("2", desk.Send("repl _G.hyprtester_special_active_event_count"));
        Assert.Equal("nil", desk.Send("repl _G.hyprtester_special_active_workspace_type"));
        Assert.Equal("userdata", desk.Send("repl _G.hyprtester_special_active_monitor_type"));

        // The case's cleanup.
        if (SpecialsShown(desk).Contains($"special:{special}"))
            desk.Send($"dispatch hl.dsp.workspace.toggle_special('{special}')");

        desk.Send("eval do _G.hyprtester_special_active_subscription = nil; _G.hyprtester_special_active_event_count = nil; "
            + "_G.hyprtester_special_active_workspace_type = nil; _G.hyprtester_special_active_monitor_type = nil end");
    }

    [Fact]
    public void LuaDefaultSpecialWorkspaceName()
    {
        using var desk = Desk.WithLua("");

        Ok(desk, "dispatch hl.dsp.workspace.toggle_special()");
        Assert.Contains("special:special", SpecialsShown(desk));
        Ok(desk, "dispatch hl.dsp.workspace.toggle_special()");
        Assert.DoesNotContain("special:special", SpecialsShown(desk));

        Ok(desk, "eval hl.get_active_monitor():set_special_workspace('')");
        Assert.Contains("special:special", SpecialsShown(desk));
        Ok(desk, "eval hl.get_active_monitor():set_special_workspace(nil)");
        Assert.DoesNotContain("special:special", SpecialsShown(desk));
    }

    [Fact]
    public void LuaReloadConfig()
    {
        const string variable = "normally_nonexistent_variable";
        using var desk = Desk.WithLua("");

        Ok(desk, $"eval {variable} = true");
        Ok(desk, "dispatch hl.dsp.reload_config()");
        Assert.Equal("nil", desk.Send($"repl return {variable}"));
    }

    [Fact]
    public void LuaDevicesEnumerationAndHyprctlParity()
    {
        using var desk = Desk.WithLua("");

        // The test plugin supplies one pointer and two keyboards.
        Ok(desk, """
            eval do
                    local devices = hl.get_devices()
                    assert(type(devices) == 'table')
                    local types = { pointer = true, keyboard = true, tablet = true, tablet_pad = true,
                                    tablet_tool = true, touch = true, switch = true }
                    local count, pointers, keyboards = 0, 0, 0
                    local byAddress = {}
                    for index, device in pairs(devices) do
                        assert(type(index) == 'number' and index % 1 == 0 and index >= 1 and index <= #devices)
                        assert(type(device) == 'userdata' and types[device.type])
                        assert(type(device.address) == 'string' and device.address:match('^0x%x+$'))
                        assert(not byAddress[device.address], 'duplicate device address')
                        byAddress[device.address] = device
                        count = count + 1
                        if device.type == 'pointer' and device.name:match('^test%-mouse') then
                            pointers = pointers + 1
                            assert(device.default_speed == 0)
                            assert(type(device.scroll_factor) == 'number')
                        elseif device.type == 'keyboard' and device.name:match('^test%-keyboard') then
                            keyboards = keyboards + 1
                            assert(type(device.main) == 'boolean')
                            assert(type(device.caps_lock) == 'boolean' and type(device.num_lock) == 'boolean')
                        end
                    end
                    assert(count == #devices and pointers == 1 and keyboards == 2)
                    local again = hl.get_devices()
                    assert(#again == count)
                    for _, device in ipairs(again) do
                        assert(device == byAddress[device.address])
                    end
                end
            """);

        // The reference turns the devices reply into Lua assertions with jq. This is the same Lua.
        Ok(desk, "eval " + Parity(desk.Send("j/devices")));

        var readOnly = desk.Send("""
            eval do
                    local device = assert(hl.get_devices()[1])
                    local original = device.address
                    assert(pcall(function() device.address = 'changed' end))
                    assert(device.address == original)
                end
            """);
        Assert.Contains("attempt to modify read-only hl object", readOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("assertion failed", readOnly, StringComparison.Ordinal);
    }

    [Fact]
    public void LuaDevicesRetainedKeyboardTracksLayoutChanges()
    {
        using var desk = Desk.WithLua("");

        Ok(desk, "r/eval hl.config({ input = { kb_layout = 'us,pl,ua', kb_variant = '', kb_options = '' } })");
        Ok(desk, """
            eval do
                    for _, device in ipairs(hl.get_devices()) do
                        if device.type == 'keyboard' and device.name:match('^test%-keyboard') then
                            _G.hyprtester_lua_keyboard = device
                            break
                        end
                    end
                    assert(_G.hyprtester_lua_keyboard, 'synthetic keyboard missing')
                    assert(_G.hyprtester_lua_keyboard.layout == 'us,pl,ua')
                end
            """);

        string[] layoutNames = ["English (US)", "Polish", "Ukrainian"];
        for (var i = 0; i < layoutNames.Length; i++)
        {
            Ok(desk, $"switchxkblayout all {i}");
            Assert.Equal(i.ToString(System.Globalization.CultureInfo.InvariantCulture), desk.Send("repl return _G.hyprtester_lua_keyboard.active_layout_index"));
            Assert.Equal(layoutNames[i], desk.Send("repl return _G.hyprtester_lua_keyboard.active_keymap"));
            Ok(desk, """
                eval do
                        local retained = _G.hyprtester_lua_keyboard
                        local found = false
                        for _, device in ipairs(hl.get_devices()) do
                            if device.address == retained.address then
                                assert(device == retained)
                                assert(device.active_layout_index == retained.active_layout_index)
                                assert(device.active_keymap == retained.active_keymap)
                                found = true
                            end
                        end
                        assert(found)
                    end
                """);
        }

        // Rebuilding the keymap must also be visible through the original userdata.
        Ok(desk, "r/eval hl.config({ input = { kb_layout = 'de', kb_variant = 'nodeadkeys' } })");
        Ok(desk, """
            eval do
                    local keyboard = _G.hyprtester_lua_keyboard
                    assert(keyboard.layout == 'de' and keyboard.variant == 'nodeadkeys')
                    assert(keyboard.active_layout_index == 0)
                    assert(type(keyboard.active_keymap) == 'string')
                end
            """);

        // The case's cleanup.
        desk.Send("eval _G.hyprtester_lua_keyboard = nil");
        desk.Send("reload");
    }

    private static void Ok(Desk desk, string line) => Assert.Equal("ok", desk.Send(line));

    // The special workspaces the monitors show, as the reference reads "(special:NAME)" from its monitors reply.
    private static string[] SpecialsShown(Desk desk)
    {
        using var monitors = System.Text.Json.JsonDocument.Parse(desk.Send("j/monitors"));
        return [.. monitors.RootElement.EnumerateArray().Select(monitor => monitor.GetProperty("specialWorkspace").GetProperty("address").GetString() ?? "")];
    }

    // The Lua the reference's jq program makes from the devices reply: each mouse and keyboard field, with the
    // reply's names turned into the Lua object's, checked against hl.get_devices().
    private static string Parity(string reply)
    {
        using var document = System.Text.Json.JsonDocument.Parse(reply);
        var root = document.RootElement;
        var renames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["defaultSpeed"] = "default_speed",
            ["scrollFactor"] = "scroll_factor",
            ["capsLock"] = "caps_lock",
            ["numLock"] = "num_lock",
        };
        var expected = new List<string>();
        foreach (var (group, type) in (ReadOnlySpan<(string, string)>)[("mice", "pointer"), ("keyboards", "keyboard")])
        {
            foreach (var device in root.GetProperty(group).EnumerateArray())
            {
                var fields = device.EnumerateObject()
                    .Select(field => $"[{System.Text.Json.JsonSerializer.Serialize(renames.GetValueOrDefault(field.Name, field.Name))}]={field.Value.GetRawText()}")
                    .Append($"[\"type\"]=\"{type}\"");
                expected.Add("{" + string.Join(",", fields) + "}");
            }
        }

        var total = (string[])["mice", "keyboards", "tablets", "touch", "switches"];
        var count = total.Sum(group => root.GetProperty(group).GetArrayLength());
        return "local expected = {" + string.Join(",", expected) + "}; "
            + "local actual = hl.get_devices(); assert(#actual == " + count + "); "
            + "local byAddress = {}; for _, device in ipairs(actual) do byAddress[device.address] = device end; "
            + "for _, expectedDevice in ipairs(expected) do "
            + "local device = assert(byAddress[expectedDevice.address]); "
            + "for field, value in pairs(expectedDevice) do "
            + "if field == \"default_speed\" or field == \"scroll_factor\" then "
            + "assert(math.abs(device[field] - value) < 0.0051, field) "
            + "else assert(device[field] == value, expectedDevice.name .. \": \" .. field) end "
            + "end "
            + "end";
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
