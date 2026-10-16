using System.Reflection;

namespace Winland.Tests.Conformance;

/// <summary>Keeps docs/conformance.md and the conformance tests saying the same thing.</summary>
public class ConformanceDocumentTests
{
    [Fact]
    public void EveryPortedCaseHasARowWithItsStatus()
    {
        var rows = ReadRows(File.ReadAllText(DocumentPath()));
        var tests = Tests();

        var ported = rows.Where(row => row.Status is "passes" or "differs").ToList();
        Assert.Equal(
            tests.Keys.Order(StringComparer.Ordinal),
            ported.Select(row => MethodName(row.Case)).Order(StringComparer.Ordinal));

        foreach (var row in ported)
        {
            var skip = tests[MethodName(row.Case)];
            if (row.Status == "passes")
                Assert.True(skip is null, $"{row.Case} is a passing row but its test is skipped.");
            else
                Assert.Equal(row.Note, skip);
        }

        Assert.All(rows, row => Assert.Contains(row.Status, new[] { "passes", "differs", "not ported" }));
        Assert.All(rows.Where(row => row.Status == "not ported"), row => Assert.NotEmpty(row.Note));
    }

    [Fact]
    public void TheScoreCountsThePassingCases()
    {
        var text = File.ReadAllText(DocumentPath());
        var tests = Tests();
        var passing = tests.Values.Count(skip => skip is null);

        Assert.Contains($"\n{passing} of {tests.Count} ported cases pass.", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    // A case name in camel or snake case, such as groups_disable_when_only, becomes GroupsDisableWhenOnly.
    private static string MethodName(string referenceCase) =>
        string.Concat(referenceCase
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    // The test methods of every conformance class, each with its skip reason or null.
    private static Dictionary<string, string?> Tests()
    {
        var tests = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var type in typeof(Desk).Assembly.GetTypes())
        {
            if (type.Namespace != typeof(Desk).Namespace || !type.Name.EndsWith("Conformance", StringComparison.Ordinal))
                continue;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<FactAttribute>() is { } fact)
                    tests.Add(method.Name, fact.Skip);
            }
        }

        return tests;
    }

    private static List<(string File, string Case, string Status, string Note)> ReadRows(string text)
    {
        var rows = new List<(string, string, string, string)>();
        var inCases = false;
        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
                inCases = line == "## Cases";

            if (!inCases || !line.StartsWith('|'))
                continue;

            var cells = line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length != 4 || cells[0] == "file" || cells[0].All(character => character == '-'))
                continue;

            rows.Add((cells[0], cells[1], cells[2], cells[3]));
        }

        return rows;
    }

    private static string DocumentPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "conformance.md");
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException("docs/conformance.md");
    }
}
