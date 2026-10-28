using System.Text.Json;

namespace Winland.Application.Tests;

/// <summary>
/// An owned window, which the system reports as a dialog that names its owner, on a <see cref="TestDesktop"/>.
/// </summary>
public class OwnedWindowTests
{
    private static readonly WindowTraits Child = WindowTraits.Normal with { Dialog = true };

    [Fact]
    public void OwnedWindowFloatsAndStaysOutOfGroups()
    {
        var desk = new TestDesktop();
        var parent = desk.Spawn("child-test-parent");

        desk.Ok("togglefloating", "class:child-test-parent");
        desk.Ok("pin", "enable class:child-test-parent");

        // The reference's child names its parent, as a Windows dialog names its owner.
        desk.Spawn("child-test-child", traits: Child, owner: parent);
        desk.Ok("focuswindow", "class:child-test-child");
        Assert.Equal(2, desk.Clients().Length);
        Assert.Equal(2, desk.Clients().Count(TestDesktop.Pinned));

        Assert.Equal("ok", desk.Send("reload"));
        desk.Close("child-test-child");
        desk.Close("child-test-parent");
        Assert.Empty(desk.Clients());

        // A child that names a parent floats, so it does not join the focused Group.
        desk.Spawn("a");
        desk.Ok("togglegroup");

        var second = desk.Spawn("child-test-parent");
        Assert.Equal(2, desk.Clients().Length);
        desk.Spawn("child-test-child", traits: Child, owner: second);
        desk.Ok("focuswindow", "class:child-test-child");
        Assert.Equal(3, desk.Clients().Length);

        Assert.Equal(1, desk.Clients().Count(Ungrouped));
    }

    private static bool Ungrouped(JsonElement client) => client.GetProperty("grouped").GetArrayLength() == 0;
}
