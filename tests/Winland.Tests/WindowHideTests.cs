using FluentAssertions;
using Winland.Core;

namespace Winland.Tests;

[TestClass]
public sealed class WindowHideTests
{
    [TestMethod]
    public void A_window_is_hidden_with_the_cloak()
    {
        var host = new Host();
        var hide = new WindowHide(host);

        hide.Hide(7).Should().Be(HideWay.Cloak);

        host.Calls.Should().Equal("cloak 7");
        hide.Way(7).Should().Be(HideWay.Cloak);
    }

    [TestMethod]
    public void A_window_the_cloak_is_not_used_on_is_hidden_with_SW_HIDE_whatever_the_reason()
    {
        // Every reason there is, so one a Host test adds later is held to the same rule.
        foreach (var refusal in Enum.GetValues<CloakRefusal>().Where(reason => reason != CloakRefusal.None))
        {
            var host = new Host { Refusal = refusal };
            var hide = new WindowHide(host);

            hide.Hide(7).Should().Be(HideWay.Hide, "the cloak answered {0}", refusal);

            host.Calls.Should().Equal("cloak 7", "hide 7");
            hide.Way(7).Should().Be(HideWay.Hide);
        }
    }

    [TestMethod]
    public void A_window_neither_way_takes_off_the_screen_is_not_on_the_ledger()
    {
        // Windows refuses SW_HIDE for an administrator window from a desktop that is not one. Explorer cloaks such a window,
        // so it comes here only when the cloak was not used on it either.
        var host = new Host { Refusal = CloakRefusal.Refused, Stays = true };
        var hide = new WindowHide(host);

        hide.Hide(7).Should().BeNull();

        host.Calls.Should().Equal("cloak 7", "hide 7");
        hide.Way(7).Should().BeNull("a window on the glass is never held as hidden");
    }

    [TestMethod]
    public void The_way_is_on_the_ledger_before_the_Host_is_asked_so_the_record_is_never_short_of_the_glass()
    {
        var host = new Host { Refusal = CloakRefusal.Refused };
        var hide = new WindowHide(host);
        host.Ledger = hide;

        hide.Hide(7);

        host.WaysSeen.Should().Equal(HideWay.Cloak, HideWay.Hide);
    }

    [TestMethod]
    public void A_cloaked_window_is_shown_by_uncloaking_it()
    {
        var host = new Host();
        var hide = new WindowHide(host);
        hide.Hide(7);
        host.Calls.Clear();

        hide.Show(7).Should().BeTrue();

        host.Calls.Should().Equal("uncloak 7");
        hide.Way(7).Should().BeNull();
    }

    [TestMethod]
    public void A_window_hidden_with_SW_HIDE_is_shown_without_asking_Explorer()
    {
        var host = new Host { Refusal = CloakRefusal.NoView };
        var hide = new WindowHide(host);
        hide.Hide(7);
        host.Calls.Clear();

        hide.Show(7).Should().BeTrue();

        host.Calls.Should().Equal("unhide 7");
        hide.Way(7).Should().BeNull();
    }

    [TestMethod]
    public void A_window_this_did_not_hide_is_left_alone()
    {
        var host = new Host();
        var hide = new WindowHide(host);

        hide.Show(7).Should().BeTrue();

        host.Calls.Should().BeEmpty();
    }

    [TestMethod]
    public void A_window_Explorer_does_not_uncloak_stays_on_the_ledger_and_is_asked_again()
    {
        var host = new Host();
        var hide = new WindowHide(host);
        hide.Hide(7);
        host.Refusal = CloakRefusal.Unreachable;

        hide.Show(7).Should().BeFalse();

        hide.Way(7).Should().Be(HideWay.Cloak);

        host.Refusal = CloakRefusal.None;
        hide.Show(7).Should().BeTrue();
        hide.Way(7).Should().BeNull();
    }

    [TestMethod]
    public void A_window_that_closed_is_forgotten()
    {
        var host = new Host();
        var hide = new WindowHide(host);
        hide.Hide(7);
        host.Calls.Clear();

        hide.Forget(7);

        hide.Way(7).Should().BeNull();
        host.Calls.Should().BeEmpty();
    }

    [TestMethod]
    public void A_window_another_desktop_left_hidden_is_taken_over_as_it_is_and_shown_the_way_it_was_hidden()
    {
        var host = new Host();
        var hide = new WindowHide(host);

        hide.Adopt(7, HideWay.Cloak);
        hide.Adopt(8, HideWay.Hide);

        // Nothing is asked of the Host: the windows are already off the screen.
        host.Calls.Should().BeEmpty();
        hide.Way(7).Should().Be(HideWay.Cloak);
        hide.Way(8).Should().Be(HideWay.Hide);

        hide.Show(7).Should().BeTrue();
        hide.Show(8).Should().BeTrue();

        host.Calls.Should().Equal("uncloak 7", "unhide 8");
    }

