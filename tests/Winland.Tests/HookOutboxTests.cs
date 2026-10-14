using FluentAssertions;
using Winland.Core.Input;

namespace Winland.Tests;

[TestClass]
public sealed class HookOutboxTests
{
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(10);

    [TestMethod]
    public void Lines_are_taken_in_the_order_they_were_added()
    {
        var outbox = new HookOutbox();
        outbox.Add(new HookWire.EscapeMode(true));
        outbox.Add(new HookWire.EscapeMode(true));
        outbox.Add(new HookWire.Deliver());
        outbox.Add(new HookWire.EscapeMode(false));

        Taken(outbox, 4).Should().Equal(
            new HookWire.EscapeMode(true),
            new HookWire.EscapeMode(true),
            new HookWire.Deliver(),
            new HookWire.EscapeMode(false));
    }

    [TestMethod]
    public void A_pointer_notice_that_waits_is_dropped_for_a_newer_one()
    {
        var outbox = new HookOutbox();
        outbox.Add(new HookWire.Pointer());
        outbox.Add(new HookWire.EscapeMode(true));
        outbox.Add(new HookWire.Pointer());
        outbox.Add(new HookWire.Pointer());
        outbox.Add(new HookWire.Forget());

        Taken(outbox, 3).Should().Equal(new HookWire.EscapeMode(true), new HookWire.Pointer(), new HookWire.Forget());
    }

    [TestMethod]
    public void A_pointer_notice_after_the_last_one_was_taken_waits_too()
    {
        var outbox = new HookOutbox();
        outbox.Add(new HookWire.Pointer());
        Taken(outbox, 1);
        outbox.Add(new HookWire.Pointer());
        outbox.Add(new HookWire.Forget());

        Taken(outbox, 2).Should().Equal(new HookWire.Pointer(), new HookWire.Forget());
    }

    [TestMethod]
    public void Only_pointer_notices_are_dropped_for_a_newer_one()
    {
        var outbox = new HookOutbox();
        outbox.Add(new HookWire.Deliver());
        outbox.Add(new HookWire.Deliver());
        outbox.Add(new HookWire.EscapeMode(true));
        outbox.Add(new HookWire.EscapeMode(true));
        outbox.Add(new HookWire.Quit());

        Taken(outbox, 5).Should().Equal(
            new HookWire.Deliver(),
            new HookWire.Deliver(),
            new HookWire.EscapeMode(true),
            new HookWire.EscapeMode(true),
            new HookWire.Quit());
    }

    [TestMethod]
    public void A_closed_outbox_refuses_a_line()
    {
        var outbox = new HookOutbox();
        outbox.Close();

        outbox.Add(new HookWire.EscapeMode(true)).Should().BeFalse();
    }

    [TestMethod]
    public void Closing_drops_the_lines_that_wait()
    {
        var outbox = new HookOutbox();
        outbox.Add(new HookWire.EscapeMode(true)).Should().BeTrue();
        outbox.Add(new HookWire.Pointer()).Should().BeTrue();

        outbox.Close();

        outbox.Take(out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_take_waits_for_the_next_line()
    {
        var outbox = new HookOutbox();
        var take = Start(() => outbox.Take(out var line) ? line : null);

        outbox.Add(new HookWire.Deliver());

        take.Wait(Soon).Should().BeTrue();
        take.Result.Should().Be(new HookWire.Deliver());
    }

    [TestMethod]
    public void Closing_ends_a_take_that_waits()
    {
        var outbox = new HookOutbox();
        var take = Start(() => outbox.Take(out _));

        outbox.Close();

        take.Wait(Soon).Should().BeTrue();
        take.Result.Should().BeFalse();
    }

    /// <summary>The next <paramref name="count"/> lines. A line that never comes fails the test and does not hang it.</summary>
    private static List<HookWire> Taken(HookOutbox outbox, int count)
    {
        var take = Start(() =>
        {
            var lines = new List<HookWire>();
            while (lines.Count < count && outbox.Take(out var line))
            {
                lines.Add(line);
            }

            return lines;
        });
        take.Wait(Soon).Should().BeTrue();
        return take.Result;
    }

    /// <summary>Runs <paramref name="work"/> on a thread of its own, as the writer is, so a busy thread pool cannot hold it back.</summary>
    private static Task<T> Start<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>();
        new Thread(() => done.SetResult(work())) { IsBackground = true }.Start();
        return done.Task;
    }
}
