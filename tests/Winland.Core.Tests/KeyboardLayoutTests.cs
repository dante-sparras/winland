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

    [Fact]
    public void DeviceNamesAreThePinnedReleasesInternalNames()
    {
        Assert.Equal("logitech-usb-receiver", InputDeviceNames.Internal("Logitech USB Receiver"));
        Assert.Equal("a-b-c-d", InputDeviceNames.Internal("A,B\nC D"));
        Assert.Equal(
            ["hid-keyboard-device", "hid-keyboard-device-1", "unknown-device", "hid-keyboard-device-2", "unknown-device-1"],
            InputDeviceNames.Assign(
            [
                new(InputDeviceKind.Keyboard, 1, "HID Keyboard Device"),
                new(InputDeviceKind.Keyboard, 2, "HID Keyboard Device"),
                new(InputDeviceKind.Pointer, 3, ""),
                new(InputDeviceKind.Pointer, 4, "hid keyboard device"),
                new(InputDeviceKind.Pointer, 5, ""),
            ]));
    }

    [Fact]
    public void DevicesReportTheDevicesPluggedIn()
    {
        var core = new Core(ConfigParser.Parse("input:kb_layout = us,se\n"))
        {
            Devices = new Plugged(
                new(InputDeviceKind.Keyboard, 0x20, "Contoso Keyboard"),
                new(InputDeviceKind.Pointer, 0x30, "Contoso Mouse"),
                new(InputDeviceKind.Keyboard, 0x40, "Contoso Keyboard")),
        };

        using var devices = JsonDocument.Parse(core.Execute("j/devices").Text);
        var keyboards = devices.RootElement.GetProperty("keyboards").EnumerateArray().ToArray();
        Assert.Equal(["contoso-keyboard", "contoso-keyboard-1"], keyboards.Select(keyboard => keyboard.GetProperty("name").GetString()));
        Assert.Equal(["0x20", "0x40"], keyboards.Select(keyboard => keyboard.GetProperty("address").GetString()));
        Assert.Equal([true, false], keyboards.Select(keyboard => keyboard.GetProperty("main").GetBoolean()));
        Assert.All(keyboards, keyboard => Assert.Equal("us,se", keyboard.GetProperty("layout").GetString()));
        var mouse = Assert.Single(devices.RootElement.GetProperty("mice").EnumerateArray().ToArray());
        Assert.Equal(("contoso-mouse", "0x30"), (mouse.GetProperty("name").GetString(), mouse.GetProperty("address").GetString()));

        var text = core.Execute("devices").Text;
        Assert.Contains("\tMouse at 30:\n\t\tcontoso-mouse\n", text, StringComparison.Ordinal);
        Assert.Contains("\tKeyboard at 40:\n\t\tcontoso-keyboard-1\n", text, StringComparison.Ordinal);

        var switched = core.Execute("switchxkblayout Contoso-Keyboard-1 1");
        Assert.Equal("ok", switched.Text);
        Assert.Equal(1, Index(core));
        Assert.Equal(["activelayout>>contoso-keyboard-1,Swedish"], CommandProtocol.FormatStream(switched.Effect.Events, switched.Effect.State));
        var all = core.Execute("switchxkblayout all 0");
        Assert.Equal(
            ["activelayout>>contoso-keyboard,English (US)", "activelayout>>contoso-keyboard-1,English (US)"],
            CommandProtocol.FormatStream(all.Effect.Events, all.Effect.State));
        Assert.Equal("device not found", core.Execute("switchxkblayout keyboard 0").Text);
    }

    private sealed class Plugged(params InputDevice[] devices) : IInputDevices
    {
        public IReadOnlyList<InputDevice> Read() => devices;
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
