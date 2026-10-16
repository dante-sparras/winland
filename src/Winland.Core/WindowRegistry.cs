namespace Winland;

/// <summary>
/// Forgets a window in every piece of per-window state it tracks. The core registers each map, set and
/// list it keys by window, and each field that names a window, so closing a window clears them all in
/// one call.
/// </summary>
internal sealed class WindowRegistry
{
    private readonly List<Action<WindowId>> _forgetters = [];

    public void Track<T>(Dictionary<WindowId, T> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _forgetters.Add(window => map.Remove(window));
    }

    public void Track(HashSet<WindowId> set)
    {
        ArgumentNullException.ThrowIfNull(set);
        _forgetters.Add(window => set.Remove(window));
    }

    public void Track(List<WindowId> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        _forgetters.Add(window => list.RemoveAll(item => item == window));
    }

    /// <summary>Registers state that is not a collection, such as a field that names one window.</summary>
    public void Track(Action<WindowId> forget)
    {
        ArgumentNullException.ThrowIfNull(forget);
        _forgetters.Add(forget);
    }

    public void Forget(WindowId window)
    {
        foreach (var forget in _forgetters)
            forget(window);
    }
}
