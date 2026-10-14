using System.Globalization;

namespace Winland.Core;

/// <summary>What the elevated helper should do to one window.</summary>
public enum ElevatedKind
{
    /// <summary>Move and size the window. Stacking stays as it is.</summary>
    Place,

    /// <summary>Close the window.</summary>
    Close,

    /// <summary>Bring the window to the top without moving it.</summary>
    Raise,

    /// <summary>Bring the window to the foreground.</summary>
    Focus,

    /// <summary>Write the Workspace the window is on onto the window, or take it off (ADR 0078).</summary>
    Mark,
}

/// <summary>One place, close, raise, focus, or mark.</summary>
/// <param name="Kind">Which act.</param>
/// <param name="Id">Window identity.</param>
/// <param name="X">Left edge, for a place. The mark, for a mark.</param>
/// <param name="Y">Top edge. Meaningful for a place.</param>
/// <param name="Width">Width in pixels. Meaningful for a place.</param>
/// <param name="Height">Height in pixels. Meaningful for a place.</param>
public readonly record struct ElevatedAct(ElevatedKind Kind, long Id, int X, int Y, int Width, int Height)
{
    /// <summary>A place. The caller records it on a book.</summary>
    /// <param name="id">Window identity.</param>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <returns>The place.</returns>
    public static ElevatedAct Place(long id, int x, int y, int width, int height) =>
        new(ElevatedKind.Place, id, x, y, width, height);

    /// <summary>A close. The caller records it on a book.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>The close.</returns>
    public static ElevatedAct Close(long id) => new(ElevatedKind.Close, id, 0, 0, 0, 0);

    /// <summary>A raise. The caller records it on a book.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>The raise.</returns>
    public static ElevatedAct Raise(long id) => new(ElevatedKind.Raise, id, 0, 0, 0, 0);

    /// <summary>A focus. The caller records it on a book.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>The focus.</returns>
    public static ElevatedAct Focus(long id) => new(ElevatedKind.Focus, id, 0, 0, 0, 0);

    /// <summary>A mark. The caller records it on a book.</summary>
    /// <param name="id">Window identity.</param>
    /// <param name="mark">A Workspace number, <see cref="WorkspaceMark.Scratchpad"/>, or <see cref="WorkspaceMark.None"/> to take the mark off.</param>
    /// <returns>The mark.</returns>
    public static ElevatedAct Mark(long id, int mark) => new(ElevatedKind.Mark, id, mark, 0, 0, 0);

    /// <summary>The mark a <see cref="ElevatedKind.Mark"/> writes. <see cref="WorkspaceMark.None"/> takes the mark off.</summary>
    public int Value => X;

    /// <summary>The single line a drain emits for this act.</summary>
    public string Line => Kind switch
    {
        ElevatedKind.Place => FormattableString.Invariant($"P {Id} {X} {Y} {Width} {Height}"),
        ElevatedKind.Close => FormattableString.Invariant($"C {Id}"),
        ElevatedKind.Raise => FormattableString.Invariant($"R {Id}"),
        ElevatedKind.Focus => FormattableString.Invariant($"F {Id}"),
        ElevatedKind.Mark => FormattableString.Invariant($"M {Id} {X}"),
        _ => "",
    };
}

/// <summary>
/// Pending place, mark, close, raise, and focus acts.
/// A close drops the other acts for that window and sticks until <see cref="Drain"/>.
/// The latest place replaces an earlier place, and the latest mark an earlier mark. One raise per window. One focus in the book.
/// Not thread-safe.
/// </summary>
public sealed class ElevatedBook
{
    private readonly List<ElevatedAct> _places = [];
    private readonly List<ElevatedAct> _marks = [];
    private readonly List<long> _closes = [];
    private readonly List<ElevatedAct> _stack = [];

    /// <summary>Records a place. The latest rectangle replaces an earlier one and keeps its slot.</summary>
    /// <param name="id">Window identity.</param>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <returns>True when that place is pending afterward.</returns>
    public bool Place(long id, int x, int y, int width, int height)
    {
        if (id <= 0 || width <= 0 || height <= 0 || _closes.Contains(id))
        {
            return false;
        }

        var place = ElevatedAct.Place(id, x, y, width, height);
        var index = _places.FindIndex(act => act.Id == id);
        if (index >= 0)
        {
            _places[index] = place;
        }
        else
        {
            _places.Add(place);
        }

        return true;
    }

