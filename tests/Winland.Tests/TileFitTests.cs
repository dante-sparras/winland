using FluentAssertions;
using Winland.Core.Tiling;

namespace Winland.Tests;

[TestClass]
public sealed class TileFitTests
{
    private static readonly TileRect Area = new(0, 0, 1000, 800);

    [TestMethod]
    public void A_reported_minimum_is_clamped_and_recorded_before_the_first_place()
    {
        var session = Pair(new TileRect(0, 0, 400, 300));
        var glass = new Glass
        {
            Reported = [new WindowMinimum(2, 700, 200)],
        };

        TileFit.Place(session, glass, new TileRect(0, 0, 400, 300), jump: false);

        glass.Glides.Should().ContainSingle();
        var placed = glass.Glides[0].Where(item => item.Visible).ToDictionary(item => item.Id);
        placed[2].Width.Should().Be(400);
        placed[2].Height.Should().Be(200);
        placed[1].Width.Should().Be(400);
        placed[1].Height.Should().Be(100);
    }

    [TestMethod]
    public void A_window_that_never_answers_its_minimum_is_given_up_on_after_a_few_places()
    {
        var session = Pair(Area);
        var glass = new Glass();

        for (var place = 0; place < TilingSession.MinimumTries + 2; place++)
        {
            TileFit.Place(session, glass, Area, jump: false);
        }

        glass.Asked.Should().HaveCount(TilingSession.MinimumTries);

        TileFit.Place(session, glass, new TileRect(0, 0, 900, 800), jump: false);

        glass.Asked.Should().HaveCount(TilingSession.MinimumTries + 1, "another area asks again");
    }

    [TestMethod]
    public void A_window_fullscreen_in_its_app_keeps_the_minimum_it_had()
    {
        var session = Pair(Area);
        TileFit.Place(session, new Glass { Reported = [new WindowMinimum(2, 700, 200)] }, Area, jump: false);
        var glass = new Glass
        {
            Reported = [new WindowMinimum(2, 300, 50)],
            Kept = _ => [new WindowMinimum(2, 900, 800)],
        };

        TileFit.Place(session, glass, Area, Area, jump: false, held: new HashSet<long> { 2 });

        glass.Glides.Should().ContainSingle();
        var placed = glass.Glides[0].Where(item => item.Visible).ToDictionary(item => item.Id);
        placed[2].Width.Should().Be(700);
        placed[1].Width.Should().Be(300);
    }

    [TestMethod]
    public void A_window_still_as_wide_as_the_workspace_is_placed_again_before_that_becomes_its_minimum()
    {
        var session = Pair(Area);
        var passes = 0;
        var glass = new Glass
        {
            Kept = _ =>
            {
                passes++;
                return passes == 1 ? [new WindowMinimum(1, Area.Width, 0)] : [];
            },
        };

        TileFit.Place(session, glass, Area, jump: false);

        glass.Glides.Should().HaveCount(2);
        var last = glass.Glides[^1].Where(item => item.Visible).ToDictionary(item => item.Id);
        last[1].Width.Should().BeLessThan(Area.Width);
        last[2].X.Should().BeGreaterThan(last[1].X);
        last[1].Y.Should().Be(last[2].Y);
    }

    [TestMethod]
    public void A_window_that_stays_as_wide_as_the_workspace_stacks_with_its_neighbor()
    {
        var session = Pair(Area);
        var glass = new Glass
        {
            Kept = _ => [new WindowMinimum(1, Area.Width, 0)],
        };

        TileFit.Place(session, glass, Area, jump: false);

        var last = glass.Glides[^1].Where(item => item.Visible).OrderBy(item => item.Y).ToList();
        last.Should().OnlyContain(item => item.Width == Area.Width && item.X == 0);
        last[1].Y.Should().BeGreaterThan(last[0].Y);
    }

    [TestMethod]
    public void An_overflow_places_again()
    {
        var session = Pair(Area);
        var passes = 0;
        var glass = new Glass
        {
            Kept = _ =>
            {
                passes++;
                return passes == 1 ? [new WindowMinimum(2, 700, 200)] : [];
            },
        };

        TileFit.Place(session, glass, Area, jump: false);

        glass.Glides.Should().HaveCount(2);
        var second = glass.Glides[1].Where(item => item.Visible).ToDictionary(item => item.Id);
        second[2].Width.Should().Be(700);
        second[1].Width.Should().Be(300);
    }

    [TestMethod]
    public void A_fit_places_once()
    {
        var session = Pair(Area);
        var glass = new Glass();

        TileFit.Place(session, glass, Area, jump: false);

        glass.Glides.Should().ContainSingle();
        glass.Jumps.Should().BeEmpty();
    }

    [TestMethod]
    public void An_unchanged_minimum_still_places_once()
    {
        var session = Pair(Area);
        var glass = new Glass { Reported = [new WindowMinimum(2, 700, 200)] };

        TileFit.Place(session, glass, Area, jump: false);
        TileFit.Place(session, glass, Area, jump: false);

        glass.Glides.Should().HaveCount(2);
        glass.Glides[1].Where(item => item.Visible).Single(item => item.Id == 2).Width.Should().Be(700);
    }

    [TestMethod]
    public void The_fifth_place_never_happens()
    {
        var session = Pair(Area);
        var kept = 0;
        var glass = new Glass
        {
            Kept = _ =>
            {
                kept += 100;
                return [new WindowMinimum(2, kept, 200)];
            },
        };

        TileFit.Place(session, glass, Area, jump: false);

        glass.Glides.Should().HaveCount(4);
    }

