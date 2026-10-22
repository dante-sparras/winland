namespace Winland.Application.Tests.Conformance;

/// <summary>Winland's window tag cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class TagsConformance
{
    [Fact]
    public void Tags()
    {
        var desk = new Desk();
        desk.Spawn("tagged");
        desk.Spawn("untagged");

        Assert.Equal("ok", desk.Send("keyword windowrulev2 tag +testTag, class:tagged"));
        Assert.Equal("ok", desk.Send("keyword windowrulev2 no_blur, tag:negative:testTag"));
        Assert.Equal("ok", desk.Send("keyword windowrulev2 allows_input, tag:testTag"));

        Assert.Equal(2, desk.Clients().Length);
        desk.Ok("focuswindow", "class:tagged");
        Assert.Contains("testTag", ActiveTags(desk), StringComparison.Ordinal);
        Assert.Contains("true", desk.Send("getprop activewindow allows_input"), StringComparison.Ordinal);
        Assert.Contains("false", desk.Send("getprop activewindow no_blur"), StringComparison.Ordinal);

        desk.Ok("focuswindow", "class:untagged");
        Assert.DoesNotContain("testTag", ActiveTags(desk), StringComparison.Ordinal);
        Assert.Contains("true", desk.Send("getprop activewindow no_blur"), StringComparison.Ordinal);
        Assert.Contains("false", desk.Send("getprop activewindow allows_input"), StringComparison.Ordinal);
    }

    [Fact]
    public void TagsFullscreenCleanup()
    {
        var desk = new Desk();
        desk.Spawn("hyprtester-16423-tags");
        desk.Ok("focuswindow", "class:hyprtester-16423-tags");
        desk.Ok("tagwindow", "+hyprtester-16423-foo");
        Assert.Equal("hyprtester-16423-foo", ActiveTags(desk));

        foreach (var tag in new[] { "+hyprtester-16423-foo", "hyprtester-16423-foo", "+hyprtester-16423-foo*", "hyprtester-16423-foo*" })
        {
            Assert.Equal("ok", desk.Send($"keyword windowrulev2 tag {tag}, class:^hyprtester-16423-tags$, fullscreen:1"));
            Assert.Equal("hyprtester-16423-foo", ActiveTags(desk));

            for (var cycle = 0; cycle < 2; cycle++)
            {
                desk.Ok("fullscreen", "0 set");

                // The static tag stays beside exactly one dynamic tag.
                Assert.Equal("hyprtester-16423-foo, hyprtester-16423-foo*", ActiveTags(desk));

                desk.Ok("fullscreen", "0 unset");
                Assert.Equal("hyprtester-16423-foo", ActiveTags(desk));
            }

            // The reference turns the rule off here. The line-based syntax has no way to, so a reload drops the rules
            // the keywords added.
            Assert.Equal("ok", desk.Send("reload"));
        }
    }

    // The active window's tags, written as the reference text reply writes them.
    private static string ActiveTags(Desk desk) =>
        string.Join(", ", desk.ActiveWindow().GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
}
