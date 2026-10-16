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

    private const string Previous = ".old";

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
            existing = ReadApprovals(approvals, out denied);
        }
        catch (InvalidDataException exception)
        {
            return Fail(exception.Message);
        }

        if (denied is not null)
            return Fail(denied);

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
                var owner = existing.FirstOrDefault(entry => PluginNames.Same(entry.Name, plugin.Name) && !replaced.Contains(entry.Repository ?? ""));
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

        // All or nothing. While files are swapped the approvals hold the old and the new hashes,
        // so whichever file the host finds loads. A move that fails puts every file and the
        // approvals back as they were.
        try
        {
            InstalledPlugins.Write(approvals, [.. existing, .. added]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Discard(copies);
            return Fail($"Could not write the approvals '{approvals}': {exception.Message} Nothing was changed.");
        }

        var moveFailure = MoveAll(copies);
        if (moveFailure is not null)
        {
            InstalledPlugins.Write(approvals, existing);
            return Fail($"Could not replace a plugin assembly: {moveFailure} Nothing was changed.");
        }

        var kept = existing.Where(entry => entry.Repository is null || !replaced.Contains(entry.Repository)).ToList();
        var dropped = existing.Except(kept).ToArray();
        kept.AddRange(added);
        InstalledPlugins.Write(approvals, kept);
        foreach (var (_, target) in copies)
            DeleteQuietly(target + Previous);

        foreach (var entry in dropped)
        {
            if (added.Exists(plugin => PluginNames.Same(plugin.Name, entry.Name)))
                continue;

            lines.Add($"Removed plugin '{entry.Name}'.");
            if (DeleteInside(directory, entry.File) is string failure)
                lines.Add($"Could not delete '{entry.File}': {failure} Its approval is gone, so it no longer loads.");
        }

        foreach (var plugin in added)
            lines.Add($"Installed plugin '{plugin.Name}' to '{plugin.File}' (sha256 {plugin.Sha256}).");

        return new ApplyResult(true, lines);
    }

    // The approvals that are kept are read through a handle the guard checked, so a file put in
    // place of the protected one after the check cannot add entries.
    private IReadOnlyList<InstalledPlugin> ReadApprovals(string approvals, out string? denied)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(approvals, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (FileNotFoundException)
        {
            denied = null;
            return [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Could not read approvals '{approvals}': {exception.Message}", exception);
        }

        using (stream)
        {
            denied = guard.DenyApprovals(stream);
            return denied is null ? InstalledPlugins.Read(stream, approvals) : [];
        }
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

        // A non-administrator may have created either folder first. Finding it proves nothing,
        // so its owner and access list are replaced every time. The parent goes first: whoever owns
        // it could rename the protected directory away and put a folder of their own in its place.
        var parent = Path.GetDirectoryName(directory);
        try
        {
            if (parent is not null)
                protector.Protect(parent);

            protector.Protect(directory);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return $"Could not protect the plugin directory: {exception.Message}";
        }

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
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

    // Each existing target is set aside before the new file takes its name, so it can come back.
    private static string? MoveAll(List<(string Pending, string Target)> copies)
    {
        var done = new List<(string Target, bool HadPrevious)>();
        foreach (var (pending, target) in copies)
        {
            var hadPrevious = File.Exists(target);
            try
            {
                if (hadPrevious)
                    File.Move(target, target + Previous, overwrite: true);

                try
                {
                    File.Move(pending, target);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    if (hadPrevious)
                        File.Move(target + Previous, target);

                    throw;
                }

                done.Add((target, hadPrevious));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                foreach (var (moved, previous) in Enumerable.Reverse(done))
                {
                    if (previous)
                        File.Move(moved + Previous, moved, overwrite: true);
                    else
                        File.Delete(moved);
                }

                Discard(copies);
                return $"'{target}': {exception.Message}";
            }
        }

        return null;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover copy of a replaced assembly loads nothing: it is not named <plugin>.dll.
            _ = exception;
        }
    }

    private static void Discard(List<(string Pending, string Target)> copies)
    {
        foreach (var (pending, _) in copies)
            File.Delete(pending);
    }

    // The approvals are already written when this runs, so a file that cannot be deleted is reported, not thrown.
    private static string? DeleteInside(string directory, string? file)
    {
        if (file is null)
            return null;

        var full = Path.GetFullPath(file);
        if (!PluginNames.IsInside(directory, full))
            return null;

        try
        {
            File.Delete(full);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }

    private static ApplyResult Fail(string message) => new(false, [message]);
}
