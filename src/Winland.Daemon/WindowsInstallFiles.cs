using System.Diagnostics;
using Winland.Install;

namespace Winland;

/// <summary>
/// Copies and deletes the install folders. A link is never followed: the elevated task must start the
/// file that was copied, not a file a junction was pointed at afterwards.
/// </summary>
sealed class WindowsInstallFiles : IFileTree
{
    private int retirementScheduled;

    private string? imageDirectory;

    public bool Exists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IReadOnlyList<ListedFile> List(string directory)
    {
        if (!Directory.Exists(directory))
            return [];

        var root = Path.GetFullPath(directory);
        var listed = new List<ListedFile>();
        Walk(root, root, listed);
        return listed;
    }

    public bool SameContent(string left, string right)
    {
        using var first = OpenPlain(left);
        using var second = OpenPlain(right);
        if (first.Length != second.Length)
            return false;

        var buffer = new byte[81920];
        var other = new byte[buffer.Length];
        int read;
        while ((read = first.Read(buffer, 0, buffer.Length)) > 0)
        {
            var got = 0;
            while (got < read)
            {
                var count = second.Read(other, got, read - got);
                if (count == 0)
                    return false;

                got += count;
            }

            if (!buffer.AsSpan(0, read).SequenceEqual(other.AsSpan(0, read)))
                return false;
        }

        return true;
    }

