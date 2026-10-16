namespace Winland.Plugins.Manager;

/// <summary>Sets an access list on a new plugin directory so only administrators can write it.</summary>
public interface IPluginDirectoryProtector
{
    void Protect(string directory);
}

public sealed record ApplyResult(bool Ok, IReadOnlyList<string> Lines);

/// <summary>
/// The elevated step. It copies each staged assembly into the protected plugin directory,
/// hashes the copy, and approves that hash only when it equals the hash the user was shown.
/// One mismatch installs nothing.
/// </summary>
public sealed class PluginApply(IPluginLocationGuard guard, IPluginDirectoryProtector protector, string defaultDirectory)
{
    private const string Pending = ".new";

    public ApplyResult Run(ApplyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var lines = new List<string>();
        var directory = Path.GetFullPath(request.PluginDirectory);
        var approvals = Path.GetFullPath(request.ApprovalsPath);

        var invalid = Validate(request);
        if (invalid is not null)
            return Fail(invalid);

        var unprepared = Prepare(directory, approvals, lines);
        if (unprepared is not null)
            return Fail(unprepared);

        var denied = guard.Deny(directory, approvals);
        if (denied is not null)
            return Fail(denied);

        IReadOnlyList<InstalledPlugin> existing;
        try
        {
            existing = InstalledPlugins.Read(approvals);
        }
        catch (InvalidDataException exception)
        {
            return Fail(exception.Message);
        }

        var replaced = request.Install.Select(repository => repository.Url)
            .Concat(request.Remove)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var url in request.Remove)
        {
            if (!existing.Any(entry => SameRepository(entry, url)))
                return Fail($"Repository '{url}' is not installed.");
        }

        foreach (var repository in request.Install)
        {
            foreach (var plugin in repository.Plugins)
            {
                var owner = existing.FirstOrDefault(entry => entry.Name == plugin.Name && !replaced.Contains(entry.Repository ?? ""));
                if (owner is not null)
                {
                    var from = owner.Repository is null ? "by hand" : $"from '{owner.Repository}'";
                    return Fail($"Plugin '{plugin.Name}' is already approved {from}. Remove it first.");
                }
            }
        }

        var copies = new List<(string Pending, string Target)>();
        var added = new List<InstalledPlugin>();
        foreach (var repository in request.Install)
        {
            foreach (var plugin in repository.Plugins)
            {
                var target = Path.Combine(directory, plugin.Name + ".dll");
                var pending = target + Pending;
                copies.Add((pending, target));
                try
                {
                    File.Copy(plugin.Source, pending, overwrite: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Discard(copies);
                    return Fail($"Could not copy '{plugin.Source}': {exception.Message}");
                }

                var hash = PluginHash.OfFile(pending);
                if (!hash.Equals(plugin.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Discard(copies);
                    return Fail($"The assembly for plugin '{plugin.Name}' changed after it was shown. Nothing was installed.");
                }

                added.Add(new InstalledPlugin(plugin.Name, hash, target, repository.Url, repository.Commit));
            }
        }

        foreach (var (pending, target) in copies)
            File.Move(pending, target, overwrite: true);

        var kept = existing.Where(entry => entry.Repository is null || !replaced.Contains(entry.Repository)).ToList();
        var dropped = existing.Except(kept).ToArray();
        kept.AddRange(added);
        InstalledPlugins.Write(approvals, kept);

        foreach (var entry in dropped)
        {
            if (added.Exists(plugin => plugin.Name == entry.Name))
                continue;

            DeleteInside(directory, entry.File);
            lines.Add($"Removed plugin '{entry.Name}'.");
        }

        foreach (var plugin in added)
            lines.Add($"Installed plugin '{plugin.Name}' to '{plugin.File}' (sha256 {plugin.Sha256}).");

        return new ApplyResult(true, lines);
    }

    // The paths come from a program that is not elevated, and the user's environment can point
    // them anywhere. Only the default directory is created and protected. Another one is used
    // only when an administrator already set it up with an approvals file; the guard then decides.
    private string? Prepare(string directory, string approvals, List<string> lines)
    {
        var standard = Path.GetFullPath(defaultDirectory);
        if (!SamePath(directory, standard) || !SamePath(approvals, PluginPaths.DefaultApprovals(null, null, standard)))
        {
            if (Directory.Exists(directory) && File.Exists(approvals))
                return null;

            return $"'{directory}' is not the default plugin directory '{standard}', and an administrator has not set it up with the approvals file '{approvals}'. "
                + $"Set it up as an administrator, or unset {PluginPaths.DirectoryVariable} and {PluginPaths.ApprovalsVariable}.";
        }

        var reparse = ReparsePoint(directory);
        if (reparse is not null)
            return $"Refusing to use the plugin directory because '{reparse}' is a reparse point.";

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            lines.Add($"Created the plugin directory '{directory}'.");
        }

        // A non-administrator may have created the folder first. Finding it proves nothing,
        // so its owner and access list are replaced every time.
        protector.Protect(directory);
        return null;
    }

    // A junction would send the protection and the copies to wherever it points.
    private static string? ReparsePoint(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return current.FullName;
        }

        return null;
    }

    private static bool SamePath(string left, string right) =>
        Path.TrimEndingDirectorySeparator(left).Equals(Path.TrimEndingDirectorySeparator(right), StringComparison.OrdinalIgnoreCase);

    private static string? Validate(ApplyRequest request)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var repository in request.Install)
        {
            if (string.IsNullOrWhiteSpace(repository.Url) || string.IsNullOrWhiteSpace(repository.Commit))
                return "Each repository needs a URL and a commit.";

            if (repository.Plugins.Count == 0)
                return $"Repository '{repository.Url}' lists no plugins.";

            foreach (var plugin in repository.Plugins)
            {
                if (!PluginNames.IsValid(plugin.Name))
                    return $"'{plugin.Name}' is not a valid plugin name.";

                if (!names.Add(plugin.Name))
                    return $"Plugin '{plugin.Name}' is listed twice.";

                if (plugin.Sha256.Length != 64 || !plugin.Sha256.All(char.IsAsciiHexDigitLower))
                    return $"'{plugin.Sha256}' is not a SHA-256.";

                if (!Path.IsPathRooted(plugin.Source))
                    return $"Assembly path '{plugin.Source}' is not absolute.";
            }
        }

        return null;
    }

    private static bool SameRepository(InstalledPlugin entry, string url) =>
        entry.Repository is not null && entry.Repository.Equals(url, StringComparison.OrdinalIgnoreCase);

    private static void Discard(List<(string Pending, string Target)> copies)
    {
        foreach (var (pending, _) in copies)
            File.Delete(pending);
    }

    private static void DeleteInside(string directory, string? file)
    {
        if (file is null)
            return;

        var full = Path.GetFullPath(file);
        if (!PluginNames.IsInside(directory, full))
            return;

        File.Delete(full);
    }

    private static ApplyResult Fail(string message) => new(false, [message]);
}
