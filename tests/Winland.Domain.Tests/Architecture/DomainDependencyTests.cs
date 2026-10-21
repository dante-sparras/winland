namespace Winland.Domain.Tests;

/// <summary>The Domain is the innermost layer. It references no other Winland project and no package.</summary>
public class DomainDependencyTests
{
    [Fact]
    public void TheDomainReferencesOnlyTheBaseClassLibrary()
    {
        var references = typeof(WindowId).Assembly.GetReferencedAssemblies().Select(name => name.Name!);

        Assert.All(references, name => Assert.True(name.StartsWith("System", StringComparison.Ordinal), name));
    }
}
