namespace Winland.Plugins;

public sealed record PluginHostOptions
{
    public string? Directory { get; init; }

    public string? ApprovalsPath { get; init; }

    public TimeSpan? CallBudget { get; init; }
}

public static class PluginPaths
{
    public const string DirectoryVariable = "WINLAND_PLUGIN_DIR";

    public const string ApprovalsVariable = "WINLAND_PLUGIN_APPROVALS";

    public static string DefaultDirectory(string? configured, string? variable, string programData) =>
        First(configured, variable) ?? Path.Combine(programData, "Winland", "plugins");

    public static string DefaultApprovals(string? configured, string? variable, string directory) =>
        First(configured, variable) ?? Path.Combine(directory, "approved.json");

    private static string? First(string? configured, string? variable)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        if (!string.IsNullOrWhiteSpace(variable))
            return variable;

        return null;
    }
}
