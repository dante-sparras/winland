using FluentAssertions;
using Winland.Core.Input;

namespace Winland.Tests;

[TestClass]
public sealed class HookRepairTests
{
    private readonly HookRepair _repair = new();

    [TestMethod]
    public void A_hook_that_keeps_being_called_is_never_installed_again()
    {
        for (var i = 0; i < 100; i++)
        {
            _repair.HookCalled(lagMs: 5);
            _repair.KeyReachedWindows(() => false).Should().BeFalse();
        }

        _repair.Due(now: 1000).Should().BeNull();
    }

    [TestMethod]
    public void Keys_that_reach_windows_without_a_hook_call_mark_the_hook_gone()
    {
        _repair.KeyReachedWindows(() => false).Should().BeFalse();
        _repair.KeyReachedWindows(() => false).Should().BeFalse();
        _repair.KeyReachedWindows(() => false).Should().BeTrue();

        _repair.Due(now: 1000).Should().Be(HookRepairReason.Gone);
    }

    [TestMethod]
    public void A_hook_call_between_keys_starts_the_count_over()
    {
        _repair.KeyReachedWindows(() => false);
        _repair.KeyReachedWindows(() => false);
        _repair.HookCalled();
        _repair.KeyReachedWindows(() => false);
        _repair.KeyReachedWindows(() => false);

        _repair.Due(now: 1000).Should().BeNull();
    }

    [TestMethod]
    public void Keys_the_hook_cannot_see_do_not_mark_it_gone()
    {
        var asked = 0;
        for (var i = 0; i < 9; i++)
        {
            _repair.KeyReachedWindows(() =>
            {
                asked++;
                return true;
            });
        }

        asked.Should().Be(3);
        _repair.Due(now: 1000).Should().BeNull();
    }

    [TestMethod]
    public void A_late_hook_call_asks_for_an_install()
    {
        _repair.HookCalled(lagMs: HookRepair.LateLimitMs - 1).Should().BeFalse();
        _repair.Due(now: 1000).Should().BeNull();

        _repair.HookCalled(lagMs: HookRepair.LateLimitMs).Should().BeTrue();
        _repair.Due(now: 1000).Should().Be(HookRepairReason.Late);
    }

    [TestMethod]
    public void A_reason_inside_the_gap_waits_for_it()
    {
        _repair.SuperWentPast();
        _repair.Installed(HookRepairReason.Behind, now: 1000);

        _repair.Unlocked();

        _repair.Due(now: 1000 + HookRepair.MinGapMs - 1).Should().BeNull();
        _repair.Pending.Should().BeTrue();
        _repair.Due(now: 1000 + HookRepair.MinGapMs).Should().Be(HookRepairReason.Unlocked);
    }

    [TestMethod]
    public void Installed_clears_the_reason()
    {
        _repair.Unlocked();
        _repair.Installed(HookRepairReason.Unlocked, now: 1000);

        _repair.Pending.Should().BeFalse();
        _repair.Due(now: 100_000).Should().BeNull();
    }

    [TestMethod]
    public void A_fight_with_another_hook_backs_off()
    {
        var now = 1000L;
        var gaps = new List<long>();
        for (var i = 0; i < 8; i++)
        {
            _repair.SuperWentPast();
            var wait = 0L;
            while (_repair.Due(now + wait) is null)
            {
                wait += 100;
            }

            gaps.Add(wait);
            now += wait;
            _repair.Installed(HookRepairReason.Behind, now);
        }

        gaps[0].Should().Be(0);
        gaps.Should().BeInAscendingOrder();
        gaps[^1].Should().Be(HookRepair.MaxGapMs);
    }

    [TestMethod]
    public void A_calm_spell_ends_the_backoff()
    {
        _repair.Installed(HookRepairReason.Behind, now: 1000);
        _repair.Installed(HookRepairReason.Behind, now: 2000);
        _repair.Installed(HookRepairReason.Behind, now: 4000);

        // The third install left a 4000 ms gap. Quiet past that gap ends the fight.
        var calm = 4000 + 4000 + HookRepair.CalmMs;
        _repair.Installed(HookRepairReason.Behind, now: calm);
        _repair.SuperWentPast();

        _repair.Due(now: calm + HookRepair.MinGapMs).Should().Be(HookRepairReason.Behind);
    }

    [TestMethod]
    public void A_gone_hook_does_not_wait_out_a_backoff()
    {
        _repair.Installed(HookRepairReason.Behind, now: 1000);
        _repair.Installed(HookRepairReason.Behind, now: 2000);
        _repair.Installed(HookRepairReason.Gone, now: 3000);

        _repair.KeyReachedWindows(() => false);
        _repair.KeyReachedWindows(() => false);
        _repair.KeyReachedWindows(() => false);

        _repair.Due(now: 3000 + HookRepair.MinGapMs).Should().Be(HookRepairReason.Gone);
    }

    [TestMethod]
    public void A_hook_that_goes_missing_during_a_fight_does_not_wait_out_the_backoff()
    {
        _repair.Installed(HookRepairReason.Behind, now: 1000);
        _repair.Installed(HookRepairReason.Behind, now: 2000);
        _repair.Installed(HookRepairReason.Behind, now: 4000);

        // The third install left a 4000 ms gap. No hook is in the chain now, so chords are dead until the install.
        _repair.KeyReachedWindows(() => false);
        _repair.KeyReachedWindows(() => false);
        _repair.KeyReachedWindows(() => false);

        _repair.Due(now: 4000 + HookRepair.MinGapMs - 1).Should().BeNull();
        _repair.Due(now: 4000 + HookRepair.MinGapMs).Should().Be(HookRepairReason.Gone);
    }

    [TestMethod]
    public void A_gone_hook_whose_install_is_refused_still_backs_off()
    {
        _repair.KeyReachedWindows(() => false);
        _repair.KeyReachedWindows(() => false);
        _repair.KeyReachedWindows(() => false);
        _repair.Failed(now: 1000);
        _repair.Failed(now: 2000);

        // The hook is still gone, so the keys typed meanwhile mark it gone again.
        _repair.KeyReachedWindows(() => false);
        _repair.KeyReachedWindows(() => false);
        _repair.KeyReachedWindows(() => false);

        _repair.Due(now: 2000 + HookRepair.MinGapMs).Should().BeNull();
        _repair.Due(now: 2000 + (2 * HookRepair.MinGapMs)).Should().Be(HookRepairReason.Gone);
    }

    [TestMethod]
    public void Gone_outranks_a_later_reason()
    {
        _repair.KeyReachedWindows(() => false);
        _repair.KeyReachedWindows(() => false);
        _repair.KeyReachedWindows(() => false);
        _repair.Unlocked();

        _repair.Due(now: 1000).Should().Be(HookRepairReason.Gone);
    }

    [TestMethod]
    public void A_failed_install_keeps_the_reason_and_waits_before_the_next_try()
    {
        _repair.Unlocked();
        _repair.Due(now: 1000).Should().Be(HookRepairReason.Unlocked);

        _repair.Failed(now: 1000);
        _repair.Due(now: 1000 + HookRepair.MinGapMs - 1).Should().BeNull();
        _repair.Due(now: 1000 + HookRepair.MinGapMs).Should().Be(HookRepairReason.Unlocked);

        _repair.Failed(now: 2000);
        _repair.Due(now: 2000 + HookRepair.MinGapMs).Should().BeNull();
        _repair.Due(now: 2000 + (2 * HookRepair.MinGapMs)).Should().Be(HookRepairReason.Unlocked);
    }
}
