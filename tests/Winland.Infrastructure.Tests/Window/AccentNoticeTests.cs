using System.Runtime.InteropServices;

namespace Winland.Infrastructure.Tests;

public class AccentNoticeTests
{
    [Fact]
    public void ColorizationAndTheImmersiveColorSetAskForANewAccent()
    {
        Assert.True(AccentNotices.Changed(AccentNotices.ColorizationChanged, 0));

        var immersive = Marshal.StringToHGlobalUni("ImmersiveColorSet");
        var other = Marshal.StringToHGlobalUni("Policy");
        try
        {
            Assert.True(AccentNotices.Changed(AccentNotices.SettingChange, immersive));
            Assert.False(AccentNotices.Changed(AccentNotices.SettingChange, other));
            Assert.False(AccentNotices.Changed(AccentNotices.SettingChange, 0));
            Assert.False(AccentNotices.Changed(0x0010, immersive));
        }
        finally
        {
            Marshal.FreeHGlobal(immersive);
            Marshal.FreeHGlobal(other);
        }
    }

    [Fact]
    public void AReadAccentIsOpaque()
    {
        var color = SystemAccent.ReadWindows();
        if (color is BorderColor read)
            Assert.Equal(0xFF, read.A);
    }

    [Fact]
    public void TheHostFeedsTheAccentIntoTheCoreAndSkipsAnUnchangedRead()
    {
        var previous = SystemAccent.Read;
        var reads = 0;
        SystemAccent.Read = () =>
        {
            reads++;
            return new BorderColor(0xFF, 0x11, 0x22, 0x33);
        };
        try
        {
            var core = new WindowManager();
            core.Handle(new Configure(ConfigParser.Parse("")));
            using var host = new WindowHost(core);

            host.NoteAccent();
            host.NoteAccent();

            Assert.Equal(2, reads);
            Assert.Equal(
                "{\"option\":\"general:col.active_border\",\"str\":\"0xFF112233\",\"set\":false}",
                core.Execute("getoption general:col.active_border").Text);
        }
        finally
        {
            SystemAccent.Read = previous;
        }
    }

    [Fact]
    public void AConfiguredBorderWinsOverTheAccentTheHostReads()
    {
        var previous = SystemAccent.Read;
        SystemAccent.Read = () => new BorderColor(0xFF, 0x11, 0x22, 0x33);
        try
        {
            var core = new WindowManager();
            core.Handle(new Configure(ConfigParser.Parse("general:col.active_border = rgba(aabbccff)\n")));
            using var host = new WindowHost(core);

            host.NoteAccent();

            Assert.Equal(
                "{\"option\":\"general:col.active_border\",\"str\":\"0xFFAABBCC\",\"set\":true}",
                core.Execute("getoption general:col.active_border").Text);

            core.Handle(new Configure(ConfigParser.Parse("")));
            Assert.Equal(
                "{\"option\":\"general:col.active_border\",\"str\":\"0xFF112233\",\"set\":false}",
                core.Execute("getoption general:col.active_border").Text);
        }
        finally
        {
            SystemAccent.Read = previous;
        }
    }
}
