using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        Assert.Equal(new Notification("It broke", new BorderColor(0xFF, 0xFF, 0x44, 0x44), 5000, 3, 20, 1), added.Notification);
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

    [Fact]
    // As in the pinned release, a pause reads its elapsed time after the lock is taken, so it holds 0 and a resume
    // starts the timeout again.
    public void APauseHoldsZeroAndAResumeStartsTheTimeoutAgain()
    {
        var (core, advance) = Clocked();
        core.Execute(Note("create", ("text", "hi"), ("timeout", 5000), ("icon", 6), ("color", 0), ("font", 13)));

        advance(2000);
        Assert.Equal("true", core.Execute(Note("pause", ("id", 1))).Text);
        advance(3000);
        var paused = View(core.Execute(Note("query", ("id", 1))).Text);
        Assert.Equal(0, paused.GetProperty("elapsed").GetInt64());
        Assert.Equal(5000, paused.GetProperty("since").GetInt64());
        Assert.True(paused.GetProperty("paused").GetBoolean());

        advance(10000);
        Assert.Equal(0, View(core.Execute(Note("query", ("id", 1))).Text).GetProperty("elapsed").GetInt64());
        Assert.Equal("true", core.Execute(Note("resume", ("id", 1))).Text);
        advance(1000);
        var resumed = View(core.Execute(Note("query", ("id", 1))).Text);
        Assert.Equal(1000, resumed.GetProperty("elapsed").GetInt64());
        Assert.False(resumed.GetProperty("paused").GetBoolean());
    }

    [Fact]
    public void ASecondPauseKeepsTheHeldTimeAndAResumeAtZeroDoesNothing()
    {
        var (core, advance) = Clocked();
        core.Execute(Note("create", ("text", "hi"), ("timeout", 5000), ("icon", 6), ("color", 0), ("font", 13)));
        advance(1500);
        core.Execute(Note("pause", ("id", 1)));
        core.Execute(Note("pause", ("id", 1)));
        advance(4000);
        Assert.Equal(0, View(core.Execute(Note("query", ("id", 1))).Text).GetProperty("elapsed").GetInt64());

        core.Execute(Note("resume", ("id", 1)));
        advance(1000);
        Assert.Equal(0, View(core.Execute(Note("query", ("id", 1))).Text).GetProperty("elapsed").GetInt64());
        Assert.True(View(core.Execute(Note("query", ("id", 1))).Text).GetProperty("paused").GetBoolean());

        core.Execute(Note("resume", ("id", 1)));
        core.Execute(Note("resume", ("id", 1)));
        advance(500);
        var resumed = View(core.Execute(Note("query", ("id", 1))).Text);
        Assert.Equal(500, resumed.GetProperty("elapsed").GetInt64());
        Assert.False(resumed.GetProperty("paused").GetBoolean());
    }

    [Fact]
    public void SetTimeoutResetsTheElapsedTimeAndAllowsZero()
    {
        var (core, advance) = Clocked();
        core.Execute(Note("create", ("text", "hi"), ("timeout", 5000), ("icon", 6), ("color", 0), ("font", 13)));
        advance(2000);
        core.Execute(Note("pause", ("id", 1)));
        Assert.Equal("true", core.Execute(Note("timeout", ("id", 1), ("timeout", 100))).Text);
        var held = View(core.Execute(Note("query", ("id", 1))).Text);
        Assert.Equal(0, held.GetProperty("elapsed").GetInt64());
        Assert.Equal(100, held.GetProperty("timeout").GetDouble());
        core.Execute(Note("resume", ("id", 1)));
        advance(101);
        Assert.Equal("null", core.Execute(Note("query", ("id", 1))).Text);

        core.Execute(Note("create", ("text", "again"), ("timeout", 5000), ("icon", 6), ("color", 0), ("font", 13)));
        advance(500);
        Assert.Equal("true", core.Execute(Note("timeout", ("id", 2), ("timeout", 8000))).Text);
        var reset = View(core.Execute(Note("query", ("id", 2))).Text);
        Assert.Equal(0, reset.GetProperty("elapsed").GetInt64());
        Assert.Equal(8000, reset.GetProperty("timeout").GetDouble());
        Assert.Equal("true", core.Execute(Note("timeout", ("id", 2), ("timeout", 0))).Text);
        advance(1);
        Assert.Equal("null", core.Execute(Note("query", ("id", 2))).Text);
    }

    [Fact]
    public void ANotificationExpiresAfterItsWholeMillisecondsUnlessPaused()
    {
        var (core, advance) = Clocked();
        core.Execute(Note("create", ("text", "hi"), ("timeout", 1000), ("icon", 6), ("color", 0), ("font", 13)));
        advance(1000);
        Assert.Equal(1000, View(core.Execute(Note("query", ("id", 1))).Text).GetProperty("elapsed").GetInt64());
        advance(1);
        Assert.Equal("null", core.Execute(Note("query", ("id", 1))).Text);

        core.Execute(Note("create", ("text", "held"), ("timeout", 1000), ("icon", 6), ("color", 0), ("font", 13)));
        core.Execute(Note("pause", ("id", 2)));
        advance(5000);
        Assert.Equal(0, View(core.Execute(Note("query", ("id", 2))).Text).GetProperty("elapsed").GetInt64());
    }

    [Fact]
    public void ColorZeroAtCreateTakesTheIconColorAndSetColorKeepsZero()
    {
        var core = new Core();
        core.Execute(Note("create", ("text", "hi"), ("timeout", 1000), ("icon", 1), ("color", 0), ("font", 13)));
        var created = View(core.Execute(Note("query", ("id", 1))).Text);
        Assert.Equal(NotificationColor.Bits(NotificationFrames.IconColor(1)), created.GetProperty("color").GetInt64());

        Assert.Equal("true", core.Execute(Note("color", ("id", 1), ("color", 0))).Text);
        Assert.Equal(0, View(core.Execute(Note("query", ("id", 1))).Text).GetProperty("color").GetInt64());
    }

    [Fact]
    public void GetListsTheOldestFirstAndDismissRemovesOne()
    {
        var core = new Core();
        core.Execute(Note("create", ("text", "one"), ("timeout", 1000), ("icon", 6), ("color", 1), ("font", 13)));
        core.Execute(Note("create", ("text", "two"), ("timeout", 1000), ("icon", 6), ("color", 1), ("font", 13)));

        using var list = JsonDocument.Parse(core.Execute(Note("get")).Text);
        Assert.Equal(["one", "two"], list.RootElement.EnumerateArray().Select(item => item.GetProperty("text").GetString()));
        Assert.Equal("true", core.Execute(Note("dismiss", ("id", 1))).Text);
        Assert.Equal("null", core.Execute(Note("query", ("id", 1))).Text);
        Assert.Equal("two", View(core.Execute(Note("query", ("id", 2))).Text).GetProperty("text").GetString());
    }

    [Fact]
    public void DismissNotifySweepsExpiredNotificationsThenTheOldest()
    {
        var (core, advance) = Clocked();
        core.Execute(Note("create", ("text", "old"), ("timeout", 1000), ("icon", 6), ("color", 1), ("font", 13)));
        advance(1001);
        var created = core.Execute(Note("create", ("text", "new"), ("timeout", 5000), ("icon", 6), ("color", 1), ("font", 13)));
        Assert.Equal(new NotificationDismissed(1), created.Effect.Notifications[0]);
        Assert.IsType<NotificationAdded>(created.Effect.Notifications[1]);

        Assert.Equal(new NotificationsDismissed(1), Assert.Single(core.Execute("dismissnotify 1").Effect.Notifications));
        Assert.Equal("[]", core.Execute(Note("get")).Text);
    }

    [Fact]
    public void RestoreKeepsItsIdAndATakenIdIsANewOne()
    {
        var (core, advance) = Clocked();
        var restored = core.Execute(Note(
            "restore",
            ("id", 5),
            ("text", "back"),
            ("timeout", 5000),
            ("icon", 1),
            ("color", 255),
            ("font", 13),
            ("elapsed", 100),
            ("since", 200),
            ("locks", 1)));
        Assert.Contains("\"id\":5", restored.Text, StringComparison.Ordinal);
        advance(3000);
        var view = View(core.Execute(Note("query", ("id", 5))).Text);
        Assert.Equal(100, view.GetProperty("elapsed").GetInt64());
        Assert.Equal(3200, view.GetProperty("since").GetInt64());
        Assert.True(view.GetProperty("paused").GetBoolean());

        var other = new Core();
        other.Execute(Note("create", ("text", "live"), ("timeout", 5000), ("icon", 6), ("color", 1), ("font", 13)));
        var again = other.Execute(Note(
            "restore",
            ("id", 1),
            ("text", "other"),
            ("timeout", 5000),
            ("icon", 6),
            ("color", 1),
            ("font", 13),
            ("elapsed", 0),
            ("since", 0),
            ("locks", 0)));
        Assert.Contains("\"id\":2", again.Text, StringComparison.Ordinal);
        Assert.Equal("other", View(other.Execute(Note("query", ("id", 2))).Text).GetProperty("text").GetString());
        Assert.Equal("live", View(other.Execute(Note("query", ("id", 1))).Text).GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("notification")]
    [InlineData("notification {}")]
    [InlineData("notification not-json")]
    [InlineData("notification {\"op\":\"nope\"}")]
    public void ABadNotificationCommandIsRejected(string command) =>
        Assert.Equal("error: invalid notification", new Core().Execute(command).Text);

    [Fact]
    public void NotifyAndTheNotificationCommandShareIds()
    {
        var core = new Core();
        core.Execute("notify 1 60000 0 hello");
        Assert.Equal("hello", View(core.Execute(Note("query", ("id", 1))).Text).GetProperty("text").GetString());
        Assert.Contains("\"id\":2", core.Execute(Note("create", ("text", "next"), ("timeout", 1000), ("icon", 6), ("color", 1), ("font", 13))).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0", 0u)]
    [InlineData("0xFFFF0000", 0xFFFF0000u)]
    [InlineData("#f00", 0xFFFF0000u)]
    [InlineData("#ff0000", 0xFFFF0000u)]
    [InlineData("#ff000080", 0x80FF0000u)]
    [InlineData("rgb(ff0000)", 0xFFFF0000u)]
    [InlineData("rgb(255, 0, 0)", 0xFFFF0000u)]
    [InlineData("rgba(ff000080)", 0x80FF0000u)]
    [InlineData("rgba(255, 0, 0, 0.5)", 0x7FFF0000u)]
    public void ColorsParseAsThePinnedReleaseDoes(string text, uint bits)
    {
        Assert.True(NotificationColor.TryParse(text, out var parsed));
        Assert.Equal(bits, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("rgb(ff0000) 45deg")]
    public void AColorThatIsNotOneIsRejected(string text) => Assert.False(NotificationColor.TryParse(text, out _));

    [Fact]
    public void CardsHoldAPauseAndAChangeDoesNotResetTheTimeSinceCreation()
    {
        var cards = new List<NotificationCard>();
        var color = new BorderColor(0xFF, 1, 2, 3);
        var added = new NotificationAdded(
            new Notification("a", color, 1000, 6, 13, 1),
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(0, 0, 1920, 1080),
            1,
            "Segoe UI");
        NotificationCards.Apply(cards, added, 0);
        Assert.Equal(400, cards[0].Elapsed(400));
        Assert.Equal(400, cards[0].Since(400));

        NotificationCards.Apply(cards, new NotificationChanged(1, "b", color, 1000, 6, 13, true, 400), 400);
        Assert.Equal("b", cards[0].Notification.Text);
        Assert.Equal(400, cards[0].Elapsed(900));
        Assert.Equal(900, cards[0].Since(900));
        Assert.False(cards[0].Gone(5000));

        NotificationCards.Apply(cards, new NotificationChanged(1, "b", color, 1000, 6, 13, false, 100), 900);
        Assert.Equal(100, cards[0].Elapsed(900));
        Assert.Equal(900, cards[0].Since(900));
        NotificationCards.Apply(cards, new NotificationDismissed(9), 900);
        Assert.Single(cards);
        NotificationCards.Apply(cards, new NotificationDismissed(1), 900);
        Assert.Empty(cards);
    }

    [Fact]
    public void ANegativeDismissNotifyClearsTheCards()
    {
        var cards = new List<NotificationCard>();
        var added = new NotificationAdded(
            new Notification("a", default, 1000, 6, 13, 1),
            new Rectangle(0, 0, 100, 100),
            new Rectangle(0, 0, 100, 100),
            1,
            "Segoe UI");
        NotificationCards.Apply(cards, added, 0);
        NotificationCards.Apply(cards, added with { Notification = added.Notification with { Id = 2 } }, 0);
        NotificationCards.Apply(cards, new NotificationsDismissed(-5), 0);
        Assert.Empty(cards);

        NotificationCards.Apply(cards, added, 0);
        NotificationCards.Expire(cards, 1001);
        Assert.Empty(cards);
    }

    private static (Core Core, Action<long> Advance) Clocked()
    {
        var now = Stopwatch.GetTimestamp();
        var core = new Core();
        core.UseNotificationClock(() => now);
        return (core, milliseconds => now += milliseconds * Stopwatch.Frequency / 1000);
    }

    private static string Note(string op, params (string Name, object Value)[] fields)
    {
        var body = new JsonObject { ["op"] = op };
        foreach (var (name, value) in fields)
        {
            body[name] = value switch
            {
                string text => JsonValue.Create(text),
                int number => JsonValue.Create(number),
                long number => JsonValue.Create(number),
                _ => throw new InvalidOperationException(),
            };
        }

        return "notification " + body.ToJsonString();
    }

    private static JsonElement View(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
