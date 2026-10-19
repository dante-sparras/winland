using System.Text.Json;
using Winland;

namespace Winland.Tests;

public class KeyboardLayoutTests
{
    [Fact]
    public void LayoutsMapToWindowsLayoutsWithTheirVariants()
    {
        var layouts = XkbLayouts.Parse("us, se,us,xx", ",,dvorak,");

        Assert.Equal(
            [
                new KeyboardLayoutChoice("us", "", "00000409", "English (US)"),
                new KeyboardLayoutChoice("se", "", "0000041D", "Swedish"),
                new KeyboardLayoutChoice("us", "dvorak", "00010409", "English (Dvorak)"),
                new KeyboardLayoutChoice("xx", "", null, "xx"),
            ],
            layouts);
        Assert.Equal("0000040C", XkbLayouts.Parse("fr", "nosuch").Single().WindowsId);
    }

    [Fact]
    public void AnUnsetLayoutLeavesTheSystemAlone()
    {
        var core = new Core();

        Assert.Null(core.Handle(new PointerMoved(new Point(1, 1))).KeyboardLayout);

        core.Execute("keyword input:kb_layout us,se");
        Assert.Equal(new KeyboardLayoutRequest("00000409,0000041D", 0), core.Handle(new PointerMoved(new Point(2, 2))).KeyboardLayout);
    }

    [Fact]
    public void SwitchingPicksTheLayoutAndReportsIt()
    {
        var core = new Core(ConfigParser.Parse("input:kb_layout = us,pl,ua\n"));

        var switched = core.Execute("switchxkblayout all 2");
        Assert.Equal("ok", switched.Text);
        Assert.Equal(2, switched.Effect.KeyboardLayout?.Active);
        Assert.Contains("activelayout>>keyboard,Ukrainian", CommandProtocol.FormatStream(switched.Effect.Events, switched.Effect.State));

        Assert.Equal("ok", core.Execute("switchxkblayout keyboard next").Text);
        Assert.Equal(0, Index(core));
        Assert.Equal("ok", core.Execute("switchxkblayout current prev").Text);
        Assert.Equal(2, Index(core));

        Assert.Equal("layout idx out of range of 3", core.Execute("switchxkblayout all 3").Text);
        Assert.Equal("invalid arg 2", core.Execute("switchxkblayout all x").Text);
        Assert.Equal("device not found", core.Execute("switchxkblayout nosuch 0").Text);
        Assert.Equal(2, Index(core));
    }

    [Fact]
    public void DevicesListTheKeyboardWithItsLayouts()
    {
        var core = new Core(ConfigParser.Parse("input:kb_layout = us,se\ninput:kb_variant = intl,\n"));
        core.Execute("switchxkblayout all 1");

        using var devices = JsonDocument.Parse(core.Execute("j/devices").Text);
        var keyboard = Assert.Single(devices.RootElement.GetProperty("keyboards").EnumerateArray().ToArray());
        Assert.Equal("keyboard", keyboard.GetProperty("name").GetString());
        Assert.Equal("us,se", keyboard.GetProperty("layout").GetString());
        Assert.Equal("intl,", keyboard.GetProperty("variant").GetString());
        Assert.Equal(1, keyboard.GetProperty("active_layout_index").GetInt32());
        Assert.Equal("Swedish", keyboard.GetProperty("active_keymap").GetString());
        Assert.Single(devices.RootElement.GetProperty("mice").EnumerateArray().ToArray());

        Assert.Contains("active layout index: 1", core.Execute("devices").Text, StringComparison.Ordinal);
    }

    private static int Index(Core core)
    {
        using var devices = JsonDocument.Parse(core.Execute("j/devices").Text);
        return devices.RootElement.GetProperty("keyboards")[0].GetProperty("active_layout_index").GetInt32();
    }

    [Fact]
    public void BindsReadTheFirstLayoutUnlessResolvedBySymbol()
    {
        Assert.False(new Core().BindsInFirstLayout);

        var configured = new Core();
        configured.Handle(new Configure(ConfigParser.Parse("input:kb_layout = us,se" + Environment.NewLine)));
        Assert.True(configured.BindsInFirstLayout);

        var bySymbol = new Core();
        bySymbol.Handle(new Configure(ConfigParser.Parse("input:kb_layout = us,se" + Environment.NewLine + "input:resolve_binds_by_sym = true" + Environment.NewLine)));
        Assert.False(bySymbol.BindsInFirstLayout);
    }
}
