using System.Globalization;
using System.Text;

namespace Winland;

/// <summary>
/// Arranges the Workspaces whose Layout a config's script supplies, such as one <c>hl.layout.register</c> makes in
/// <c>winland.lua</c> (ADR 0013). The core calls it inside its lock, so it only queues the request: the script runs
/// later, on a thread of its own, and answers with the pipe command <see cref="LayoutRequest.Answer"/> writes.
/// </summary>
public interface ILayoutScripts
{
    /// <summary>
    /// Queues a request to lay out one Workspace. A later request for the same Layout replaces an arrangement that has
    /// not run yet, and one with no targets means the Layout has nothing to lay out. A request with a
    /// <see cref="LayoutRequest.Message"/> is a layout message, which is never dropped.
    /// </summary>
    void Arrange(LayoutRequest request);
}

/// <summary>
/// What a script Layout is to arrange: the Layout it is for, the request's generation, the registered name, the tiling
/// area and the tiled Clients in the Layout's order, each with the box it has now. <see cref="Message"/> is a
/// <c>layoutmsg</c> for the script, when the request carries one.
/// </summary>
public sealed record LayoutRequest(long Layout, long Generation, string Name, Rectangle Area, IReadOnlyList<LayoutTarget> Targets)
{
    /// <summary>The verb of the pipe command that answers a request.</summary>
    public const string Verb = "layoutplace";

    /// <summary>
    /// The config keyword that registers a Layout a script arranges, <c>lualayout = lua:NAME</c>, which
    /// <c>hl.layout.register</c> becomes. <c>general:layout</c> and a workspace rule then select it by that name.
    /// </summary>
    public const string Keyword = "lualayout";

    /// <summary>The prefix of the name of a Layout a script arranges.</summary>
    public const string Prefix = "lua:";

    public string? Message { get; init; }

    /// <summary>
    /// The pipe command that answers this request with the boxes the script placed, by the index of each target in
    /// <see cref="Targets"/>, or, when <paramref name="placed"/> is null, says the script failed and the Layout falls
    /// back to its grid. A target with no box keeps the one it has.
    /// </summary>
    public string Answer(IReadOnlyDictionary<int, LayoutBox>? placed)
    {
        var text = new StringBuilder();
        text.Append(Verb).Append(' ').Append(Layout.ToString(CultureInfo.InvariantCulture))
            .Append(' ').Append(Generation.ToString(CultureInfo.InvariantCulture));
        if (placed is null)
            return text.Append(" failed").ToString();

        foreach (var (index, box) in placed.OrderBy(pair => pair.Key))
        {
            if (index < 0 || index >= Targets.Count)
                continue;

            text.Append(" 0x").Append(Targets[index].Window.Value.ToString("x", CultureInfo.InvariantCulture));
            foreach (var value in (double[])[box.X, box.Y, box.Width, box.Height])
                text.Append(' ').Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }
}

/// <summary>A tiled Client a script Layout arranges, and the box it has now, before gaps and border.</summary>
public readonly record struct LayoutTarget(WindowId Window, Rectangle Box);

/// <summary>A box a script places a target at, in desktop pixels, which may be fractional.</summary>
public readonly record struct LayoutBox(double X, double Y, double Width, double Height);
