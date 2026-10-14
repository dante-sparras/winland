using FluentAssertions;
using Winland.Core.Diagnostics;

namespace Winoma.Tests;

[TestClass]
public sealed class FrameSampleTests
{
    [TestMethod]
    public void Ordinary_medians_are_one_line_and_the_sample_that_closes_the_window_is_left_out()
    {
        var sample = new FrameSample();
        sample.NoteArrange(0, 1, 1).Should().BeNull();

        sample.Start(1_000);
        sample.NoteArrange(1_000, 1, 4);
        sample.NoteArrange(1_200, 3, 4);
        sample.NoteIdle(1_000, 1, 4);
        sample.NoteIdle(1_200, 2, 4);
        sample.NoteIdle(1_400, 9, 4);

        sample.NoteArrange(6_000, 99, 4).Should().Be("frame sample ordinary arrange=2ms samples=2 idle=2ms samples=3");
        sample.NoteIdle(6_001, 1, 4).Should().BeNull();
    }

    [TestMethod]
    public void A_heavier_snapshot_is_reported_beside_the_ordinary_one()
    {
        var sample = new FrameSample();
        sample.Start(0);
        sample.NoteArrange(0, 2, 4);
        sample.NoteIdle(0, 3, 4);
        sample.NoteArrange(100, 8, 5);
        sample.NoteIdle(100, 9, 5);

        sample.NoteIdle(5_000, 0, 4).Should().Be(
            "frame sample ordinary arrange=2ms samples=1 idle=3ms samples=1 heavy arrange=8ms samples=1 idle=9ms samples=1");
    }

    [TestMethod]
    public void An_even_count_uses_the_mean_of_the_two_middle_samples()
    {
        var sample = new FrameSample();
        sample.Start(0);
        sample.NoteArrange(0, 1, 1);
        sample.NoteArrange(1, 2, 1);

        sample.NoteArrange(5_000, 0, 1).Should().Be("frame sample ordinary arrange=1.5ms samples=2");
    }

    [TestMethod]
    public void Pace_reports_median_95th_and_slowest_of_each_kind_once_a_stretch_ends()
    {
        var sample = new FrameSample();
        sample.Start(0);
        for (var i = 1; i <= 20; i++)
        {
            sample.NoteIdle(10_000 + i, i, 1);
        }

        sample.NoteArrange(20_000, 4, 1);
        sample.NoteChord(20_001, 7);
        sample.NoteChord(20_002, 3);

        sample.NoteIdle(FrameSample.PaceMilliseconds, 0.5, 1).Should().Be(
            "pace 600s turn p50=10 p95=19 max=20 n=20 arrange p50=4 p95=4 max=4 n=1 chord p50=3 p95=7 max=7 n=2");
    }

    [TestMethod]
    public void The_note_that_ends_a_stretch_opens_the_next_and_an_empty_kind_is_left_out()
    {
        var sample = new FrameSample();
        sample.Start(0);
        sample.NoteChord(10_000, 2);

        sample.NoteChord(FrameSample.PaceMilliseconds + 5, 9).Should().Be("pace 600s chord p50=2 p95=2 max=2 n=1");
        sample.NoteChord(FrameSample.PaceMilliseconds + 6, 1).Should().BeNull();
        sample.NoteArrange((2 * FrameSample.PaceMilliseconds) + 5, 1, 1).Should().Be("pace 600s chord p50=1 p95=9 max=9 n=2");
    }

    [TestMethod]
    public void Nothing_is_paced_before_start_and_the_startup_summary_still_comes_first()
    {
        var sample = new FrameSample();
        sample.NoteChord(FrameSample.PaceMilliseconds * 2, 5).Should().BeNull();

        sample.Start(0);
        sample.NoteArrange(0, 2, 1);
        sample.NoteArrange(5_000, 0, 1).Should().Be("frame sample ordinary arrange=2ms samples=1");
    }

    [TestMethod]
    public void A_startup_summary_and_a_pace_summary_due_on_the_same_note_are_joined()
    {
        var sample = new FrameSample();
        sample.Start(0);
        sample.NoteIdle(0, 2, 1);

        sample.NoteIdle(FrameSample.PaceMilliseconds, 1, 1).Should().Be(
            "frame sample ordinary idle=2ms samples=1 | pace 600s turn p50=2 p95=2 max=2 n=1");
    }
}
