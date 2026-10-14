using FluentAssertions;
using Winland.Ipc;

namespace Winland.Tests;

[TestClass]
public sealed class PictureTests
{
    [TestMethod]
    public void An_empty_picture_reads_back_as_workspace_one()
    {
        WmPicture.TryRead(WmPicture.Write(WmPicture.Empty), out var view).Should().BeTrue();
        view.Scene.Should().Be(0);
        view.Current.Should().Be(1);
        view.Windows.Should().Be(0);
        view.Front.Should().Equal(WmPicture.Empty.Front);
        view.Screens.Should().BeEmpty();
        view.Clients.Should().BeEmpty();
    }

    [TestMethod]
    public void A_picture_and_its_strips_round_trip()
    {
        var occupancy = new int[10];
        occupancy[1] = 2;
        occupancy[3] = 1;
        var front = new int[10];
        Array.Fill(front, -1);
        front[1] = 0;
        var view = new WmPicture.View(
            Scene: 4,
            Current: 1,
            Monitor: 0,
            Several: false,
            Switching: true,
            Scratchpad: false,
            Windows: 2,
            Occupancy: occupancy,
            Front: front,
            Screens: [new WmPicture.Screen(0, 0, 800, 600, false)],
            FrontX: 0,
            FrontY: 0,
            FrontWidth: 800,
            FrontHeight: 600,
            Clients: [new WmPicture.Client(42, "Code", "workspace", 1)]);

        WmPicture.TryRead(WmPicture.Write(view), out var read).Should().BeTrue();
        read.Scene.Should().Be(4);
        read.Current.Should().Be(1);
        read.Switching.Should().BeTrue();
        read.Windows.Should().Be(2);
        read.Occupancy[1].Should().Be(2);
        read.Occupancy[3].Should().Be(1);
        read.Front[1].Should().Be(0);
        read.Front[2].Should().Be(-1);
        read.Screens.Should().ContainSingle();
        read.Screens[0].Width.Should().Be(800);
        read.FrontWidth.Should().Be(800);
        read.Clients.Should().ContainSingle();
        read.Clients[0].Id.Should().Be(42);
        read.Clients[0].Title.Should().Be("Code");
        read.Clients[0].Place.Should().Be("workspace");
        read.Clients[0].Workspace.Should().Be(1);

        var strips = new WmPicture.Strip[] { new(10, 20, 300, 28, [42L, 7L], 0) };
        WmPicture.TryStrips(WmPicture.WriteStrips(strips), out var back).Should().BeTrue();
        back.Should().ContainSingle();
        back[0].X.Should().Be(10);
        back[0].Y.Should().Be(20);
        back[0].Width.Should().Be(300);
        back[0].Height.Should().Be(28);
        back[0].Active.Should().Be(0);
        back[0].Members.Should().Equal(42L, 7L);
    }
}
