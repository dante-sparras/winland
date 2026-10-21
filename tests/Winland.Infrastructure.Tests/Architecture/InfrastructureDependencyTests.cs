namespace Winland.Infrastructure.Tests;

/// <summary>The Windows adapter references the layers inside it, never the programs that host it.</summary>
public class InfrastructureDependencyTests
{
    private static readonly string[] InnerLayers = ["Winland.Domain", "Winland.Application"];

    [Fact]
    public void TheInfrastructureReferencesOnlyTheInnerLayers()
    {
        var winland = typeof(WindowHost).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Where(name => name.StartsWith("Winland", StringComparison.Ordinal) || name.StartsWith("winland", StringComparison.Ordinal));

        Assert.All(winland, name => Assert.Contains(name, InnerLayers));
    }
}