    [TestMethod]
    public void Jump_places_immediately()
    {
        var session = Pair(Area);
        var glass = new Glass();

        TileFit.Place(session, glass, Area, jump: true);

        glass.Jumps.Should().ContainSingle();
        glass.Glides.Should().BeEmpty();
    }

    [TestMethod]
    public void A_window_that_answered_is_not_asked_again_in_the_same_area()
    {
        var session = Pair(Area);
        var glass = new Glass { Reported = [new WindowMinimum(1, 100, 100), new WindowMinimum(2, 700, 200)] };

        TileFit.Place(session, glass, Area, jump: false);
        TileFit.Place(session, glass, Area, jump: false);

        glass.Asked.Should().ContainSingle().Which.Should().BeEquivalentTo([1L, 2L]);
        glass.Glides[1].Where(item => item.Visible).Single(item => item.Id == 2).Width.Should().Be(700);
    }

    [TestMethod]
    public void A_new_window_is_asked_alone()
    {
        var session = Pair(Area);
        var glass = new Glass { Reported = [new WindowMinimum(1, 100, 100), new WindowMinimum(2, 100, 100), new WindowMinimum(3, 100, 100)] };
        TileFit.Place(session, glass, Area, jump: false);

        session.Add(3, Area);
        TileFit.Place(session, glass, Area, jump: false);

        glass.Asked[1].Should().BeEquivalentTo([3L]);
    }

    [TestMethod]
    public void Another_area_asks_every_window_again()
    {
        var session = Pair(Area);
        var glass = new Glass { Reported = [new WindowMinimum(1, 100, 100), new WindowMinimum(2, 100, 100)] };
        TileFit.Place(session, glass, Area, jump: false);

        TileFit.Place(session, glass, new TileRect(1000, 0, 1000, 800), jump: false);

        glass.Asked.Should().HaveCount(2);
        glass.Asked[1].Should().BeEquivalentTo([1L, 2L]);
    }

    [TestMethod]
    public void A_window_that_did_not_answer_is_asked_on_the_next_place()
    {
        var session = Pair(Area);
        var glass = new Glass { Reported = [new WindowMinimum(1, 100, 100)] };
        TileFit.Place(session, glass, Area, jump: false);

        TileFit.Place(session, glass, Area, jump: false);

        glass.Asked[1].Should().BeEquivalentTo([2L]);
    }

    [TestMethod]
    public void A_window_that_kept_a_larger_size_is_asked_again_on_the_next_place()
    {
        var session = Pair(Area);
        var places = 0;
        var glass = new Glass
        {
            Reported = [new WindowMinimum(1, 100, 100), new WindowMinimum(2, 100, 100)],
            Kept = _ => ++places == 1 ? [new WindowMinimum(2, 700, 0)] : [],
        };
        TileFit.Place(session, glass, Area, jump: false);

        TileFit.Place(session, glass, Area, jump: false);

        glass.Asked[1].Should().BeEquivalentTo([2L]);
    }

    [TestMethod]
    public void Held_and_fullscreen_windows_are_not_asked()
    {
        var session = Pair(Area);
        session.Add(3, Area);
        session.Focus(3);
        session.ToggleFullscreen();
        var glass = new Glass();

        TileFit.Place(session, glass, Area, Area, jump: false, held: new HashSet<long> { 2 });

        glass.Asked.Should().ContainSingle().Which.Should().BeEquivalentTo([1L]);
    }

    [TestMethod]
    public void A_window_drawn_with_the_tiles_that_is_larger_than_the_area_costs_no_second_placement()
    {
        // The tile of the Workspace in front on a wider monitor is named in this placement too, and is wider than this whole area.
        var session = Pair(Area);
        var beside = new Placement(9, 1000, 0, 1600, 800, true, false);
        var glass = new Glass
        {
            Kept = placed => placed.Any(item => item.Id == 9) ? [new WindowMinimum(9, 1700, 0)] : [],
        };

        TileFit.Place(session, glass, Area, Area, jump: false, also: [beside]);

        glass.Glides.Should().ContainSingle("only this session's own tiles are fitted here");
        glass.Glides[0].Should().Contain(beside);
    }

    private static TilingSession Pair(TileRect area)
    {
        var session = new TilingSession();
        session.Add(1, area);
        session.Add(2, area);
        return session;
    }

    private sealed class Glass : ITilePlacement
    {
        public List<WindowMinimum> Reported { get; init; } = [];

        public Func<IReadOnlyList<Placement>, IReadOnlyList<WindowMinimum>>? Kept { get; init; }

        public List<IReadOnlyList<Placement>> Glides { get; } = [];

        public List<IReadOnlyList<Placement>> Jumps { get; } = [];

        public List<IReadOnlyCollection<long>> Asked { get; } = [];

        public IReadOnlyList<WindowMinimum> Minimums(IReadOnlyCollection<long> ids)
        {
            Asked.Add(ids.ToArray());
            return Reported.Where(minimum => ids.Contains(minimum.Id)).ToList();
        }

        public void Apply(IReadOnlyList<Placement> placements) => Glides.Add(placements);

        public void PlaceNow(IReadOnlyList<Placement> placements) => Jumps.Add(placements);

        public IReadOnlyList<WindowMinimum> Overflows(IReadOnlyList<Placement> placements) =>
            Kept?.Invoke(placements) ?? [];
    }
}
