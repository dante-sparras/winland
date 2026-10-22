namespace Winland.Application.Tests;

public class DispatcherTableTests
{
    [Fact]
    public void TheTableRunsExactlyTheDispatchersTheConfigAccepts()
    {
        var table = WindowManager.DispatcherNames.Order(StringComparer.Ordinal).ToArray();
        var known = Dispatchers.Names.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(known, table);
    }

    [Fact]
    public void AnUnknownDispatcherIsAnError()
    {
        var reply = new WindowManager().Execute("dispatch notadispatcher");

        Assert.Equal("error: unknown dispatcher 'notadispatcher'", reply.Text);
    }
}
