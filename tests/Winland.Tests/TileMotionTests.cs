using FluentAssertions;
using Winland.Core.Tiling;

namespace Winland.Tests;

[TestClass]
public sealed class TileMotionTests
{
    private static readonly Placement Tile = new(1, 0, 0, 400, 300, Visible: true);
    private static readonly TileRect OnTile = new(0, 0, 400, 300);

    // Where the tile that travels 80 right and 40 down is 80 ms into its glide.
    private static readonly TileRect MidGlide = Eased(MotionCurve.EaseOutQuint(80.0 / TileReturn.DurationMilliseconds));

    private static TileRect Eased(double progress)
    {
        static int Travel(int distance, double progress) => (int)Math.Round(distance * progress, MidpointRounding.AwayFromZero);
        var left = Travel(80, progress);
        var top = Travel(40, progress);
        return new TileRect(left, top, 400, 300);
    }
    private static readonly TileRect Elsewhere = new(50, 60, 200, 100);

    [TestMethod]
    public void A_window_that_was_never_on_a_tile_lands_there_at_once()
    {
        var motion = new TileMotion();

        var step = motion.Arrange([Tile], At(0), Frames((1, Elsewhere)));

        step.Moves.Should().Equal(new TileMove(1, TileMoveKind.Land, OnTile));
        step.Pace.Should().Be(TilePace.Parked);
    }

    [TestMethod]
    public void A_window_whose_tile_moved_glides_there()
    {
        var motion = Settled();
        var moved = Tile with { X = 80, Y = 40 };
        var frames = Frames((1, OnTile));

        var start = motion.Arrange([moved], At(1_000), frames);
        start.Moves.Should().BeEmpty();
        start.Gliding.Should().Equal(1);
        start.Pace.Should().Be(TilePace.Fine);
        motion.IsGliding(1).Should().BeTrue();

        // Ease-out cubic is 7/8 of the way at the halfway sample.
        var mid = motion.Arrange([moved], At(1_080), frames);
        mid.Moves.Should().Equal(new TileMove(1, TileMoveKind.Glide, MidGlide));
        mid.Pace.Should().Be(TilePace.Fine);

        var landed = motion.Arrange([moved], At(1_000 + TileReturn.DurationMilliseconds), frames);
        landed.Moves.Should().Equal(new TileMove(1, TileMoveKind.Glide, new TileRect(80, 40, 400, 300)));
        landed.Pace.Should().Be(TilePace.Parked);
        motion.IsGliding(1).Should().BeFalse();

        var after = motion.Arrange([moved], At(2_000), Frames((1, new TileRect(80, 40, 400, 300))));
        after.Moves.Should().BeEmpty();
        after.Gliding.Should().BeEmpty();
    }

    [TestMethod]
    public void A_held_button_keeps_unchanged_tiles_still_and_reads_no_window()
    {
        var motion = Settled();

        var step = motion.Arrange([Tile], At(0, mouseDown: true), _ => throw new InvalidOperationException("read a window"));

        step.Kind.Should().Be(TileStepKind.Held);
        step.Moves.Should().BeEmpty();
        step.Gliding.Should().BeEmpty();
        step.Pace.Should().Be(TilePace.Coast);
    }

    [TestMethod]
    public void A_window_pulled_off_its_tile_glides_home_when_the_button_comes_up()
    {
        var motion = Settled();
        var pulled = Frames((1, new TileRect(100, 100, 400, 300)));
        motion.Arrange([Tile], At(0, mouseDown: true), pulled);

        var release = motion.Arrange([Tile], At(1_000), pulled);
        release.Moves.Should().BeEmpty();
        release.Gliding.Should().Equal(1);
        release.Pace.Should().Be(TilePace.Fine);

        var landed = motion.Arrange([Tile], At(1_000 + TileReturn.DurationMilliseconds), pulled);
        landed.Moves.Should().Equal(new TileMove(1, TileMoveKind.Glide, OnTile));
        landed.Pace.Should().Be(TilePace.Parked);
    }

