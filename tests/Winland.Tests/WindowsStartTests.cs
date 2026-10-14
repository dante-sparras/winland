using FluentAssertions;
using Winland.Core.Chords;
namespace Winland.Tests;

[TestClass]
public sealed class WindowsStartTests
{
    [TestMethod]
    public void The_start_menu_host_is_the_start_menu()
    {
        WindowsStart.IsStart("StartMenuExperienceHost").Should().BeTrue();
        WindowsStart.IsStart("startmenuexperiencehost").Should().BeTrue();
    }

    [TestMethod]
    public void Any_other_foreground_process_is_left_alone()
    {
        WindowsStart.IsStart("explorer").Should().BeFalse();
        WindowsStart.IsStart("SearchHost").Should().BeFalse();
        WindowsStart.IsStart(null).Should().BeFalse();
    }

    [TestMethod]
    public void A_name_read_in_place_is_judged_like_a_string()
    {
        WindowsStart.IsStart("STARTMENUEXPERIENCEHOST".AsSpan()).Should().BeTrue();
        WindowsStart.IsStart("StartMenuExperienceHost2".AsSpan()).Should().BeFalse();
        WindowsStart.IsStart(ReadOnlySpan<char>.Empty).Should().BeFalse();
    }
}

