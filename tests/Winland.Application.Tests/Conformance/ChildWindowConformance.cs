using System.Text.Json;

namespace Winland.Application.Tests.Conformance;

/// <summary>
/// Winland's child window case. Its client opens a parent toplevel and then a child toplevel that names
/// the parent. On Windows that child is an owned window, which the system reports as a dialog.
/// </summary>
[Trait("Category", "Conformance")]
public class ChildWindowConformance
{
    private static readonly WindowTraits Child = WindowTraits.Normal with { Dialog = true };

    [Fact]
    public void ChildWindow()
    {
        var desk = new Desk();
        var parent = desk.Spawn("child-test-parent");

        desk.Ok("setfloating", "class:child-test-parent");
        desk.Ok("pin", "enable class:child-test-parent");

        // The reference's child names its parent, as a Windows dialog names its owner.
        desk.Spawn("child-test-child", traits: Child, owner: parent);
        desk.Ok("focuswindow", "class:child-test-child");
        Assert.Equal(2, desk.Clients().Length);
        Assert.Equal(2, desk.Clients().Count(Desk.Pinned));

        Assert.Equal("ok", desk.Send("reload"));
        desk.Close("child-test-child");
        desk.Close("child-test-parent");
        Assert.Empty(desk.Clients());

        // A child that names a parent floats, so group:auto_group does not take it into the focused Group.
        desk.Spawn("a");
        desk.Ok("togglegroup");
        desk.Keyword("group:auto_group", "true");

        var second = desk.Spawn("child-test-parent");
        Assert.Equal(2, desk.Clients().Length);
        desk.Spawn("child-test-child", traits: Child, owner: second);
        desk.Ok("focuswindow", "class:child-test-child");
        Assert.Equal(3, desk.Clients().Length);

        Assert.Equal(1, desk.Clients().Count(Ungrouped));
    }

    private static bool Ungrouped(JsonElement client) => client.GetProperty("grouped").GetArrayLength() == 0;
}