    [TestMethod]
    public void A_window_whose_tile_changed_size_and_place_glides_to_both()
    {
        // A tile on the left that swaps with one stacked on the right: it travels and it gets shorter.
        var motion = Settled();
        var swapped = Tile with { X = 400, Height = 150 };
        var frames = Frames((1, OnTile));

        motion.Arrange([swapped], At(1_000), frames).Gliding.Should().Equal(1);

        var mid = motion.Tick(At(1_040), frames).Moves.Should().ContainSingle().Subject;
        mid.Kind.Should().Be(TileMoveKind.Glide);
        mid.Rect.X.Should().BeInRange(1, 399);
        mid.Rect.Height.Should().BeInRange(151, 299);
        mid.Rect.Width.Should().Be(400);

        motion.Tick(At(1_000 + TileReturn.DurationMilliseconds), frames).Moves
            .Should().Equal(new TileMove(1, TileMoveKind.Glide, new TileRect(400, 0, 400, 150)));
    }

    [TestMethod]
    public void A_tile_that_gives_room_to_a_window_that_opens_sets_off_once_that_window_has_faded_in()
    {
        var motion = Settled();
        var halved = Tile with { Width = 200 };
        var opened = new Placement(2, 200, 0, 200, 300, Visible: true);
        var frames = Frames((1, OnTile), (2, new TileRect(200, 0, 200, 300)));

        var start = motion.Arrange([halved, opened], At(1_000), frames);
        start.Moves.Should().Equal(new TileMove(2, TileMoveKind.Land, new TileRect(200, 0, 200, 300)));
        start.Pace.Should().Be(TilePace.Fine);

        var held = 1_000 + MotionCurve.OpenFadeMilliseconds;
        motion.Tick(At(held), frames).Moves.Should().BeEmpty("the new window is not solid above it yet");

        var mid = motion.Tick(At(held + 40), frames).Moves.Should().ContainSingle().Subject;
        mid.Rect.Width.Should().BeInRange(201, 399);

        motion.Tick(At(held + TileReturn.DurationMilliseconds), frames).Moves
            .Should().Equal(new TileMove(1, TileMoveKind.Glide, new TileRect(0, 0, 200, 300)));
        motion.IsGliding(1).Should().BeFalse();
    }

    [TestMethod]
    public void A_tile_that_takes_the_room_of_a_window_that_left_takes_it_at_once()
    {
        var motion = new TileMotion();
        var half = Tile with { Width = 200 };
        var beside = new Placement(2, 200, 0, 200, 300, Visible: true);
        var frames = Frames((1, new TileRect(0, 0, 200, 300)), (2, new TileRect(200, 0, 200, 300)));
        motion.Arrange([half, beside], At(0), frames);
        motion.Arrange([half, beside], At(0), frames);

        var step = motion.Arrange([Tile], At(1_000), frames);

        step.Moves.Should().Equal(new TileMove(1, TileMoveKind.Land, OnTile));
        step.Gliding.Should().BeEmpty("the window that leaves fades out above it, or is gone and has left the wallpaper bare");
    }

    [TestMethod]
    public void A_held_button_does_not_stop_a_glide_when_the_tiles_change()
    {
        var motion = Settled();
        motion.Arrange([Tile], At(0, mouseDown: true), Frames((1, OnTile)));
        var halved = Tile with { Width = 200 };
        var opened = new Placement(2, 200, 0, 200, 300, Visible: true);
        var frames = Frames((1, OnTile), (2, Elsewhere));

        var start = motion.Arrange([halved, opened], At(1_000, mouseDown: true), frames);
        start.Moves.Should().Equal(new TileMove(2, TileMoveKind.Land, new TileRect(200, 0, 200, 300)));
        start.Gliding.Should().Equal(1);
        start.Pace.Should().Be(TilePace.Fine);

        var landed = motion.Arrange([halved, opened], At(1_000 + MotionCurve.OpenFadeMilliseconds + TileReturn.DurationMilliseconds, mouseDown: true), Frames((1, OnTile), (2, new TileRect(200, 0, 200, 300))));
        landed.Moves.Should().Contain(new TileMove(1, TileMoveKind.Glide, new TileRect(0, 0, 200, 300)));
    }

    [TestMethod]
    public void A_landing_drops_a_glide_and_puts_every_tile_down_at_once()
    {
        var motion = Settled();
        var moved = Tile with { X = 80 };
        var there = new TileRect(80, 0, 400, 300);
        motion.Arrange([moved], At(1_000), Frames((1, OnTile)));

        var step = motion.Land([moved], At(1_010), Frames((1, OnTile)));
        step.Moves.Should().Equal(new TileMove(1, TileMoveKind.Land, there));
        step.Gliding.Should().BeEmpty();
        step.Pace.Should().Be(TilePace.Parked);

        motion.Arrange([moved], At(1_020), Frames((1, there))).Moves.Should().BeEmpty();
    }

