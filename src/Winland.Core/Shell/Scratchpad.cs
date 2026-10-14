namespace Winland.Core.Shell;

/// <summary>Windows hidden off every numbered workspace until the scratchpad is shown.</summary>
public sealed class Scratchpad
{
    private readonly List<long> _windows = [];

    /// <summary>True while the scratchpad is on screen.</summary>
    public bool Open { get; private set; }

    /// <summary>
    /// True while the scratchpad is shown with two or more windows, which share the whole work area (<see cref="Winland.Core.Tiling.OverlayTiles.Cover"/>).
    /// One window is a centered panel, and the tiles show around it.
    /// </summary>
    public bool CoversTiles => Open && _windows.Count > 1;

    /// <summary>Windows in the scratchpad, in the order they were sent.</summary>
    public IReadOnlyList<long> Windows => _windows;

    /// <summary>True when <paramref name="id"/> is in the scratchpad.</summary>
    public bool Contains(long id) => _windows.Contains(id);

    /// <summary>Adds <paramref name="id"/>. A window already there stays.</summary>
    /// <returns>True when the window was added.</returns>
    public bool Add(long id)
    {
        if (_windows.Contains(id))
        {
            return false;
        }

        _windows.Add(id);
        return true;
    }

    /// <summary>Drops <paramref name="id"/>.</summary>
    /// <returns>True when the window was in the scratchpad.</returns>
    public bool Remove(long id) => _windows.Remove(id);

    /// <summary>Shows the scratchpad, or hides it when it was already shown.</summary>
    /// <returns>True when the scratchpad is now shown.</returns>
    public bool Toggle()
    {
        Open = !Open;
        return Open;
    }
}
