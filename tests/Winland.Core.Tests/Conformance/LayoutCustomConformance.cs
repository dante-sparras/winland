namespace Winland.Tests.Conformance;

/// <summary>
/// The pinned release's custom layout cases, run with a Lua config that registers the two layouts the reference's
/// config registers. Each test is named after its case and sends the same requests.
/// </summary>
[Trait("Category", "Conformance")]
public class LayoutCustomConformance
{
    // The layouts the reference harness's config registers with hl.layout.register.
    private const string Layouts = """
        hl.layout.register("columns", {
            recalculate = function(ctx)
                local n = #ctx.targets
                if n == 0 then
                    return
                end

                for i, target in ipairs(ctx.targets) do
                    target:place(ctx:column(i, n))
                end
            end,
        })

        hl.layout.register("grid", {
            recalculate = function(ctx)
                local n = #ctx.targets
                if n == 0 then
                    return
                end

                local cols = math.ceil(math.sqrt(n))

                for i, target in ipairs(ctx.targets) do
                    target:place(ctx:grid_cell(i, cols))
                end
            end,
        })
        """;

    [Fact]
    public void LayoutCustomGrid()
    {
        using var desk = Desk.WithLua(Layouts);
        Assert.Equal("ok", desk.Send("eval hl.config({ general = { layout = 'lua:grid' } })"));

        desk.Spawn("kitty_A");
        desk.Spawn("kitty_B");
        Assert.Equal(2, Sizes(desk).Count(size => size == "931,1036"));

        desk.Spawn("kitty_C");
        Assert.Equal(3, Sizes(desk).Count(size => size == "931,511"));

        desk.Spawn("kitty_D");
        Assert.Equal(4, Sizes(desk).Count(size => size == "931,511"));
    }

    [Fact]
    public void LayoutCustomColumns()
    {
        using var desk = Desk.WithLua(Layouts);
        Assert.Equal("ok", desk.Send("eval hl.config({ general = { layout = 'lua:columns' } })"));

        desk.Spawn("kitty_A");
        desk.Spawn("kitty_B");
        Assert.Equal(2, Sizes(desk).Count(size => size == "931,1036"));

        // Three and four columns do not split evenly, so the reference checks only their height.
        desk.Spawn("kitty_C");
        Assert.Equal(3, Sizes(desk).Count(size => size.EndsWith(",1036", StringComparison.Ordinal)));

        desk.Spawn("kitty_D");
        Assert.Equal(4, Sizes(desk).Count(size => size.EndsWith(",1036", StringComparison.Ordinal)));
    }

    // The size of each window, as the reference's clients reply writes it.
    private static string[] Sizes(Desk desk) =>
        [.. desk.Clients().Select(client => desk.PlaceOf(Desk.ClassOf(client)).Size)];
}
