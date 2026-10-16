namespace Winland.Plugins;

/// <summary>
/// Decides whether a plugin directory or approvals file is writable by a non-administrator.
/// The Windows access-list reader is separate so tests can pass a fake list.
/// </summary>
public static class PluginLocationPolicy
{
    public static string? Deny(
        string directoryPath,
        AccessList directory,
        string approvalsPath,
        AccessList? approvals)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalsPath);

        var writer = AdministratorOnly.NonAdministrator(directory);
        if (writer is not null)
            return Message(directoryPath, writer);

        if (approvals is not AccessList file)
            return null;

        writer = AdministratorOnly.NonAdministrator(file);
        return writer is null ? null : Message(approvalsPath, writer);
    }

    private static string Message(string path, string who) =>
        $"Refusing to load plugins because a non-administrator ({who}) can write '{path}'.";
}
