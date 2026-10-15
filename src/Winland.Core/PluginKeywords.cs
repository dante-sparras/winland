namespace Winland;

/// <summary>Config keywords registered by loaded plugins. The parser calls <see cref="TryApply"/>.</summary>
public static class PluginKeywords
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, Action<string, Action<string>>> Handlers = new(StringComparer.Ordinal);

    public static int Count
    {
        get
        {
            lock (Gate)
                return Handlers.Count;
        }
    }

    public static void Register(string name, Action<string, Action<string>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        lock (Gate)
        {
            if (!Handlers.TryAdd(name, handler))
                throw new InvalidOperationException($"Config keyword '{name}' is already registered.");
        }
    }

    public static void Unregister(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        lock (Gate)
            Handlers.Remove(name);
    }

    public static bool TryApply(string keyword, string value, out IReadOnlyList<string> warnings)
    {
        Action<string, Action<string>>? handler;
        lock (Gate)
        {
            if (string.IsNullOrEmpty(keyword) || !Handlers.TryGetValue(keyword, out handler))
            {
                warnings = [];
                return false;
            }
        }

        var found = new List<string>();
        handler(value, found.Add);
        warnings = found;
        return true;
    }
}