    [TestMethod]
    public void An_inactive_group_member_is_hidden_and_is_not_a_tile_change()
    {
        var motion = Settled();
        var member = new Placement(2, 0, 0, 400, 300, Visible: false);

        var step = motion.Arrange([Tile, member], At(0), Frames((1, OnTile)));
        // The hidden window is kept on the tile, so it comes back at that size.
        step.Moves.Should().Equal(new TileMove(2, TileMoveKind.Hide, OnTile));

        var held = motion.Arrange([Tile, member], At(0, mouseDown: true), _ => throw new InvalidOperationException("read a window"));
        held.Moves.Should().BeEmpty();
    }

    [TestMethod]
    public void With_nothing_gliding_or_held_a_tick_parks_the_clock_and_reads_no_window()
    {
        var motion = Settled();

        var step = motion.Tick(At(0), _ => throw new InvalidOperationException("read a window"));

        step.Kind.Should().Be(TileStepKind.Idle);
        step.Moves.Should().BeEmpty();
        step.Pace.Should().Be(TilePace.Parked);
    }

    [TestMethod]
    public void A_tick_carries_a_glide_on()
    {
        var motion = Settled();
        var moved = Tile with { X = 80, Y = 40 };
        var frames = Frames((1, OnTile));
        motion.Arrange([moved], At(1_000), frames);

        var mid = motion.Tick(At(1_080), frames);

        mid.Kind.Should().Be(TileStepKind.Placed);
        mid.Moves.Should().Equal(new TileMove(1, TileMoveKind.Glide, MidGlide));
        mid.Pace.Should().Be(TilePace.Fine);
    }

    [TestMethod]
    public void Super_held_keeps_the_clock_coasting()
    {
        var motion = Settled();

        var step = motion.Tick(At(0, super: true), NoRead);

        step.Kind.Should().Be(TileStepKind.Held);
        step.Moves.Should().BeEmpty();
        step.Pace.Should().Be(TilePace.Coast);
    }

    [TestMethod]
    public void A_resize_whose_pointer_rests_reads_no_window()
    {
        var motion = Settled();
        var held = At(0, super: true) with { RightDown = true, Pointer = new TilePoint(10, 10) };
        motion.Tick(held, NoRead);

        var step = motion.Tick(held, NoRead);

        step.Kind.Should().Be(TileStepKind.Held);
        step.Gesture.Should().BeNull();
        step.Pace.Should().Be(TilePace.Coast);
    }

    [TestMethod]
    public void A_tick_of_a_glide_reads_only_the_window_that_glides()
    {
        var other = new Placement(2, 400, 0, 400, 300, Visible: true);
        var moved = Tile with { Y = 300 };
        var motion = new TileMotion();
        motion.Arrange([Tile, other], At(0), Frames((1, Elsewhere), (2, Elsewhere)));
        motion.Arrange([Tile, other], At(0), Frames((1, OnTile), (2, new TileRect(400, 0, 400, 300))));
        motion.Arrange([moved, other], At(0), Frames((1, OnTile), (2, new TileRect(400, 0, 400, 300))));
        var read = new List<long>();

        var step = motion.Tick(At(40), id =>
        {
            read.Add(id);
            return id == 1 ? OnTile : throw new InvalidOperationException("read a resting tile");
        });

        read.Should().Equal(1L);
        step.Moves.Should().ContainSingle().Which.Kind.Should().Be(TileMoveKind.Glide);
        step.Pace.Should().Be(TilePace.Fine);
    }

    [TestMethod]
    public void An_arrange_puts_back_a_resting_tile_that_was_knocked_off()
    {
        var motion = Settled();

        var step = motion.Arrange([Tile], At(0), Frames((1, Elsewhere)));

        step.Moves.Should().Equal(new TileMove(1, TileMoveKind.Land, OnTile));
    }

    [TestMethod]
    public void A_window_that_left_the_tiles_lands_at_once_when_it_comes_back()
    {
        var motion = Settled();
        motion.Arrange([], At(0), Frames());
        var back = Tile with { X = 80 };

        var step = motion.Arrange([back], At(1_000), Frames((1, Elsewhere)));

        step.Moves.Should().Equal(new TileMove(1, TileMoveKind.Land, new TileRect(80, 0, 400, 300)));
        step.Gliding.Should().BeEmpty();
    }

