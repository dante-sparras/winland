namespace Winland.Domain;

/// <summary>
/// A Group: several Clients that share one tile. Only the shown member is in the Workspace's Layout.
/// The others wait hidden on the same Workspace until they are shown.
/// </summary>
internal sealed class WindowGroup(WindowId first)
{
    private readonly List<WindowId> _members = [first];

    public IReadOnlyList<WindowId> Members => _members;

    /// <summary>The index of the shown member. It always points at a member.</summary>
    public int Current { get; private set; }

    public WindowId Shown => _members[Current];

    public int Count => _members.Count;

    public bool Contains(WindowId window) => _members.Contains(window);

    public int IndexOf(WindowId window) => _members.IndexOf(window);

    /// <summary>
    /// The index after removing the member at <paramref name="index"/>. A hidden member leaves the shown one
    /// shown. When the shown member leaves, the one before it takes the tile, or the next one when it was first.
    /// </summary>
    public int CurrentAfterRemoving(int index)
    {
        if (index != Current)
            return index < Current ? Current - 1 : Current;

        var current = Current;
        if ((current >= index && index != 0) || (current >= _members.Count - 1 && current > 0))
            current--;

        return current;
    }

    /// <summary>Adds <paramref name="window"/> at <paramref name="position"/>, clamped to the Group. The shown member stays shown.</summary>
    public void Insert(int position, WindowId window)
    {
        position = Math.Clamp(position, 0, _members.Count);
        _members.Insert(position, window);
        if (position <= Current)
            Current++;
    }

    /// <summary>Makes the member at <paramref name="index"/> the shown one.</summary>
    public void ShowAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _members.Count);
        Current = index;
    }

    /// <summary>Removes the member at <paramref name="index"/>. The shown index moves as <see cref="CurrentAfterRemoving"/> says.</summary>
    public void RemoveAt(int index)
    {
        var current = CurrentAfterRemoving(index);
        _members.RemoveAt(index);
        Current = current;
    }

    /// <summary>Puts <paramref name="window"/> in the place of the member at <paramref name="index"/>.</summary>
    public void Replace(int index, WindowId window) => _members[index] = window;

    /// <summary>Swaps the shown member with the one at <paramref name="index"/>, which stays shown in its new place.</summary>
    public void MoveShownTo(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _members.Count);
        (_members[Current], _members[index]) = (_members[index], _members[Current]);
        Current = index;
    }
}

/// <summary>How a window left its Group.</summary>
internal enum GroupExit
{
    /// <summary>The window was not a member.</summary>
    NotMember,

    /// <summary>It was the last member. The Group ended and the window keeps its tile.</summary>
    Dissolved,

    /// <summary>It left a Group that keeps other members. It is out of the Layout now.</summary>
    Detached,
}
