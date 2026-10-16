using Winland.Windows;

namespace Winland.Tests;

public class ProcessFactsTests
{
    [Fact]
    public void AProcessIsReadOnceWhileItKeepsItsStartTime()
    {
        var started = 100L;
        var reads = 0;
        var facts = new ProcessFacts(_ => started, _ =>
        {
            reads++;
            return "app.exe";
        }, _ => [1, 2]);

        Assert.Equal("app.exe", facts.For(42).Exe);
        Assert.Equal([1, 2], facts.For(42).Ancestors);
        Assert.Equal(1, reads);

        // The same id with another start time is another process.
        started = 200;
        facts.For(42);
        Assert.Equal(2, reads);
    }

    [Fact]
    public void AProcessWithNoStartTimeIsReadEveryTime()
    {
        var reads = 0;
        var facts = new ProcessFacts(_ => 0, _ =>
        {
            reads++;
            return "app.exe";
        }, _ => []);

        facts.For(42);
        facts.For(42);

        Assert.Equal(2, reads);
    }

    [Fact]
    public void ThisProcessIsReadFromTheSystem()
    {
        var facts = ProcessFacts.Shared.For((uint)Environment.ProcessId);

        Assert.NotEqual(0, facts.Started);
        Assert.EndsWith(".exe", facts.Exe, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(facts.Ancestors);
    }
}