    [TestMethod]
    public void A_window_that_landed_glides_when_its_tile_changes_though_no_arrange_saw_it_there()
    {
        // The shell does not arrange again while the tiles hold, so the landing is the only step before the next window opens.
        var motion = new TileMotion();
        motion.Arrange([Tile], At(0), Frames((1, Elsewhere)));
        var halved = Tile with { Width = 200 };
        var opened = new Placement(2, 200, 0, 200, 300, Visible: true);

        var step = motion.Arrange([halved, opened], At(1_000), Frames((1, OnTile), (2, Elsewhere)));

        step.Moves.Should().Equal(new TileMove(2, TileMoveKind.Land, new TileRect(200, 0, 200, 300)));
        step.Gliding.Should().Equal(1);
        step.Pace.Should().Be(TilePace.Fine);
    }

    [TestMethod]
    public void A_window_that_missed_its_landing_jumps_when_its_tile_changes()
    {
        var motion = new TileMotion();
        motion.Arrange([Tile], At(0), Frames((1, Elsewhere)));
        var halved = Tile with { Width = 200 };

        var step = motion.Arrange([halved], At(1_000), Frames((1, Elsewhere)));

        step.Moves.Should().Equal(new TileMove(1, TileMoveKind.Land, new TileRect(0, 0, 200, 300)));
        step.Gliding.Should().BeEmpty();
    }

    [TestMethod]
    public void A_landing_puts_down_at_once_a_window_no_arrange_saw_on_its_tile()
    {
        var motion = new TileMotion();
        motion.Arrange([Tile], At(0), Frames((1, Elsewhere)));
        var moved = Tile with { X = 80 };

        var step = motion.Land([moved], At(1_000), Frames((1, OnTile)));

        step.Moves.Should().Equal(new TileMove(1, TileMoveKind.Land, new TileRect(80, 0, 400, 300)));
        step.Gliding.Should().BeEmpty();
    }

    [TestMethod]
    public void A_held_button_keeps_a_window_that_just_landed_still_and_reads_no_window()
    {
        // A title-bar drag makes the shell arrange with the button down. The window that just landed is not a change of tiles.
        var motion = new TileMotion();
        motion.Arrange([Tile], At(0), Frames((1, Elsewhere)));

        var step = motion.Arrange([Tile], At(16, mouseDown: true), NoRead);

        step.Kind.Should().Be(TileStepKind.Held);
        step.Moves.Should().BeEmpty();
    }

    [TestMethod]
    public void A_carried_window_follows_the_pointer()
    {
        var motion = Settled();

        var start = motion.Tick(At(0, super: true) with { Presses = [Press(1, 100, 100)], Pointer = new TilePoint(100, 100) }, NoRead);
        motion.Carried.Should().Be(1);
        start.Moves.Should().Equal(new TileMove(1, TileMoveKind.Carry, OnTile));
        start.Pace.Should().Be(TilePace.Fine);

        var moved = motion.Tick(At(8, mouseDown: true) with { Pointer = new TilePoint(130, 90) }, NoRead);
        moved.Kind.Should().Be(TileStepKind.Held);
        moved.Moves.Should().Equal(new TileMove(1, TileMoveKind.Carry, new TileRect(30, -10, 400, 300)));
        moved.Pace.Should().Be(TilePace.Fine);
    }

    [TestMethod]
    public void Letting_go_of_a_carry_past_the_slop_drops_the_window_at_the_pointer()
    {
        var motion = Settled();
        motion.Tick(At(0, super: true) with { Presses = [Press(1, 100, 100)], Pointer = new TilePoint(100, 100) }, NoRead);

        var step = motion.Tick(
            At(8) with { Presses = [new TilePress(Down: false, 300, 120)], Pointer = new TilePoint(300, 120) },
            Frames((1, new TileRect(200, 20, 400, 300))));

        step.Gesture.Should().Be(new TileDrag(1, Drop: true, 300, 120, 0, 0, new TileRect(200, 20, 400, 300)));
        motion.Carried.Should().BeNull();
        step.Gliding.Should().Equal(1);
    }

