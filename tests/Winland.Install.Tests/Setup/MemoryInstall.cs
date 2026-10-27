namespace Winland.Install.Tests;

sealed class MemoryFileTree : IFileTree
{
    private readonly Dictionary<string, byte[]> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> links = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);

    public void Seed(string path, byte[] content, bool reparsePoint = false)
    {
        var full = Full(path);
        files[full] = content;
        if (reparsePoint)
            links.Add(full);

        var directory = Path.GetDirectoryName(full);
        if (directory is not null)
            directories.Add(directory);
    }

    public bool Exists(string path) => files.ContainsKey(Full(path));

    public bool DirectoryExists(string path)
    {
        var directory = Full(path);
        if (directories.Contains(directory))
            return true;

        var prefix = directory + Path.DirectorySeparatorChar;
        return files.Keys.Any(file => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ListedFile> List(string directory)
    {
        var root = Full(directory);
        var prefix = root + Path.DirectorySeparatorChar;
        return files.Keys
            .Where(file => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(file => new ListedFile(file[prefix.Length..], links.Contains(file)))
            .ToList();
    }

    public void PrepareDirectory(string directory)
    {
        var full = Full(directory);
        if (links.Contains(full))
            throw new InvalidOperationException($"Refusing to install into '{directory}': it is a link.");

        directories.Add(full);
    }

    public void Copy(string source, string destination)
    {
        var from = Full(source);
        if (links.Contains(from))
            throw new InvalidOperationException($"Refusing to install '{source}': it is a link.");

        var to = Full(destination);
        files[to] = files[from];
        links.Remove(to);
        var parent = Path.GetDirectoryName(to);
        if (parent is not null)
            directories.Add(parent);
    }

    public void DeleteFile(string path)
    {
        var full = Full(path);
        files.Remove(full);
        links.Remove(full);
    }

    public void DeleteTree(string directory)
    {
        var root = Full(directory);
        var prefix = root + Path.DirectorySeparatorChar;
        foreach (var file in files.Keys.Where(file => file.Equals(root, StringComparison.OrdinalIgnoreCase) || file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            files.Remove(file);
            links.Remove(file);
        }

        foreach (var entry in directories.Where(entry => entry.Equals(root, StringComparison.OrdinalIgnoreCase) || entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            directories.Remove(entry);
    }

    public byte[] Read(string path) => files[Full(path)];

    private static string Full(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}

sealed class MemoryRegistry : IRegistry
{
    private readonly Dictionary<(RegistryRoot Root, string Key, string Name), RegistryEntry> values = new();

    public IReadOnlyDictionary<(RegistryRoot Root, string Key, string Name), RegistryEntry> Values => values;

    public string? GetString(RegistryRoot root, string key, string name) =>
        values.TryGetValue(KeyOf(root, key, name), out var entry) ? entry.Text : null;

    public void SetString(RegistryRoot root, string key, string name, string value, bool expandable) =>
        values[KeyOf(root, key, name)] = new RegistryEntry(value, expandable, Dword: null);

    public void SetDword(RegistryRoot root, string key, string name, int value) =>
        values[KeyOf(root, key, name)] = new RegistryEntry(null, Expandable: false, value);

    public void DeleteKey(RegistryRoot root, string key)
    {
        var prefix = key + @"\";
        foreach (var entry in values.Keys.Where(entry => entry.Root == root && (entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase) || entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))).ToList())
            values.Remove(entry);
    }

    public void DeleteValue(RegistryRoot root, string key, string name) =>
        values.Remove(KeyOf(root, key, name));

    private static (RegistryRoot Root, string Key, string Name) KeyOf(RegistryRoot root, string key, string name) =>
        (root, key, name);

    public readonly record struct RegistryEntry(string? Text, bool Expandable, int? Dword);
}

sealed class MemoryStartMenu : IStartMenu
{
    private readonly Dictionary<string, Shortcut> shortcuts = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<Shortcut> Shortcuts => shortcuts.Values;

    public void Place(Shortcut shortcut) => shortcuts[shortcut.LinkPath] = shortcut;

    public void Remove(string linkPath) => shortcuts.Remove(linkPath);
}

sealed class MemorySession : IDaemonSession
{
    private readonly List<string> operations = [];

    public IReadOnlyList<string> Operations => operations;

    public string? Started { get; private set; }

    public IReadOnlyList<string> StartedArguments { get; private set; } = [];

    /// <summary>Whether a Daemon is running for the next <see cref="Stop"/> to find.</summary>
    public bool Running { get; set; } = true;

    public bool Stop()
    {
        operations.Add("stop");
        var was = Running;
        Running = false;
        return was;
    }

    public void Start(string executable, IReadOnlyList<string> arguments)
    {
        operations.Add("start");
        Running = true;
        Started = executable;
        StartedArguments = arguments;
    }
}
