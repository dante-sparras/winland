using Winland.Core;

namespace Winland.Core.Tests.Conformance;

/// <summary>Winland's misc cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class MiscConformance
{
    [Fact]
    public void Misc()
    {
        const string special = "special:test";
        var desk = new Desk("""
            misc {
                close_special_on_empty = false
            }
            """);
        desk.Ok("workspace", special);
        desk.Spawn("a");

        Assert.Contains(special, ShownSpecials(desk));

        desk.Close("a");
        Assert.Contains(special, ShownSpecials(desk));

        desk.Spawn("a");
        desk.Keyword("misc:close_special_on_empty", "true");
        desk.Close("a");

        Assert.DoesNotContain(special, ShownSpecials(desk));
    }

    // The special workspace each monitor shows, as the reference reads "special workspace:" from its monitors reply.
    private static string[] ShownSpecials(Desk desk)
    {
        using var monitors = System.Text.Json.JsonDocument.Parse(desk.Send("j/monitors"));
        return [.. monitors.RootElement.EnumerateArray().Select(monitor => monitor.GetProperty("specialWorkspace").GetProperty("name").GetString() ?? "")];
    }
}
