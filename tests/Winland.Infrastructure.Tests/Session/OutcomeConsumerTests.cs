namespace Winland.Infrastructure.Tests;

public class OutcomeConsumerTests
{
    // Every request the core puts in an outcome must reach the system. A new field fails here until
    // someone decides where it is applied and adds it to this list.
    [Fact]
    public void EveryOutcomeFieldHasAKnownConsumer()
    {
        string[] known =
        [
            nameof(Outcome.Placements), // WindowAdapter.ApplyCore
            nameof(Outcome.State), // WindowAdapter.ApplyCore, SessionEffects, queries
            nameof(Outcome.Consumed), // the hooks swallow consumed input
            nameof(Outcome.Traits), // Cursor, before the outcome leaves the core
            nameof(Outcome.Closes), // WindowAdapter.ApplyCore
            nameof(Outcome.Close), // shorthand for Closes
            nameof(Outcome.Focus), // WindowAdapter.ApplyCore
            nameof(Outcome.Show), // WindowAdapter.ApplyCore
            nameof(Outcome.Hide), // WindowAdapter.ApplyCore
            nameof(Outcome.ShowMinimized), // WindowAdapter.ApplyCore
            nameof(Outcome.Launches), // WindowAdapter.ApplyCore
            nameof(Outcome.Launch), // shorthand for Launches
            nameof(Outcome.Events), // WindowManager.Noticed, the event stream
            nameof(Outcome.Decorations), // WindowAdapter.ApplyCore
            nameof(Outcome.Motions), // WindowAdapter.ApplyCore
            nameof(Outcome.Live), // WindowAdapter.ApplyCore
            nameof(Outcome.ResetDecorations), // WindowAdapter.ApplyCore
            nameof(Outcome.GroupBars), // WindowAdapter.ApplyCore
            nameof(Outcome.ConfigNotice), // WindowAdapter.ApplyCore, TrayHost
            nameof(Outcome.Tray), // WindowAdapter.ApplyCore, TrayHost
            nameof(Outcome.Warp), // SessionEffects
            nameof(Outcome.Cursor), // SessionEffects
            nameof(Outcome.ResizeCursor), // not applied: behavior.md says the system pointer is not changed
            nameof(Outcome.Error), // command replies
            nameof(Outcome.Kills), // WindowAdapter.ApplyCore
            nameof(Outcome.Exit), // WindowAdapter.ApplyCore
            nameof(Outcome.Relays), // SessionEffects
            nameof(Outcome.ZOrder), // WindowAdapter.ApplyCore
            nameof(Outcome.IsEmpty), // WindowAdapter.Apply
        ];

        var fields = typeof(Outcome)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(known.Order(StringComparer.Ordinal), fields);
    }
}
