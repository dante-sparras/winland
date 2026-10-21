namespace Winland;

/// <summary>
/// A Group: several Clients that share one tile. Only the shown member is in the Workspace's Layout.
/// The others wait hidden on the same Workspace until they are shown.
/// </summary>
internal sealed class WindowGroup(WindowId first)
{
    public List<WindowId> Members { get; } = [first];

    /// <summary>The index of the shown member.</summary>
    public int Current { get; set; }

    public bool Locked { get; set; }

    /// <summary>Set when no other window may join this Group.</summary>
    public bool Denied { get; set; }

    public WindowId Shown => Members[Current];

    public int Count => Members.Count;

    public bool Contains(WindowId window) => Members.Contains(window);

    /// <summary>The index after removing the member at <paramref name="index"/>, as Winland computes it.</summary>
    public int CurrentAfterRemoving(int index)
    {
        var current = Current;
        if ((current >= index && index != 0) || (current >= Members.Count - 1 && current > 0))
            current--;

        return current;
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
