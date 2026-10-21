namespace Winland;

/// <summary>
/// Collects whether every file a config was read from is writable by administrators alone.
/// Only then may the config launch programs elevated (ADR 0007): the config is under
/// %APPDATA% by default, where any program the user runs can write.
/// </summary>
sealed class ConfigTrust
{
    /// <summary>Why the config is not trusted, from the first file that fails. Null when every file passed.</summary>
    public string? Refusal { get; private set; }

    /// <summary>Notes one file that was read. A null access list could not be read.</summary>
    public void Note(string path, AccessList? access)
    {
        if (Refusal is not null)
            return;

        if (access is not AccessList list)
        {
            Refusal = $"Winland could not check who can write '{path}'";
            return;
        }

        if (AdministratorOnly.NonAdministrator(list) is string who)
            Refusal = $"a non-administrator ({who}) can write '{path}'";
    }
}
