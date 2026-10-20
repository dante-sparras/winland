using System.Text.Json;
using Winland;

namespace Winland.Tests;

public class WorkspaceRenameTests
{
    [Fact]
    public void ARenameOutlastsAReloadOfItsDefaultName()
    {
        const string Config = "workspace = 1, defaultName:Home\n";
        var core = new Core(ConfigParser.Parse(Config));
        Assert.Equal("Home", ActiveName(core));

        Assert.Equal("ok", core.Execute("dispatch renameworkspace 1 Mail and chat").Text);
        core.Handle(new Configure(ConfigParser.Parse(Config)));

        Assert.Equal("Mail and chat", ActiveName(core));
        Assert.Equal("1", Active(core).GetProperty("address").GetString());
    }

    [Fact]
    public void TheNewNameIsNotAnAddress()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600)));
        core.Execute("dispatch renameworkspace 1 mail");

        core.Execute("dispatch workspace name:mail");
        Assert.Equal("mail", Active(core).GetProperty("address").GetString());

        core.Execute("dispatch workspace 1");
        Assert.Equal("mail", ActiveName(core));
    }

    [Fact]
    public void ASpecialWorkspaceKeepsItsName()
    {
        var core = new Core();
        core.Execute("dispatch togglespecialworkspace scratch");

        var reply = core.Execute("dispatch renameworkspace special:scratch notes");

        Assert.Equal("ok", reply.Text);
        Assert.DoesNotContain(reply.Effect.Events, item => item is WorkspaceRenamed);
    }

    [Theory]
    [InlineData("7 seven")]
    [InlineData("name: empty")]
    [InlineData("")]
    public void AWorkspaceThatIsNotThereIsAnError(string arguments)
    {
        var core = new Core();

        Assert.Equal("error: no such workspace", core.Execute("dispatch renameworkspace " + arguments).Text);
    }

    [Fact]
    public void NoNameLeavesItEmpty()
    {
        var core = new Core();

        core.Execute("dispatch renameworkspace 1");

        Assert.Equal("", ActiveName(core));
    }

    [Fact]
    public void ChangingTheIdEmitsOnlyThatEvent()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600)));

        var reply = core.Execute("dispatch changeworkspaceid 1 7");

        Assert.Equal("ok", reply.Text);
        Assert.Equal(["changeworkspaceid>>1,7"], CommandProtocol.FormatStream(reply.Effect.Events, reply.Effect.State));
        Assert.Equal(new WorkspaceId("7"), Assert.Single(reply.Effect.State.Clients).Workspace);
        Assert.Equal("7", ActiveName(core));
    }

    [Fact]
    public void TheWorkspaceHistoryFollowsTheNewId()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600)));
        core.Execute("dispatch workspace 2");
        core.Handle(new WindowAppeared(new WindowId(2), new Rectangle(0, 0, 800, 600)));

        core.Execute("dispatch changeworkspaceid 1 5");
        core.Execute("dispatch workspace previous");

        Assert.Equal("5", Active(core).GetProperty("address").GetString());
    }

    [Fact]
    public void AnIdTakesTheWholeUnsigned32BitRange()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600)));

        var reply = core.Execute("dispatch changeworkspaceid 1 4294967295");

        Assert.Equal("ok", reply.Text);
        Assert.Equal(["changeworkspaceid>>1,4294967295"], CommandProtocol.FormatStream(reply.Effect.Events, reply.Effect.State));
        Assert.Equal(4294967295L, Active(core).GetProperty("id").GetInt64());
        Assert.Equal("ok", core.Execute("dispatch workspace 3000000000").Text);
        Assert.Equal("ok", core.Execute("dispatch workspace 4294967295").Text);
        Assert.Equal("4294967295", ActiveName(core));
    }

    [Theory]
    [InlineData("1 0")]
    [InlineData("1 4294967296")]
    [InlineData("1 2 3")]
    [InlineData("name:mail 4")]
    public void AnIdChangeTheReferenceRefusesIsAnError(string arguments)
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600)));
        core.Execute("dispatch workspace name:mail");
        core.Handle(new WindowAppeared(new WindowId(2), new Rectangle(0, 0, 800, 600)));

        Assert.StartsWith("error:", core.Execute("dispatch changeworkspaceid " + arguments).Text);
    }

    private static JsonElement Active(Core core)
    {
        using var document = JsonDocument.Parse(core.Execute("j/activeworkspace").Text);
        return document.RootElement.Clone();
    }

    private static string? ActiveName(Core core) => Active(core).GetProperty("name").GetString();
}
