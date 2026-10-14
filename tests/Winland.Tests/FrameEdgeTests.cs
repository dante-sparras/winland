using FluentAssertions;
using Winland.Core;
namespace Winland.Tests;

[TestClass]
public sealed class FrameEdgeTests
{
    [TestMethod]
    public void A_client_below_the_frame_extends_by_the_whole_border()
    {
        FrameEdge.Top(clientBelowFrame: true, thickness: 2).Should().Be(2);
    }

    [TestMethod]
    public void A_client_that_meets_the_frame_covers_the_outer_pixel()
    {
        FrameEdge.Top(clientBelowFrame: false, thickness: 2).Should().Be(1);
        FrameEdge.Top(clientBelowFrame: false, thickness: 1).Should().Be(1);
    }

    [TestMethod]
    public void A_missing_border_adds_nothing()
    {
        FrameEdge.Top(clientBelowFrame: false, thickness: 0).Should().Be(0);
        FrameEdge.Top(clientBelowFrame: true, thickness: -1).Should().Be(0);
    }
}

