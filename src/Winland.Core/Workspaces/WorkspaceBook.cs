namespace Winland.Core.Workspaces;

/// <summary>
/// Which Workspaces exist, which are in front, and which one has the keyboard. A Workspace is a number Winoma keeps (ADR 0003):
/// it exists while it holds a window or is in front on a monitor, so the numbers that exist may leave holes.
/// Each monitor has one Workspace in front (ADR 0079), and the one with the keyboard is among them.
/// The Bar lists the first four whether they exist or not.
/// </summary>
public sealed class WorkspaceBook
{
    /// <summary>The Workspaces the Bar always lists: 1 through this number.</summary>
    public const int Minimum = 4;

    /// <summary>The highest number chord.</summary>
    public const int Maximum = 9;

    // Indexed by Workspace number. Slot zero is never read.
    private readonly int[] _occupancy = new int[Maximum + 1];

    // The Workspaces in front on a monitor, indexed by Workspace number. The one with the keyboard is always one of them.
    private readonly bool[] _front = new bool[Maximum + 1];

    /// <summary>Starts with Workspace 1 in front, with the keyboard.</summary>
    public WorkspaceBook() => _front[1] = true;

    /// <summary>The Workspace with the keyboard, 1-based. It is in front on its monitor.</summary>
    public int Current { get; private set; } = 1;

    /// <summary>
    /// The Workspace that had the keyboard before the last change, on whichever monitor it was. The former-workspace chord goes to it.
    /// </summary>
    public int Former { get; private set; } = 1;

    /// <summary>True when <paramref name="number"/> is in front on a monitor: its windows are on the glass.</summary>
    /// <param name="number">Workspace number.</param>
    /// <returns>False for a Workspace Winoma keeps hidden, and for a number no chord reaches.</returns>
    public bool InFront(int number) => number >= 1 && number <= Maximum && _front[number];

    /// <summary>
    /// Says which Workspaces are in front, one for each monitor. The Workspace with the keyboard stays in front whatever is named.
    /// </summary>
    /// <param name="numbers">The Workspace in front on each monitor.</param>
    public void SetFront(IEnumerable<int> numbers)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        Array.Clear(_front);
        foreach (var number in numbers)
        {
            RequireChord(number);
            _front[number] = true;
        }

        _front[Current] = true;
        var count = 0;
        foreach (var front in _front)
        {
            count += front ? 1 : 0;
        }

        SeveralInFront = count > 1;
    }

    /// <summary>True while more than one Workspace is in front: there are two monitors or more.</summary>
    public bool SeveralInFront { get; private set; }

    /// <summary>How many windows <paramref name="number"/> holds.</summary>
    /// <param name="number">Workspace number, 1 through <see cref="Maximum"/>.</param>
    /// <returns>The count last recorded with <see cref="SetOccupancy"/>.</returns>
    public int Occupancy(int number)
    {
        RequireChord(number);
        return _occupancy[number];
    }

    /// <summary>Records how many windows <paramref name="number"/> holds.</summary>
    /// <param name="number">Workspace number, 1 through <see cref="Maximum"/>.</param>
    /// <param name="count">Its windows, shown or hidden: its tiles, and each Free window that stands alone on it.</param>
    public void SetOccupancy(int number, int count)
    {
        RequireChord(number);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _occupancy[number] = count;
    }

    /// <summary>True when <paramref name="number"/> holds a window or is in front on a monitor.</summary>
    /// <param name="number">Workspace number.</param>
    /// <returns>False for an empty Workspace that is in front on no monitor, and for a number no chord reaches.</returns>
    public bool Exists(int number) =>
        number >= 1 && number <= Maximum && (_front[number] || _occupancy[number] > 0);

    /// <summary>True when the Bar and <c>winoma desktop</c> list <paramref name="number"/>: one of the first four, or one that exists.</summary>
    /// <param name="number">Workspace number.</param>
    /// <returns>False for an empty Workspace above the fourth that is not in front.</returns>
    public bool Listed(int number) => number >= 1 && (number <= Minimum || Exists(number));

    /// <summary>
    /// Gives <paramref name="number"/> the keyboard, in front where the Workspace that had it was. A number that did not exist is made, and no other is.
    /// With more than one monitor the caller then says which Workspaces are in front (<see cref="SetFront"/>).
    /// </summary>
    /// <param name="number">Workspace number, 1 through <see cref="Maximum"/>.</param>
    /// <returns>The Workspace with the keyboard.</returns>
    public int Jump(int number)
    {
        RequireChord(number);
        if (Current != number)
        {
            _front[Current] = false;
            Former = Current;
            Current = number;
            _front[number] = true;
        }

        return Current;
    }

    /// <summary>The Workspace after the one with the keyboard among those that exist, on every monitor, and the lowest after the highest.</summary>
    /// <returns>The one with the keyboard when no other exists.</returns>
    public int Next()
    {
        for (var step = 1; step < Maximum; step++)
        {
            var number = ((Current - 1 + step) % Maximum) + 1;
            if (Exists(number))
            {
                return number;
            }
        }

        return Current;
    }

    /// <summary>The Workspace before the one with the keyboard among those that exist, on every monitor, and the highest before the lowest.</summary>
    /// <returns>The one with the keyboard when no other exists.</returns>
    public int Previous()
    {
        for (var step = 1; step < Maximum; step++)
        {
            var number = ((Current - 1 - step + Maximum) % Maximum) + 1;
            if (Exists(number))
            {
                return number;
            }
        }

        return Current;
    }

    private static void RequireChord(int number)
    {
        if (number < 1 || number > Maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, "Workspace chords run from 1 through 9.");
        }
    }
}
