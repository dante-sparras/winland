using System.IO.Compression;

namespace Winland.Install;

/// <summary>Turns the downloaded zip into the folder layout install already copies.</summary>
public interface IReleasePackage
{
    /// <summary>Unpacks <paramref name="archive"/> into <paramref name="directory"/> and returns <c>winland.exe</c>.</summary>
    string Extract(byte[] archive, string directory);
}

public sealed class ZipReleasePackage : IReleasePackage
{
    public string Extract(byte[] archive, string directory)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;

        using var stream = new MemoryStream(archive, writable: false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.Length == 0 || entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                continue;

            // Combine keeps a rooted entry, so the prefix check is what stops it leaving the folder.
            var destination = Path.GetFullPath(Path.Combine(directory, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to unpack '{entry.FullName}'.");

            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            entry.ExtractToFile(destination);
        }

        var executable = InstallLocations.Executable(Path.Combine(directory, "winland"));
        if (!File.Exists(executable))
            throw new InvalidOperationException("The release zip has no winland\\winland.exe.");

        return executable;
    }
}
