namespace Winland.Install;

public sealed record RelaunchSpec(string FileName, string Arguments, string WorkingDirectory);

public static class RelaunchPlan
{
    public static RelaunchSpec Create(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string? runtimeDirectory,
        string? dotnetRoot,
        string? pipe,
        string? state,
        string? interpreter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var assignments = new List<string>();
        var root = RuntimeRoot(dotnetRoot, runtimeDirectory);
        if (root is not null)
            assignments.Add(Assignment("DOTNET_ROOT", root));

        AddIfPresent(assignments, "WINLAND_PIPE", pipe);
        AddIfPresent(assignments, "WINLAND_STATE", state);

        // runas starts in System32 and does not inherit the environment. cmd can
        // enter the original directory and set DOTNET_ROOT; the verb cannot.
        var launch = QuoteAlways(executable);
        if (arguments.Count > 0)
            launch += " " + string.Join(' ', arguments.Select(Quote));

        var steps = new List<string> { "cd /d " + QuoteAlways(DirectoryOf(workingDirectory)) };
        steps.AddRange(assignments);
        steps.Add(launch);
        var interpreterPath = string.IsNullOrWhiteSpace(interpreter) ? "cmd.exe" : interpreter;
        return new RelaunchSpec(interpreterPath, "/d /s /c \"" + string.Join(" && ", steps) + "\"", workingDirectory);
    }

    private static string DirectoryOf(string workingDirectory)
    {
        var directory = workingDirectory.Trim();
        if (directory.Contains('"', StringComparison.Ordinal))
            throw new ArgumentException("Cannot enter a working directory that contains a quote.", nameof(workingDirectory));

        if (directory.Length > 3)
            directory = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return directory;
    }

    // The runtime directory is <root>/shared/Microsoft.NETCore.App/<version>/.
    private static string? RuntimeRoot(string? dotnetRoot, string? runtimeDirectory)
    {
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
            return dotnetRoot.Trim();

        if (string.IsNullOrWhiteSpace(runtimeDirectory))
            return null;

        return Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", ".."));
    }

    private static void AddIfPresent(List<string> assignments, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        assignments.Add(Assignment(name, value));
    }

    private static string Assignment(string name, string value)
    {
        if (value.Contains('"', StringComparison.Ordinal) || value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException($"Cannot pass {name} to the elevated process.");

        return "set \"" + name + "=" + value + "\"";
    }

    private static string QuoteAlways(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string Quote(string value)
    {
        if (value.AsSpan().IndexOfAny(" \"\t&|<>^") < 0)
            return value;

        return QuoteAlways(value);
    }
}
