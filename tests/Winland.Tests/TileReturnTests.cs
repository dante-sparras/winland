using FluentAssertions;
using Winland.Core.Tiling;

namespace Winland.Tests;

[TestClass]
public sealed class TileReturnTests
{
    [TestMethod]
    public void Tiles_that_share_an_edge_share_it_on_every_sample_of_a_glide()
    {
        // Three columns become two. The middle window moves and widens, and the right one moves its left edge with it.
        var middle = new ReturnWindow(1, new TileRect(300, 0, 300, 400), new TileRect(0, 0, 450, 400));
        var right = new ReturnWindow(2, new TileRect(600, 0, 300, 400), new TileRect(450, 0, 450, 400));

        foreach (var (now, drawn) in Glide(middle, right))
        {
            (drawn[1].X + drawn[1].Width).Should().Be(drawn[2].X, "the tiles meet at " + now + " ms");
        }
    }

    [TestMethod]
    public void An_edge_that_stays_where_it_is_does_not_move_during_a_glide()
    {
        // The left edge travels 100 pixels and the right edge none. Halfway, both x and width fall on half a pixel.
        var window = new ReturnWindow(1, new TileRect(400, 0, 400, 300), new TileRect(500, 0, 300, 300));

        foreach (var (now, drawn) in Glide(window))
        {
            (drawn[1].X + drawn[1].Width).Should().Be(800, "the right edge rests at " + now + " ms");
        }
    }

    [TestMethod]
    public void A_home_that_moves_a_pixel_during_a_glide_is_where_the_glide_ends()
    {
        var from = new TileRect(0, 0, 400, 300);
        var glide = new TileReturn();
        glide.Step([new ReturnWindow(1, from, new TileRect(200, 0, 400, 300))], 0);

        // The tile is one pixel wider on the next sample. The ease goes on from where it was, and ends on the new tile.
        var home = new TileRect(200, 0, 401, 300);
        glide.Step([new ReturnWindow(1, from, home)], 80);
        var last = glide.Step([new ReturnWindow(1, from, home)], TileReturn.DurationMilliseconds);

        last.Should().Equal(new ReturnFrame(1, home, Settled: true));
    }

    [TestMethod]
    public void A_window_that_only_moves_keeps_its_size_on_every_sample()
    {
        var window = new ReturnWindow(1, new TileRect(0, 0, 400, 300), new TileRect(333, 127, 400, 300));

        foreach (var (now, drawn) in Glide(window))
        {
            (drawn[1].Width, drawn[1].Height).Should().Be((400, 300), "the size holds at " + now + " ms");
        }
    }

    [TestMethod]
    public void A_glide_ends_on_the_tile_exactly()
    {
        var window = new ReturnWindow(1, new TileRect(13, 7, 401, 299), new TileRect(500, 0, 300, 300));

        var last = Glide(window).Last();

        last.Now.Should().Be(TileReturn.DurationMilliseconds);
        last.Drawn[1].Should().Be(window.Home);
    }

    /// <summary>Every millisecond of a glide that starts at zero, with where each window is drawn then.</summary>
    private static IEnumerable<(long Now, Dictionary<long, TileRect> Drawn)> Glide(params ReturnWindow[] windows)
    {
        var glide = new TileReturn();
        glide.Step(windows, 0);
        var drawn = windows.ToDictionary(window => window.Id, window => window.Current);
        for (long now = 1; now <= TileReturn.DurationMilliseconds; now++)
        {
            foreach (var frame in glide.Step(windows, now))
            {
                drawn[frame.Id] = frame.Rect;
            }

            yield return (now, drawn);
        }
    }
}