    [TestMethod]
    public void A_cloaked_window_the_ledger_does_not_know_is_uncloaked_when_it_is_shown()
    {
        // A desktop before this one cloaked it, and no record named it.
        var host = new Host();
        host.ShellCloaked.Add(7);
        var hide = new WindowHide(host);

        hide.Cloaked(7).Should().BeTrue();
        hide.Show(7).Should().BeTrue();

        host.Calls.Should().Equal("uncloak 7");
    }

    [TestMethod]
    public void A_cloaked_window_the_ledger_does_not_know_that_Explorer_does_not_uncloak_is_still_hidden()
    {
        var host = new Host { Refusal = CloakRefusal.Unreachable };
        host.ShellCloaked.Add(7);
        var hide = new WindowHide(host);

        hide.Show(7).Should().BeFalse();
    }

    [TestMethod]
    public void A_window_Windows_keeps_on_another_desktop_is_not_Winoma_s_to_uncloak()
    {
        // It reads the same cloak as a window Winoma hid (ADR 0003).
        var host = new Host();
        host.ShellCloaked.Add(7);
        host.OtherDesktop.Add(7);
        var hide = new WindowHide(host);

        hide.Cloaked(7).Should().BeFalse();
        hide.Show(7).Should().BeTrue();

        host.Calls.Should().BeEmpty();
    }

    [TestMethod]
    public void A_window_is_not_hidden_when_its_record_could_not_be_written()
    {
        // A desktop killed after the hide would leave a window nothing shows again.
        var host = new Host { RecordFails = true };
        var hide = new WindowHide(host);

        hide.Hide(7).Should().BeNull();

        host.Calls.Should().BeEmpty();
        hide.Way(7).Should().BeNull();
    }

    [TestMethod]
    public void The_record_names_each_way_before_the_window_is_hidden_that_way()
    {
        var host = new Host { Refusal = CloakRefusal.NoView };
        var hide = new WindowHide(host);

        hide.Hide(7);

        host.Trail.Should().Equal("record 7 Cloak", "cloak 7", "record 7 Hide", "hide 7");
    }

    [TestMethod]
    public void A_window_is_not_hidden_with_SW_HIDE_when_the_record_could_not_name_that_way()
    {
        var host = new Host { Refusal = CloakRefusal.NoView, RecordFailsFor = HideWay.Hide };
        var hide = new WindowHide(host);

        hide.Hide(7).Should().BeNull();

        host.Calls.Should().Equal("cloak 7");
        hide.Way(7).Should().BeNull();
    }

    private sealed class Host : IHideHost
    {
        public List<string> Calls { get; } = [];

        /// <summary>The calls, with each write of the record among them.</summary>
        public List<string> Trail { get; } = [];

        /// <summary>The windows under the shell's cloak that this ledger did not put there.</summary>
        public HashSet<long> ShellCloaked { get; } = [];

        /// <summary>The windows Windows keeps on a virtual desktop of its own that is not in front.</summary>
        public HashSet<long> OtherDesktop { get; } = [];

        public bool RecordFails { get; set; }

        public HideWay? RecordFailsFor { get; set; }

        public bool Record(long id, HideWay way)
        {
            Trail.Add("record " + id + " " + way);
            return !RecordFails && RecordFailsFor != way;
        }

        public bool UnderShellCloak(long id) => ShellCloaked.Contains(id);

        public bool OnAnotherDesktop(long id) => OtherDesktop.Contains(id);

        public CloakRefusal Refusal { get; set; }

        public WindowHide? Ledger { get; set; }

        public List<HideWay?> WaysSeen { get; } = [];

        public CloakRefusal Cloak(long id)
        {
            Calls.Add("cloak " + id);
            Trail.Add("cloak " + id);
            WaysSeen.Add(Ledger?.Way(id));
            return Refusal;
        }

        public CloakRefusal Uncloak(long id)
        {
            Calls.Add("uncloak " + id);
            return Refusal;
        }

        /// <summary>True when SW_HIDE leaves the window on the screen.</summary>
        public bool Stays { get; set; }

        public bool Hide(long id)
        {
            Calls.Add("hide " + id);
            Trail.Add("hide " + id);
            WaysSeen.Add(Ledger?.Way(id));
            return !Stays;
        }

        public void Unhide(long id) => Calls.Add("unhide " + id);
    }
}