    /// <summary>
    /// Records a mark for the helper to write: the desktop was refused, as it is on an administrator window (ADR 0078).
    /// The latest mark for a window replaces an earlier one and keeps its slot. The helper runs as administrator,
    /// so only what <see cref="WorkspaceMark"/> reads as a mark, or as none, is taken.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <param name="mark">A Workspace number, <see cref="WorkspaceMark.Scratchpad"/>, or <see cref="WorkspaceMark.None"/> to take the mark off.</param>
    /// <returns>True when that mark is pending afterward.</returns>
    public bool Mark(long id, int mark)
    {
        if (id <= 0 || WorkspaceMark.Read(mark) != mark || _closes.Contains(id))
        {
            return false;
        }

        var act = ElevatedAct.Mark(id, mark);
        var index = _marks.FindIndex(pending => pending.Id == id);
        if (index >= 0)
        {
            _marks[index] = act;
        }
        else
        {
            _marks.Add(act);
        }

        return true;
    }

    /// <summary>
    /// Records a close.
    /// It drops a pending place, mark, raise, and focus for this window, and a later act for this window is refused until <see cref="Drain"/>.
    /// A repeat stays in its original slot.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <returns>True when that close is pending afterward.</returns>
    public bool Close(long id)
    {
        if (id <= 0)
        {
            return false;
        }

        _places.RemoveAll(act => act.Id == id);
        _marks.RemoveAll(act => act.Id == id);
        _stack.RemoveAll(act => act.Id == id);
        if (_closes.Contains(id))
        {
            return true;
        }

        _closes.Add(id);
        return true;
    }

    /// <summary>Records a raise. One per window. Accepting it again makes it the newest stacking act.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>True when that raise is pending afterward.</returns>
    public bool Raise(long id)
    {
        if (id <= 0 || _closes.Contains(id))
        {
            return false;
        }

        _stack.RemoveAll(act => act.Kind == ElevatedKind.Raise && act.Id == id);
        _stack.Add(ElevatedAct.Raise(id));
        return true;
    }

    /// <summary>Records the single focus. The latest window replaces the earlier one and becomes the newest stacking act.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>True when that focus is pending afterward.</returns>
    public bool Focus(long id)
    {
        if (id <= 0 || _closes.Contains(id))
        {
            return false;
        }

        _stack.RemoveAll(act => act.Kind == ElevatedKind.Focus);
        _stack.Add(ElevatedAct.Focus(id));
        return true;
    }

    /// <summary>
    /// Returns the pending acts and clears the book.
    /// Places come first, in the order each window was first placed, then marks, in the order each window was first marked,
    /// then closes, in the order each close was first recorded,
    /// then raises and the focus, with the most recently accepted stacking act last.
    /// A close is the only act for its window.
    /// </summary>
    /// <returns>The acts to apply, or an empty list when nothing is pending.</returns>
    public IReadOnlyList<ElevatedAct> Drain()
    {
        var acts = new List<ElevatedAct>(_places.Count + _marks.Count + _closes.Count + _stack.Count);
        acts.AddRange(_places);
        acts.AddRange(_marks);
        foreach (var id in _closes)
        {
            acts.Add(ElevatedAct.Close(id));
        }

        acts.AddRange(_stack);
        _places.Clear();
        _marks.Clear();
        _closes.Clear();
        _stack.Clear();
        return acts;
    }

    /// <summary>Reads a line <see cref="Drain"/> emitted.</summary>
    /// <param name="line">One line.</param>
    /// <param name="act">The act, when the line is one this book emits.</param>
    /// <returns>False when the line is empty, the window identity is empty, a place has an empty size, or a mark is a number Winoma never writes.</returns>
    public static bool TryRead(string line, out ElevatedAct act)
    {
        act = default;
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        var parts = line.Split(' ');
        if (parts.Length < 2
            || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            || id <= 0)
        {
            return false;
        }

        switch (parts[0])
        {
            case "C" when parts.Length == 2:
                act = ElevatedAct.Close(id);
                return true;
            case "R" when parts.Length == 2:
                act = ElevatedAct.Raise(id);
                return true;
            case "F" when parts.Length == 2:
                act = ElevatedAct.Focus(id);
                return true;
            case "M" when parts.Length == 3
                && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var mark)
                && WorkspaceMark.Read(mark) == mark:
                act = ElevatedAct.Mark(id, mark);
                return true;
            case "P" when parts.Length == 6
                && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
                && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
                && int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
                && int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
                && width > 0
                && height > 0:
                act = ElevatedAct.Place(id, x, y, width, height);
                return true;
            default:
                return false;
        }
    }
}
