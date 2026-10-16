namespace Winland;

/// <summary>
/// Marks a config document as allowed to launch elevated, or says why its <c>[elevated]</c>
/// requests run de-elevated. The daemon decides whether the files are protected; this only
/// applies that decision.
/// </summary>
public static class ConfigElevation
{
    /// <summary>
    /// A null <paramref name="refusal"/> trusts the document. Otherwise the document stays
    /// untrusted, with one warning when it asks for an elevated launch anywhere.
    /// </summary>
    public static ConfigDocument Decide(ConfigDocument document, string? refusal)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (refusal is null)
            return document with { ElevationTrusted = true };

        var untrusted = document with { ElevationTrusted = false };
        if (!RequestsElevation(document))
            return untrusted;

        var warning = new ConfigWarning(
            0,
            $"[elevated] launches run de-elevated because {refusal}. Only a config that only administrators can write may launch programs elevated.");
        return untrusted with { Warnings = [.. document.Warnings, warning] };
    }

    /// <summary>True when an exec line, a bind or a workspace rule asks for an elevated launch.</summary>
    public static bool RequestsElevation(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (var setting in document.Settings)
        {
            if (setting.Keyword.StartsWith("exec", StringComparison.Ordinal) && Elevated(setting.Value))
                return true;
        }

        foreach (var directive in document.Binds)
        {
            if (directive is Bind bind && LaunchParser.IsExec(bind.Dispatcher) && Elevated(bind.Arguments))
                return true;
        }

        foreach (var rule in document.WorkspaceRules)
        {
            if (rule.OnCreatedEmpty is string command && Elevated(command))
                return true;
        }

        return false;
    }

    internal static bool Elevated(string command) =>
        LaunchParser.Parse(command, allowsElevation: true, raw: false).Launch?.Elevated == true;
}
