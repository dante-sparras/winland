namespace Winland.Application.Tests.Conformance;

/// <summary>Winland's exec case. A program the core starts is a Launch in its outcome.</summary>
[Trait("Category", "Conformance")]
public class ExecConformance
{
    // The reference finds the sleep it started and checks that the compositor is its parent and reaps it. Starting
    // the process belongs to the Windows adapter, so the claim the core can make is that exec starts the command.
    [Fact]
    public void ProcessSpawning()
    {
        var desk = new Desk();

        var reply = desk.WindowManager.Execute("dispatch exec sleep 1");

        Assert.Equal("ok", reply.Text);
        Assert.Equal("sleep 1", Assert.Single(reply.Effect.Launches).CommandLine);
    }
}