    [TestMethod]
    public void Letting_go_of_a_carry_that_barely_travelled_puts_the_window_back()
    {
        var motion = Settled();
        motion.Tick(At(0, super: true) with { Presses = [Press(1, 100, 100)], Pointer = new TilePoint(100, 100) }, NoRead);

        var step = motion.Tick(
            At(8) with { Presses = [new TilePress(Down: false, 110, 105)], Pointer = new TilePoint(110, 105) },
            Frames((1, new TileRect(10, 5, 400, 300))));

        step.Gesture.Should().BeNull();
        motion.Carried.Should().BeNull();
        step.Moves.Should().Equal(new TileMove(1, TileMoveKind.Land, OnTile));
    }

    [TestMethod]
    public void A_sticky_window_let_go_past_the_slop_rests_there_and_does_not_start_home()
    {
        var sticky = Tile with { Raised = true };
        var motion = new TileMotion();
        motion.Arrange([sticky], At(0), Frames((1, Elsewhere)));
        motion.Arrange([sticky], At(0), Frames((1, OnTile)));
        motion.Tick(At(0, super: true) with { Presses = [Press(1, 100, 100) with { Floated = true }], Pointer = new TilePoint(100, 100) }, NoRead);
        var carried = new TileRect(200, 20, 400, 300);

        var let = motion.Tick(
            At(8) with { Presses = [new TilePress(Down: false, 300, 120)], Pointer = new TilePoint(300, 120) },
            Frames((1, carried)));

        let.Gesture.Should().Be(new TileDrag(1, Drop: true, 300, 120, 0, 0, carried));
        let.Moves.Should().BeEmpty("the shell keeps the rectangle it was let go on");
        let.Gliding.Should().BeEmpty();

        // An arrange that still has the old rectangle, ahead of the shell's answer, leaves it there too.
        motion.Arrange([sticky], At(12), Frames((1, carried))).Moves.Should().BeEmpty();

        var kept = motion.Arrange([sticky with { X = 200, Y = 20 }], At(16), Frames((1, carried)));

        kept.Gliding.Should().BeEmpty();
        kept.Moves.Should().OnlyContain(move => move.Kind == TileMoveKind.Land && move.Rect == carried);
    }

    [TestMethod]
    public void A_carried_window_is_not_glided_when_its_tile_moves()
    {
        var motion = Settled();
        motion.Tick(At(0, super: true) with { Presses = [Press(1, 100, 100)], Pointer = new TilePoint(100, 100) }, NoRead);
        var moved = Tile with { X = 80 };

        var step = motion.Arrange([moved], At(8), NoRead);

        step.Kind.Should().Be(TileStepKind.Placed);
        step.Moves.Should().BeEmpty();
        step.Gliding.Should().BeEmpty();
        step.Pace.Should().Be(TilePace.Fine);
    }

    [TestMethod]
    public void Super_and_the_right_button_resize_the_tile_under_the_pointer()
    {
        var motion = Settled();
        var held = At(0, super: true) with { RightDown = true };
        var frames = Frames((1, OnTile));

        var start = motion.Tick(held with { Pointer = new TilePoint(100, 100) }, frames);
        start.Gesture.Should().BeNull();
        start.Pace.Should().Be(TilePace.Coast);

        var pulled = motion.Tick(held with { Now = 16, Pointer = new TilePoint(130, 90) }, frames);
        pulled.Gesture.Should().Be(new TileDrag(1, Drop: false, 130, 90, 30, -10));
        pulled.Pace.Should().Be(TilePace.Coast);

        motion.Tick(held with { Now = 32, Pointer = new TilePoint(130, 90) }, frames).Gesture.Should().BeNull();
    }

    [TestMethod]
    public void A_window_the_pointer_is_resizing_follows_its_tile_as_it_changes()
    {
        var motion = Settled();
        var frames = Frames((1, OnTile));
        motion.Tick(At(0, super: true) with { RightDown = true, Pointer = new TilePoint(100, 100) }, frames);
        var wider = Tile with { Width = 480 };

        // The hand steers this one, so the window is put on its tile at once and nothing eases behind the pointer.
        var start = motion.Arrange([wider], At(1_000, super: true), frames);
        start.Moves.Should().Equal(new TileMove(1, TileMoveKind.Land, new TileRect(0, 0, 480, 300)));
        start.Gliding.Should().BeEmpty();
        start.Pace.Should().Be(TilePace.Coast);
    }