    public void PrepareDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException($"Refusing to install into '{directory}': it is a link.");
    }

    public void Copy(string source, string destination)
    {
        var handle = WindowsFileHandles.Open(source, WindowsFileHandles.GenericRead, FileShare.Read, followReparse: false);
        var owned = true;
        try
        {
            if (WindowsFileHandles.Attributes(handle).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException($"Refusing to install '{source}': it is a link.");

            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
                PrepareDirectory(parent);

            if (File.Exists(destination) || Directory.Exists(destination))
            {
                var attributes = File.GetAttributes(destination);
                if (attributes.HasFlag(FileAttributes.ReparsePoint) || attributes.HasFlag(FileAttributes.Directory))
                    throw new InvalidOperationException($"Refusing to install over '{destination}': it is a link or a folder.");

                try
                {
                    File.Delete(destination);
                }
                catch (Exception exception) when (exception is (IOException or UnauthorizedAccessException) && ImageDirectoryHolds(destination))
                {
                    // Windows will not delete the executable this process is running, or a library it has
                    // loaded. It will rename them, and the new file can then take the old name.
                    MoveAside(destination);
                }
            }

            using var input = new FileStream(handle, FileAccess.Read);
            owned = false;
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
        finally
        {
            if (owned)
                handle.Dispose();
        }
    }

    public void DeleteTree(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
        {
            Directory.Delete(directory);
            return;
        }

        DeleteContents(directory, directory);
        try
        {
            Directory.Delete(directory, recursive: false);
        }
        catch (Exception exception) when (ImageIsInside(directory) && exception is (IOException or UnauthorizedAccessException))
        {
            // This process is winland.exe in the folder being removed, so Windows will not delete it yet.
            ScheduleDelete(directory);
        }
    }

    private static void Walk(string root, string current, List<ListedFile> listed)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(current))
        {
            var relative = Path.GetRelativePath(root, entry);
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                listed.Add(new ListedFile(relative, true));
                continue;
            }

            if (attributes.HasFlag(FileAttributes.Directory))
                Walk(root, entry, listed);
            else
                listed.Add(new ListedFile(relative, false));
        }
    }

    private static void DeleteContents(string directory, string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                if (attributes.HasFlag(FileAttributes.Directory))
                    Directory.Delete(entry);
                else
                    File.Delete(entry);

                continue;
            }

            if (attributes.HasFlag(FileAttributes.Directory))
            {
                DeleteContents(entry, root);
                Directory.Delete(entry, recursive: false);
                continue;
            }

            try
            {
                File.Delete(entry);
            }
            catch (Exception exception) when (ImageIsInside(root) && exception is (IOException or UnauthorizedAccessException))
            {
            }
        }
    }

    private static FileStream OpenPlain(string path)
    {
        var handle = WindowsFileHandles.Open(path, WindowsFileHandles.GenericRead, FileShare.Read, followReparse: false);
        if (WindowsFileHandles.Attributes(handle).HasFlag(FileAttributes.ReparsePoint))
        {
            handle.Dispose();
            throw new InvalidOperationException($"Refusing to install '{path}': it is a link.");
        }

        return new FileStream(handle, FileAccess.Read);
    }

    private bool ImageDirectoryHolds(string path)
    {
        var root = ImageDirectory();
        if (root is null)
            return false;

        var full = Path.GetFullPath(path);
        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private string? ImageDirectory()
    {
        if (imageDirectory is not null)
            return imageDirectory;

        var image = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(image))
            return null;

        imageDirectory = Path.GetDirectoryName(Path.GetFullPath(image));
        return imageDirectory;
    }

    private void MoveAside(string destination)
    {
        var aside = destination + ".winland-old";
        if (File.Exists(aside) || Directory.Exists(aside))
        {
            var attributes = File.GetAttributes(aside);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException($"Refusing to install over '{aside}': it is a link.");

            var removed = false;
            if (!attributes.HasFlag(FileAttributes.Directory))
            {
                try
                {
                    File.Delete(aside);
                    removed = true;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }

            if (!removed)
                aside = destination + ".winland-old-" + Guid.NewGuid().ToString("N");
        }

        File.Move(destination, aside);
        var directory = ImageDirectory();
        if (directory is not null)
            ScheduleRetirement(directory);
    }

    private void ScheduleRetirement(string directory)
    {
        if (Interlocked.Exchange(ref retirementScheduled, 1) != 0)
            return;

        var script = "Wait-Process -Id " + Environment.ProcessId + "\r\n"
            + "function Remove-Retired([string]$Path) {\r\n"
            + "  if (-not (Test-Path -LiteralPath $Path)) { return }\r\n"
            + "  $attributes = [IO.File]::GetAttributes($Path)\r\n"
            + "  $link = ($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0\r\n"
            + "  $container = ($attributes -band [IO.FileAttributes]::Directory) -ne 0\r\n"
            + "  if ($link) { return }\r\n"
            + "  if ($container) {\r\n"
            + "    foreach ($child in [IO.Directory]::EnumerateFileSystemEntries($Path)) { Remove-Retired $child }\r\n"
            + "    return\r\n"
            + "  }\r\n"
            + "  if ([IO.Path]::GetFileName($Path).Contains('.winland-old')) {\r\n"
            + "    [IO.File]::SetAttributes($Path, [IO.FileAttributes]::Normal)\r\n"
            + "    [IO.File]::Delete($Path)\r\n"
            + "  }\r\n"
            + "}\r\n"
            + "Remove-Retired " + Quote(directory) + "\r\n"
            + "Remove-Item -LiteralPath $PSCommandPath -Force\r\n";

        StartCleanupScript(script);
    }

    private static bool ImageIsInside(string directory)
    {
        var image = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(image))
            return false;

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(image).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static void ScheduleDelete(string directory)
    {
        var script = "Wait-Process -Id " + Environment.ProcessId + "\r\n"
            + "function Remove-Tree([string]$Path) {\r\n"
            + "  if (-not (Test-Path -LiteralPath $Path)) { return }\r\n"
            + "  $attributes = [IO.File]::GetAttributes($Path)\r\n"
            + "  $link = ($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0\r\n"
            + "  $container = ($attributes -band [IO.FileAttributes]::Directory) -ne 0\r\n"
            + "  if ($link) {\r\n"
            + "    if ($container) { [IO.Directory]::Delete($Path) } else { [IO.File]::Delete($Path) }\r\n"
            + "    return\r\n"
            + "  }\r\n"
            + "  if ($container) {\r\n"
            + "    foreach ($child in [IO.Directory]::EnumerateFileSystemEntries($Path)) { Remove-Tree $child }\r\n"
            + "    [IO.Directory]::Delete($Path)\r\n"
            + "  } else { [IO.File]::Delete($Path) }\r\n"
            + "}\r\n"
            + "Remove-Tree " + Quote(directory) + "\r\n"
            + "Remove-Item -LiteralPath $PSCommandPath -Force\r\n";

        StartCleanupScript(script);
    }

    private static void StartCleanupScript(string script)
    {
        string folder;
        if (Elevation.IsElevated())
        {
            var windowsTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            folder = AdministratorFolder.CreateNew(windowsTemp, "winland-");
        }
        else
        {
            folder = Path.Combine(Path.GetTempPath(), "winland-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
        }

        var path = Path.Combine(folder, "remove.ps1");
        File.WriteAllText(path, script);
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = powershell,
            ArgumentList = { "-NoProfile", "-File", path },
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (process is null)
            throw new InvalidOperationException("Could not remove the install folder after Winland exits.");
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
