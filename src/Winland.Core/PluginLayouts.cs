namespace Winland;

/// <summary>
/// Tiled layouts a loaded plugin registered. <c>general:layout</c> and a workspace rule select one by the name the
/// plugin registered, and <see cref="LayoutFactory"/> builds the <see cref="ScriptedLayout"/> that asks it for boxes.
/// </summary>
public static class PluginLayouts
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, ILayoutScripts> Scripts = new(StringComparer.Ordinal);

    public static bool Contains(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        lock (Gate)
            return Scripts.ContainsKey(name);
    }

    public static IReadOnlyList<string> Names()
    {
        lock (Gate)
            return [.. Scripts.Keys];
    }

    /// <summary>
    /// Registers <paramref name="name"/>. False when the name is empty, starts with <c>lua:</c>, is a built-in layout
    /// or <c>default</c>, or is already registered, ignoring case for those reserved and taken names. The stored name
    /// is exact: selection matches it character for character.
    /// </summary>
    public static bool Register(string name, ILayoutScripts scripts, out string reason)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        if (string.IsNullOrWhiteSpace(name))
        {
            reason = "The layout name is empty.";
            return false;
        }

        if (name.StartsWith(LayoutRequest.Prefix, StringComparison.Ordinal))
        {
            reason = "Layout names starting with lua: belong to a Lua layout.";
            return false;
        }

        if (Reserved(name))
        {
            reason = $"Layout '{name}' is reserved.";
            return false;
        }

        lock (Gate)
        {
            foreach (var taken in Scripts.Keys)
            {
                if (taken.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"Layout '{name}' is already registered.";
                    return false;
                }
            }

            Scripts.Add(name, scripts);
        }

        reason = "";
        return true;
    }

    public static void Unregister(string name)
    {
        if (string.IsNullOrEmpty(name))
            return;

        lock (Gate)
            Scripts.Remove(name);
    }

    internal static bool TryScripts(string name, out ILayoutScripts scripts)
    {
        lock (Gate)
            return Scripts.TryGetValue(name, out scripts!);
    }

    // Built-in tiled layouts and the floating layout, which the pinned release also refuses as a plugin name.
    private static bool Reserved(string name)
    {
        foreach (var built in LayoutFactory.Names)
        {
            if (built.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return name.Equals("default", StringComparison.OrdinalIgnoreCase);
    }
}