    [TestMethod]
    public void A_resize_the_pointer_steers_lands_the_neighbor_it_changes_too()
    {
        var beside = new Placement(2, 400, 0, 400, 300, Visible: true);
        var motion = new TileMotion();
        var frames = Frames((1, OnTile), (2, new TileRect(400, 0, 400, 300)));
        motion.Arrange([Tile, beside], At(0), frames);
        motion.Tick(At(0, super: true) with { RightDown = true, Pointer = new TilePoint(100, 100) }, frames);

        var step = motion.Arrange([Tile with { Width = 480 }, beside with { X = 480, Width = 320 }], At(1_000, super: true), frames);

        step.Moves.Should().Equal(
            new TileMove(1, TileMoveKind.Land, new TileRect(0, 0, 480, 300)),
            new TileMove(2, TileMoveKind.Land, new TileRect(480, 0, 320, 300)));
    }

    [TestMethod]
    public void A_tile_that_changes_by_one_pixel_is_still_sent()
    {
        var motion = Settled();

        var step = motion.Arrange([Tile with { Width = 401 }], At(1_000), Frames((1, OnTile)));

        step.Moves.Should().Equal(new TileMove(1, TileMoveKind.Land, new TileRect(0, 0, 401, 300)));
    }

    [TestMethod]
    public void A_window_that_rounds_its_own_size_is_not_sent_its_tile_again()
    {
        var motion = Settled();

        var step = motion.Arrange([Tile], At(1_000), Frames((1, new TileRect(0, 0, 398, 299))));

        step.Moves.Should().BeEmpty();
    }

    [TestMethod]
    public void Letting_go_of_Super_or_the_right_button_ends_the_resize()
    {
        var motion = Settled();
        motion.Tick(At(0, super: true) with { RightDown = true, Pointer = new TilePoint(100, 100) }, Frames((1, OnTile)));

        var step = motion.Tick(At(16) with { Pointer = new TilePoint(150, 100) }, NoRead);

        step.Gesture.Should().BeNull();
        step.Kind.Should().Be(TileStepKind.Idle);
    }

    /// <summary>Window 1 on <see cref="Tile"/>, as after it opened and landed.</summary>
    [TestMethod]
    public void A_group_member_stays_on_the_glass_until_the_window_taking_its_tile_has_glided_there_and_drawn()
    {
        var beside = new TileRect(400, 0, 400, 300);
        var motion = Settled();
        motion.Arrange([Tile, new Placement(2, 400, 0, 400, 300, Visible: true)], At(0), Frames((1, OnTile), (2, Elsewhere)));
        motion.Arrange([Tile, new Placement(2, 400, 0, 400, 300, Visible: true)], At(0), Frames((1, OnTile), (2, beside)));

        // Window 2 joins window 1's tile and is the member shown.
        Placement[] joined = [Tile with { Visible = false }, new Placement(2, 0, 0, 400, 300, Visible: true)];
        var gliding = Frames((1, OnTile), (2, beside));

        var start = motion.Arrange(joined, At(1_000), gliding);
        start.Moves.Should().BeEmpty();
        start.Gliding.Should().Equal(2);

        var landed = motion.Arrange(joined, At(1_000 + TileReturn.DurationMilliseconds), gliding);
        landed.Moves.Should().Equal(new TileMove(2, TileMoveKind.Glide, OnTile));
        landed.Pace.Should().Be(TilePace.Coast, "the glide has landed, and the cover wait stays on the 16 ms clock");

        var there = Frames((1, OnTile), (2, OnTile));
        motion.Tick(At(1_010 + TileReturn.DurationMilliseconds), there).Moves.Should().BeEmpty();
        motion.Tick(At(1_010 + TileReturn.DurationMilliseconds + TileMotion.CoverMilliseconds - 1), there).Moves.Should().BeEmpty();

        var hidden = motion.Tick(At(1_010 + TileReturn.DurationMilliseconds + TileMotion.CoverMilliseconds), there);
        hidden.Moves.Should().Equal(new TileMove(1, TileMoveKind.Hide, OnTile));
        hidden.Pace.Should().Be(TilePace.Parked);
    }

