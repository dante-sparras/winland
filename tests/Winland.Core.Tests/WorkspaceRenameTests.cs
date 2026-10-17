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

    private static JsonElement Active(Core core)
    {
        using var document = JsonDocument.Parse(core.Execute("activeworkspace").Text);
        return document.RootElement.Clone();
    }

    private static string? ActiveName(Core core) => Active(core).GetProperty("name").GetString();
}
