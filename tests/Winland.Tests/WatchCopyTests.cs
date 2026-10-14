using FluentAssertions;
using Winland.Core;

namespace Winoma.Tests;

/// <summary>Which watch copy a product may remove, and when the process it waits on is gone.</summary>
[TestClass]
public sealed class WatchCopyTests
{
    private static readonly DateTimeOffset Started = new(2026, 10, 14, 8, 41, 21, TimeSpan.FromHours(2));

    [TestMethod]
    public void Each_product_removes_only_its_own_old_copies()
    {
        const string kept = @"C:\Winoma\watch\w-20261014064121-13472";
        const string older = @"C:\Winoma\watch\w-20261014000000-1";
        const string shell = @"C:\Winoma\watch\20261014064121-99";

        WatchCopies.IsWinland(kept).Should().BeTrue();
        WatchCopies.IsWinland(shell).Should().BeFalse();

        WatchCopies.Removable(older, kept, winland: true).Should().BeTrue();
        WatchCopies.Removable(shell, kept, winland: true).Should().BeFalse();
        WatchCopies.Removable(kept, kept, winland: true).Should().BeFalse();
        WatchCopies.Removable(kept.ToUpperInvariant(), kept, winland: true).Should().BeFalse();

        WatchCopies.Removable(shell, @"C:\Winoma\watch\20261014075813-21420", winland: false).Should().BeTrue();
        WatchCopies.Removable(older, @"C:\Winoma\watch\20261014075813-21420", winland: false).Should().BeFalse();
    }

    [TestMethod]
    public void A_watched_process_is_gone_when_its_id_is_missing_it_has_exited_or_the_id_was_reused()
    {
        WatchedProcess.Gone(Started, null).Should().BeTrue();
        WatchedProcess.Gone(Started, new WatchedProcess.Sight(Started, HasExited: true)).Should().BeTrue();
        WatchedProcess.Gone(Started, new WatchedProcess.Sight(Started.AddSeconds(30), HasExited: false)).Should().BeTrue();

        WatchedProcess.Gone(Started, new WatchedProcess.Sight(Started, HasExited: false)).Should().BeFalse();
        WatchedProcess.Gone(null, new WatchedProcess.Sight(Started, HasExited: false)).Should().BeFalse();
    }
}
