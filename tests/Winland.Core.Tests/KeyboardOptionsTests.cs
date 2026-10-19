using System.Text.Json;
using Winland;

namespace Winland.Tests;

public class KeyboardOptionsTests
{
    [Fact]
    public void OptionsMoveKeysAndPickTheLayoutToggle()
    {
        var options = KeyboardOptions.Parse("caps:swapescape, grp:alt_shift_toggle,altwin:swap_lalt_lwin");

        Assert.Equal(KeyboardOptions.Escape, options.Remaps[KeyboardOptions.CapsLock]);
        Assert.Equal(KeyboardOptions.CapsLock, options.Remaps[KeyboardOptions.Escape]);
        Assert.Equal(KeyboardOptions.LeftWin, options.Remaps[KeyboardOptions.LeftAlt]);
        Assert.Equal(KeyboardOptions.LeftAlt, options.Remaps[KeyboardOptions.LeftWin]);
        Assert.Equal(LayoutToggle.AltShift, options.Toggle);
        Assert.Empty(options.Unknown);
    }

    [Fact]
    public void ALaterOptionWinsAndCapsNoneDropsTheKey()
    {
        Assert.Equal(KeyboardOptions.LeftControl, KeyboardOptions.Parse("caps:escape,ctrl:nocaps").Remaps[KeyboardOptions.CapsLock]);
        Assert.Equal(0, KeyboardOptions.Parse("caps:none").Remaps[KeyboardOptions.CapsLock]);
        Assert.Same(KeyboardOptions.None, KeyboardOptions.Parse(" "));
    }

    [Fact]
    public void AnOptionWinlandDoesNotKnowWarns()
    {
        var document = ConfigParser.Parse("input:kb_options = caps:escape,compose:ralt,lv3:ralt_switch\n");

        Assert.Equal(
            [
                new ConfigWarning(1, "XKB option 'compose:ralt' is not honored."),
                new ConfigWarning(1, "XKB option 'lv3:ralt_switch' is not honored."),
            ],
            document.Warnings);
        Assert.Equal("caps:escape,compose:ralt,lv3:ralt_switch", InputOptions.From(document).KbOptions);
    }

    [Fact]
    public void TheCoreHandsTheOptionsToTheHookAndCyclesLayouts()
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("input:kb_layout = us,se\ninput:kb_options = grp:win_space_toggle,caps:escape\n")));

        Assert.Equal(LayoutToggle.WinSpace, core.KeyOptions.Toggle);
        Assert.Equal(KeyboardOptions.Escape, core.KeyOptions.Remaps[KeyboardOptions.CapsLock]);

        var cycled = core.Handle(new LayoutCycled());
        Assert.Equal(1, cycled.KeyboardLayout?.Active);
        Assert.Contains("activelayout>>keyboard,Swedish", CommandProtocol.FormatStream(cycled.Events, cycled.State));
        Assert.Equal(0, core.Handle(new LayoutCycled()).KeyboardLayout?.Active);

        using var devices = JsonDocument.Parse(core.Execute("j/devices").Text);
        Assert.Equal(0, devices.RootElement.GetProperty("keyboards")[0].GetProperty("active_layout_index").GetInt32());
    }
}
