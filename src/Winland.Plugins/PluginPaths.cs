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

    /// <summary>
    /// Picks the plugin directory and approvals file for the elevated host. Any program the user
    /// runs can set the user's environment, so the variables are followed only to a location the
    /// guard passes. Otherwise the defaults under ProgramData are used and the reason is returned.
    /// </summary>
    public static PluginLocation Choose(string? directoryVariable, string? approvalsVariable, string programData, IPluginLocationGuard guard)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programData);
        ArgumentNullException.ThrowIfNull(guard);
        var standard = DefaultDirectory(null, null, programData);
        var fallback = new PluginLocation(standard, DefaultApprovals(null, null, standard), null);
        if (string.IsNullOrWhiteSpace(directoryVariable) && string.IsNullOrWhiteSpace(approvalsVariable))
            return fallback;

        var directory = DefaultDirectory(null, directoryVariable, programData);
        var approvals = DefaultApprovals(null, approvalsVariable, directory);
        var denied = guard.Deny(directory, approvals);
        if (denied is null)
            return new PluginLocation(directory, approvals, null);

        return fallback with
        {
            Ignored = $"Ignoring {DirectoryVariable} and {ApprovalsVariable}, so plugins load from '{standard}': {denied}",
        };
    }

    private static string? First(string? configured, string? variable)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        if (!string.IsNullOrWhiteSpace(variable))
            return variable;

        return null;
    }
}

/// <summary>Where plugins load from. <see cref="Ignored"/> says why a variable was not followed.</summary>
public sealed record PluginLocation(string Directory, string Approvals, string? Ignored);
