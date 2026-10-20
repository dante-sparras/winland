using System.Text;
using Microsoft.Win32.SafeHandles;
using Winland.Plugins;

namespace Winland;

/// <summary>
/// Keeps the recovery file. Winland runs elevated and the file sits in a folder the user can change,
/// so no path is trusted twice. The folder is opened once per operation, and an elevated Winland
/// checks who may empty it through that handle. Each file is opened without following a link at its
/// own name, used only when the handle shows it is in that same folder, and renamed into the folder
/// by handle. A junction or a rename on the way cannot send a write or a delete anywhere else.
/// </summary>
sealed class RecoveryStore
{
    private readonly string _path;
    private readonly string _temp;
    private readonly string _folder;
    private readonly Func<SafeFileHandle, string?>? _refuseFolder;
    private readonly Lock _gate = new();
    private bool _armed;

    /// <summary>The recovery file.</summary>
    public string File => _path;
    private bool _closed;
    private bool _reported;
    private string? _written;

    /// <param name="path">The recovery file.</param>
    /// <param name="refuseFolder">
    /// Checks the folder, through its open handle, before anything is read or written there. An elevated
    /// Winland passes one, and null means the folder may be used.
    /// </param>
    public RecoveryStore(string path, Func<SafeFileHandle, string?>? refuseFolder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _temp = _path + ".tmp";
        _folder = Path.GetDirectoryName(_path) ?? throw new ArgumentException("The state file names no folder.", nameof(path));
        _refuseFolder = refuseFolder;
    }

    public RecoverySnapshot? Read()
    {
        try
        {
            using var folder = OpenFolder();
            if (folder is null)
                return null;

            using var file = OpenEntry(_path, WindowsFileHandles.GenericRead, folder);
            if (file is null)
                return null;

            var bytes = new byte[RandomAccess.GetLength(file)];
            var read = 0;
            while (read < bytes.Length)
            {
                var count = RandomAccess.Read(file, bytes.AsSpan(read), read);
                if (count == 0)
                    break;

                read += count;
            }

            var json = Encoding.UTF8.GetString(bytes, 0, read);
            return RecoverySnapshot.TryParse(json, out var snapshot) ? snapshot : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not read '{_path}': {exception.Message}");
            return null;
        }
    }

    public void Arm()
    {
        lock (_gate)
            _armed = true;
    }

    public void Save(RecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var json = snapshot.ToJson();
        lock (_gate)
        {
            if (!_armed || _closed || json == _written)
                return;

            try
            {
                WriteAtomic(json);
                _written = json;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Report($"Could not write '{_path}': {exception.Message}");
            }
        }
    }

    public void Delete()
    {
        lock (_gate)
        {
            _closed = true;
            _armed = false;
            _written = null;
            try
            {
                using var folder = OpenFolder();
                if (folder is null)
                    return;

                DeleteEntry(_path, folder);
                DeleteEntry(_temp, folder);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not delete '{_path}': {exception.Message}");
            }
        }
    }

    private void WriteAtomic(string json)
    {
        Directory.CreateDirectory(_folder);
        using var folder = OpenFolder()
            ?? throw new IOException($"The folder '{_folder}' is gone.");

        // A temporary file left by a run that died is removed first. The new one must be new: an
        // existing file, or a link of that name, could be someone else's.
        DeleteEntry(_temp, folder);
        using var file = WindowsFileHandles.CreateNewFile(_temp, FileShare.None);
        if (!InFolder(file, folder))
        {
            WindowsFileHandles.MarkForDeletion(file);
            throw new IOException($"'{_temp}' was not made in '{_folder}'.");
        }

        RandomAccess.Write(file, Encoding.UTF8.GetBytes(json), 0);

        // A rename cannot replace a junction. A link at the file's name is only a way elsewhere, so
        // the link itself goes; a file there is replaced in one step.
        using (var existing = OpenEntry(_path, WindowsFileHandles.Delete, folder, link: true))
        {
            if (existing is not null && WindowsFileHandles.Attributes(existing).HasFlag(FileAttributes.ReparsePoint))
                WindowsFileHandles.MarkForDeletion(existing);
        }

        WindowsFileHandles.Rename(file, folder, Path.GetFileName(_path));
    }

    // Null when the folder does not exist. Throws when it may not be used.
    private SafeFileHandle? OpenFolder()
    {
        SafeFileHandle folder;
        try
        {
            folder = WindowsFileHandles.Open(
                _folder,
                WindowsFileHandles.ReadAttributes | WindowsFileHandles.ReadControl,
                FileShare.ReadWrite | FileShare.Delete,
                followReparse: true);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        var refused = (WindowsFileHandles.Attributes(folder) & FileAttributes.Directory) == 0
            ? $"'{_folder}' is not a folder."
            : _refuseFolder?.Invoke(folder);
        if (refused is null)
            return folder;

        folder.Dispose();
        throw new UnauthorizedAccessException(refused);
    }

    // Null when nothing has that name. A link of that name is opened itself, and an entry that is
    // not in the open folder, because a folder on the way was swapped, is not touched. Only a link
    // itself may be deleted, never read: what it points to could be anyone's.
    private static SafeFileHandle? OpenEntry(string path, uint access, SafeFileHandle folder, bool link = false)
    {
        SafeFileHandle entry;
        try
        {
            entry = WindowsFileHandles.Open(path, access | WindowsFileHandles.ReadAttributes, FileShare.ReadWrite | FileShare.Delete, followReparse: false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        var attributes = WindowsFileHandles.Attributes(entry);
        var reparse = attributes.HasFlag(FileAttributes.ReparsePoint);
        var usable = reparse ? link : !attributes.HasFlag(FileAttributes.Directory);
        if (usable && InFolder(entry, folder))
            return entry;

        entry.Dispose();
        throw new IOException($"'{path}' is a link, a folder, or not in the state folder, and was left alone.");
    }

    private static void DeleteEntry(string path, SafeFileHandle folder)
    {
        using var entry = OpenEntry(path, WindowsFileHandles.Delete, folder, link: true);
        if (entry is not null)
            WindowsFileHandles.MarkForDeletion(entry);
    }

    private static bool InFolder(SafeFileHandle entry, SafeFileHandle folder)
    {
        var parent = Path.GetDirectoryName(WindowsFileHandles.FinalPath(entry));
        return parent is not null && Path.TrimEndingDirectorySeparator(parent).Equals(
            Path.TrimEndingDirectorySeparator(WindowsFileHandles.FinalPath(folder)),
            StringComparison.OrdinalIgnoreCase);
    }

    // A folder that may not be used fails every save the same way, so it is said once.
    private void Report(string message)
    {
        if (_reported)
            return;

        _reported = true;
        Console.Error.WriteLine(message);
    }
}
