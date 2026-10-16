namespace Winland.Tests.Conformance;

/// <summary>The pinned release's window tag cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class TagsConformance
{
    [Fact(Skip = "The tag and nodim window rule effects are refused, and keyword does not apply windowrulev2 live. getprop does not read no_dim or no_shadow.")]
    public void Tags()
    {
        var desk = new Desk();
        desk.Spawn("tagged");
        desk.Spawn("untagged");

        Assert.Equal("ok", desk.Send("keyword windowrulev2 tag +testTag, class:tagged"));
        Assert.Equal("ok", desk.Send("keyword windowrulev2 noshadow, tag:negative:testTag"));
        Assert.Equal("ok", desk.Send("keyword windowrulev2 nodim, tag:testTag"));

        Assert.Equal(2, desk.Clients().Length);
        desk.Ok("focuswindow", "class:tagged");
        Assert.Contains("testTag", ActiveTags(desk), StringComparison.Ordinal);
        Assert.Contains("true", desk.Send("getprop activewindow no_dim"), StringComparison.Ordinal);
        Assert.Contains("false", desk.Send("getprop activewindow no_shadow"), StringComparison.Ordinal);

        desk.Ok("focuswindow", "class:untagged");
        Assert.DoesNotContain("testTag", ActiveTags(desk), StringComparison.Ordinal);
        Assert.Contains("true", desk.Send("getprop activewindow no_shadow"), StringComparison.Ordinal);
        Assert.Contains("false", desk.Send("getprop activewindow no_dim"), StringComparison.Ordinal);
    }

    [Fact(Skip = "tagwindow is not a dispatcher, window tags are not tracked, and keyword does not apply windowrulev2 live.")]
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

            // The reference turns the rule off here. The line-based syntax has no way to do that.
        }
    }

    // The active window's tags, written as the reference text reply writes them.
    private static string ActiveTags(Desk desk) =>
        string.Join(", ", desk.ActiveWindow().GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
}
