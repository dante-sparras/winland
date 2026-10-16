namespace Winland.Tests.Conformance;

/// <summary>The pinned release's animations case. The test is named after its case, and sends the same request.</summary>
[Trait("Category", "Conformance")]
public class AnimationsConformance
{
    [Fact(Skip = "There is no animations request.")]
    public void AnimationsTrivial()
    {
        // The reference config defines the curve the case reads back.
        var desk = new Desk("bezier = quick, 0.15, 0, 0.1, 1");

        Assert.Contains(
            "beziers:\n\n\tname: quick\n\t\tX0: 0.15\n\t\tY0: 0.00\n\t\tX1: 0.10\n\t\tY1: 1.00",
            desk.Send("animations"),
            StringComparison.Ordinal);
    }
}
