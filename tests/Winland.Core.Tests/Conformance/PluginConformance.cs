namespace Winland.Tests.Conformance;

/// <summary>
/// Winland's plugin case. Its test plugin registers a function, and the case calls it over the socket and
/// expects <c>ok</c>. A Winland plugin registers a dispatcher, which the core runs through the same path as a
/// built-in one. Loading and approving the assembly is covered by the plugin tests.
/// </summary>
[Trait("Category", "Conformance")]
public class PluginConformance
{
    [Fact]
    public void Plugin()
    {
        const string name = "conformance-plugin-test";
        var desk = new Desk();
        Assert.True(Dispatchers.TryBind(name, _ => new DispatcherResult(null, null), out var error), error);
        try
        {
            Assert.Equal("ok", desk.Dispatch(name));
        }
        finally
        {
            Dispatchers.Unbind(name);
        }
    }
}