    [TestMethod]
    public void A_window_that_lands_on_a_group_tile_at_once_has_time_to_draw_before_the_member_behind_it_hides()
    {
        var motion = Settled();
        Placement[] joined = [Tile with { Visible = false }, new Placement(2, 0, 0, 400, 300, Visible: true)];

        var land = motion.Arrange(joined, At(0), Frames((1, OnTile), (2, Elsewhere)));
        land.Moves.Should().Equal(new TileMove(2, TileMoveKind.Land, OnTile));
        land.Pace.Should().Be(TilePace.Coast, "the cover wait is not a glide, so the clock stays at 16 ms");

        var there = Frames((1, OnTile), (2, OnTile));
        motion.Tick(At(8), there).Moves.Should().NotContain(move => move.Kind == TileMoveKind.Hide);

        motion.Tick(At(8 + TileMotion.CoverMilliseconds), there).Moves.Should().Equal(new TileMove(1, TileMoveKind.Hide, OnTile));
        motion.Tick(At(200), there).Kind.Should().Be(TileStepKind.Idle);
    }

    [TestMethod]
    public void A_member_whose_cover_never_reaches_the_tile_hides_anyway()
    {
        var motion = Settled();
        Placement[] joined = [Tile with { Visible = false }, new Placement(2, 0, 0, 400, 300, Visible: true)];
        var stuck = Frames((1, OnTile), (2, Elsewhere));
        motion.Arrange(joined, At(0), stuck);

        motion.Tick(At(TileMotion.LeaveMilliseconds - 1), stuck).Moves.Should().NotContain(move => move.Kind == TileMoveKind.Hide);
        motion.Tick(At(TileMotion.LeaveMilliseconds), stuck).Moves.Should().Contain(new TileMove(1, TileMoveKind.Hide, OnTile));
    }

    [TestMethod]
    public void A_look_stands_for_five_seconds_when_nothing_moved()
    {
        var motion = new TileMotion();
        motion.Quiet(generation: 3, now: 1_000, moved: false, watched: true).Should().BeFalse();

        motion.Noted(3, 1_000);
        motion.Quiet(3, 1_000 + TileMotion.QuietMilliseconds - 1, moved: false, watched: true).Should().BeTrue();
        motion.Quiet(3, 1_000 + TileMotion.QuietMilliseconds, moved: false, watched: true).Should().BeFalse();
        motion.Quiet(3, 2_000, moved: true, watched: true).Should().BeFalse();
        motion.Quiet(4, 2_000, moved: false, watched: true).Should().BeFalse();
        motion.Quiet(3, 2_000, moved: false, watched: false).Should().BeFalse();
    }

    [TestMethod]
    public void A_window_back_on_its_tile_is_asked_the_next_time_it_leaves()
    {
        var motion = new TileMotion();
        var home = new Placement(1, 0, 0, 100, 80, Visible: true);
        var away = new TileRect(40, 40, 100, 80);
        var on = new TileRect(0, 0, 100, 80);

        motion.OffTile(home, away).Should().BeTrue();
        motion.Arranged();
        motion.OffTile(home, away).Should().BeFalse();
        motion.OffTile(home, on).Should().BeFalse();
        motion.OffTile(home, away).Should().BeTrue();
    }

    [TestMethod]
    public void A_frame_that_changed_on_the_same_tile_is_a_miss()
    {
        var motion = new TileMotion();
        var home = new Placement(1, 0, 0, 400, 300, Visible: true);
        var framed = new TileRect(0, 0, 400, 280);

        motion.OffTile(home, framed).Should().BeTrue();
        motion.Arranged();
        motion.OffTile(home, framed).Should().BeFalse();
        motion.OffTile(home, new TileRect(0, 0, 400, 268)).Should().BeTrue();
    }

    private static TileMotion Settled()
    {
        var motion = new TileMotion();
        motion.Arrange([Tile], At(0), Frames((1, Elsewhere)));
        motion.Arrange([Tile], At(0), Frames((1, OnTile)));
        return motion;
    }

    private static readonly Func<long, TileRect?> NoRead = _ => throw new InvalidOperationException("read a window");

    private static TileFacts At(long now, bool mouseDown = false, bool super = false) => new(now, super, mouseDown);

    /// <summary>Super and the left button went down on a window that sits on <see cref="OnTile"/>.</summary>
    private static TilePress Press(long id, int x, int y) => new(Down: true, x, y, id, OnTile);

    private static Func<long, TileRect?> Frames(params (long Id, TileRect Frame)[] windows)
    {
        var frames = windows.ToDictionary(window => window.Id, window => window.Frame);
        return id => frames.TryGetValue(id, out var frame) ? frame : null;
    }
}
