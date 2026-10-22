namespace Winland.Install;

public sealed record ListedFile(string RelativePath, bool ReparsePoint);

public sealed record Shortcut(string LinkPath, string Target, string Arguments, string AppUserModelId);

public enum RegistryRoot
{
    Machine,
    User,
}

/// <summary>The files an install copies and removes. A link is reported and never followed.</summary>
public interface IFileTree
{
    bool Exists(string path);

    bool DirectoryExists(string path);

    IReadOnlyList<ListedFile> List(string directory);

    void PrepareDirectory(string directory);

    void Copy(string source, string destination);

    void DeleteTree(string directory);
}

/// <summary>The registry values an install writes. <c>PATH</c> is stored expandable, so it is not expanded on the way through.</summary>
public interface IRegistry
{
    string? GetString(RegistryRoot root, string key, string name);

    void SetString(RegistryRoot root, string key, string name, string value, bool expandable);

    void SetDword(RegistryRoot root, string key, string name, int value);

    void DeleteKey(RegistryRoot root, string key);

    void DeleteValue(RegistryRoot root, string key, string name);
}

public interface IStartMenu
{
    void Place(Shortcut shortcut);

    void Remove(string linkPath);
}

/// <summary>Stops the running Daemon and starts the installed one.</summary>
public interface IDaemonSession
{
    void Stop();

    void Start(string executable, IReadOnlyList<string> arguments);
}
