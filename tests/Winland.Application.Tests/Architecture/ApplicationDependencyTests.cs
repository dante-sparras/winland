namespace Winland.Application.Tests;

/// <summary>The Application layer references the Domain and nothing outside it, and no package.</summary>
public class ApplicationDependencyTests
{
    [Fact]
    public void TheApplicationReferencesOnlyTheDomainAndTheBaseClassLibrary()
    {
        var references = typeof(WindowManager).Assembly.GetReferencedAssemblies().Select(name => name.Name!);

        Assert.All(references, name => Assert.True(name.StartsWith("System", StringComparison.Ordinal) || name == "Winland.Domain", name));
    }
}
