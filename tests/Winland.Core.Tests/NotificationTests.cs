using Winland;

namespace Winland.Tests;

public class NotificationTests
{
    [Theory]
    [InlineData("notify", "not enough args")]
    [InlineData("notify 1 1000 0", "not enough args")]
    [InlineData("notify x 1000 0 hi", "invalid arg 1")]
    [InlineData("notify 1 x 0 hi", "invalid arg 2")]
    [InlineData("notify 1 1000 blue hi", "invalid arg 3")]
    [InlineData("notify 1 1000 0 fontsize:x hi", "invalid fontsize kwarg")]
    [InlineData("notify 1 1000 0 fontsize:20", "not enough args")]
    [InlineData("dismissnotify x", "invalid arg 1")]
    public void BadArgumentsAnswerAsThePinnedReleaseDoes(string command, string reply) =>
        Assert.Equal(reply, new Core().Execute(command).Text);

    [Fact]
    public void ANotificationCarriesItsMessageColorAndPlace()
    {
        var core = new Core(ConfigParser.Parse("misc:font_family = Monospace\n"));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 2560, 1440, 1, true, new Rectangle(0, 40, 2560, 1400))]));

        var reply = core.Execute("notify 3 5000 rgb(ff4444) fontsize:20 It broke");

        Assert.Equal("ok", reply.Text);
        var added = Assert.IsType<NotificationAdded>(Assert.Single(reply.Effect.Notifications));
        Assert.Equal(new Notification("It broke", new BorderColor(0xFF, 0xFF, 0x44, 0x44), 5000, 3, 20), added.Notification);
        Assert.Equal(new Rectangle(0, 40, 2560, 1400), added.WorkArea);
        Assert.Equal("Consolas", added.Font);
    }

    [Fact]
    public void ColorZeroTakesTheIconColorAndAnUnknownIconIsNone()
    {
        var core = new Core();

        var info = Assert.IsType<NotificationAdded>(Assert.Single(core.Execute("notify 1 100 0 hi").Effect.Notifications));
        Assert.Equal(NotificationFrames.IconColor(1), info.Notification.Color);

        var none = Assert.IsType<NotificationAdded>(Assert.Single(core.Execute("notify 9 100 0 hi").Effect.Notifications));
        Assert.Equal(NotificationFrames.NoIcon, none.Notification.Icon);
        Assert.Equal("", NotificationFrames.IconText(none.Notification.Icon));
        Assert.Equal("[Err]", NotificationFrames.IconText(3));
    }

    [Fact]
    public void DismissTakesTheOldestOrAll()
    {
        var core = new Core();

        Assert.Equal(new NotificationsDismissed(2), Assert.Single(core.Execute("dismissnotify 2").Effect.Notifications));
        Assert.Equal(new NotificationsDismissed(-1), Assert.Single(core.Execute("dismissnotify").Effect.Notifications));
    }

    [Fact]
    public void CardsSlideInAndOutAroundTheirStrip()
    {
        Assert.Equal((0.0, 0.0), NotificationFrames.Slide(0, 0, 5000));
        Assert.Equal((1.0, 1.0), NotificationFrames.Slide(2000, 2000, 5000));

        var (strip, card) = NotificationFrames.Slide(300, 300, 5000);
        Assert.True(strip > card);

        var (leavingStrip, leavingCard) = NotificationFrames.Slide(4800, 4800, 5000);
        Assert.True(leavingStrip < 1 && leavingCard < 1);
        Assert.True(leavingCard < leavingStrip);
    }

    [Theory]
    [InlineData(13, 1920, 1, 13)]
    [InlineData(13, 2560, 1, 17)]
    [InlineData(1, 1920, 1, 8)]
    [InlineData(100, 3840, 2, 40)]
    public void TextScalesWithTheMonitorWidth(double fontSize, int width, double scale, int pixels) =>
        Assert.Equal(pixels, NotificationFrames.FontPixels(fontSize, width, scale));
}
