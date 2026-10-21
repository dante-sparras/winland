using System.Globalization;
using static Winland.Domain.OrdinalText;

namespace Winland.Domain;

internal sealed partial class MasterLayout
{
    public IReadOnlyList<Placement> LayoutMessage(WindowId? active, string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        LayoutMessageError = null;
        LayoutMessageFocus = null;
        var tokens = Tokens(arguments);
        if (tokens.Length == 0)
            return Fail("error: bad layoutmsg");

        var command = tokens[0];
        if (Is(command, "swapwithmaster"))
            return SwapWithMaster(active, tokens);
        if (Is(command, "focusmaster"))
            return FocusMaster(active, tokens);
        if (Is(command, "cyclenext"))
            return CycleFocus(active, next: true, Loop(tokens));
        if (Is(command, "cycleprev"))
            return CycleFocus(active, next: false, Loop(tokens));
        if (Is(command, "swapnext"))
            return SwapToward(active, next: true, Loop(tokens));
        if (Is(command, "swapprev"))
            return SwapToward(active, next: false, Loop(tokens));
        if (Is(command, "addmaster"))
            return AddMaster(active);
        if (Is(command, "removemaster"))
            return RemoveMaster(active);
        if (Is(command, "orientationleft"))
            return Orient(active, MasterOrientation.Left);
        if (Is(command, "orientationright"))
            return Orient(active, MasterOrientation.Right);
        if (Is(command, "orientationtop"))
            return Orient(active, MasterOrientation.Top);
        if (Is(command, "orientationbottom"))
            return Orient(active, MasterOrientation.Bottom);
        if (Is(command, "orientationcenter"))
            return Orient(active, MasterOrientation.Center);
        if (Is(command, "orientationnext"))
            return CycleOrientation(active, 1, null);
        if (Is(command, "orientationprev"))
            return CycleOrientation(active, -1, null);
        if (Is(command, "orientationcycle"))
            return CycleOrientation(active, 1, CycleList(tokens));
        if (Is(command, "mfact"))
            return ChangeFact(active, tokens);
        if (Is(command, "rollnext"))
            return Roll(active, next: true);
        if (Is(command, "rollprev"))
            return Roll(active, next: false);

        return Fail("error: bad layoutmsg '" + command + "'");
    }

    private IReadOnlyList<Placement> SwapWithMaster(WindowId? active, string[] tokens)
    {
        if (active is not WindowId window)
            return Fail("error: no focused window");
        if (Floating(window))
            return Fail("error: focused window is not tiled");

        var tile = Find(window);
        var master = FirstMaster();
        if (tile is null || master is null)
            return Fail("error: no master");

        var ignore = false;
        foreach (var token in tokens)
        {
            if (Is(token, "ignoremaster"))
                ignore = true;
        }

        var child = tokens.Length >= 2 && Is(tokens[1], "child");
        var keepMaster = tokens.Length >= 2 && Is(tokens[1], "master");
        if (!ReferenceEquals(tile, master))
        {
            var masterWindow = WindowOf(master);
            SwapIds(master, tile);
            RequestFocus(child ? masterWindow : window);
        }
        else if (!ignore)
        {
            var slave = FirstSlave();
            if (slave is null)
                return Placed();

            var masterWindow = WindowOf(master);
            var slaveWindow = WindowOf(slave);
            SwapIds(master, slave);
            RequestFocus(keepMaster ? slaveWindow : masterWindow);
        }

        Recalculate();
        return Placed();
    }

    private IReadOnlyList<Placement> FocusMaster(WindowId? active, string[] tokens)
    {
        if (active is not WindowId window)
            return Fail("error: no focused window");

        var master = FirstMaster();
        if (master is null)
            return Fail("error: no master");

        var arg = tokens.Length >= 2 ? tokens[1] : "";
        var masterWindow = WindowOf(master);
        if (masterWindow != window)
        {
            if (Is(arg, "previous"))
                _previousFocus = window;

            RequestFocus(masterWindow);
            return Placed();
        }

        if (Is(arg, "master"))
            return Placed();

        if (Is(arg, "previous") && _previousFocus is WindowId previous && Find(previous) is not null && previous != window)
        {
            RequestFocus(previous);
            return Placed();
        }

        var slave = FirstSlave();
        if (slave is not null)
            RequestFocus(WindowOf(slave));

        return Placed();
    }

    private IReadOnlyList<Placement> CycleFocus(WindowId? active, bool next, bool loop)
    {
        if (active is not WindowId window)
            return Fail("error: no focused window");
        if (Floating(window))
            return Placed();

        var tile = Find(window);
        if (tile is null)
            return Fail("error: no focused window");

        var other = NextTile(tile, next, loop);
        if (other is not null)
            RequestFocus(WindowOf(other));

        return Placed();
    }

    private IReadOnlyList<Placement> SwapToward(WindowId? active, bool next, bool loop)
    {
        if (active is not WindowId window)
            return Fail("error: no focused window");
        if (Floating(window))
            return Placed();

        var tile = Find(window);
        if (tile is null)
            return Fail("error: no focused window");

        var other = NextTile(tile, next, loop);
        if (other is null)
            return Placed();

        SwapIds(tile, other);
        Recalculate();
        return Placed();
    }

    private IReadOnlyList<Placement> AddMaster(WindowId? active)
    {
        if (active is not WindowId window)
            return Fail("error: no focused window");
        if (Floating(window))
            return Fail("error: window is floating");

        var windows = _tiles.Count;
        var masters = MasterCount();
        if (masters + 2 > windows && !_master.AllowSmallSplit)
            return Fail("error: nothing to do");

        var tile = Find(window);
        if (tile is null || tile.Master)
        {
            foreach (var node in _tiles)
            {
                if (!node.Master)
                {
                    node.Master = true;
                    break;
                }
            }
        }
        else
        {
            tile.Master = true;
        }

        Recalculate();
        return Placed();
    }

    private IReadOnlyList<Placement> RemoveMaster(WindowId? active)
    {
        if (active is not WindowId window)
            return Fail("error: no focused window");
        if (Floating(window))
            return Fail("error: window is not tiled");

        if (_tiles.Count < 2 || MasterCount() < 2)
            return Fail("error: nothing to do");

        var tile = Find(window);
        if (tile is null || !tile.Master)
        {
            for (var i = _tiles.Count - 1; i >= 0; i--)
            {
                if (_tiles[i].Master)
                {
                    _tiles[i].Master = false;
                    break;
                }
            }
        }
        else
        {
            tile.Master = false;
        }

        Recalculate();
        return Placed();
    }

    private IReadOnlyList<Placement> Orient(WindowId? active, MasterOrientation orientation)
    {
        if (active is null)
            return Fail("error: no focused window");

        _explicit = orientation;
        Recalculate();
        return Placed();
    }

    private IReadOnlyList<Placement> CycleOrientation(WindowId? active, int step, IReadOnlyList<MasterOrientation>? custom)
    {
        if (active is null)
            return Fail("error: no focused window");

        var cycle = custom is { Count: > 0 }
            ? custom
            : new[]
            {
                MasterOrientation.Left,
                MasterOrientation.Top,
                MasterOrientation.Right,
                MasterOrientation.Bottom,
                MasterOrientation.Center,
            };

        var index = 0;
        var current = Dynamic();
        for (var i = 0; i < cycle.Count; i++)
        {
            if (cycle[i] == current)
            {
                index = i + step;
                break;
            }
        }

        if (index >= cycle.Count)
            index %= cycle.Count;
        else if (index < 0)
            index = cycle.Count + (index % cycle.Count);

        if (index < 0 || index >= cycle.Count)
            index = 0;

        _explicit = cycle[index];
        Recalculate();
        return Placed();
    }

    private IReadOnlyList<Placement> ChangeFact(WindowId? active, string[] tokens)
    {
        if (active is null)
            return Fail("error: no focused window");

        var exact = tokens.Length >= 2 && Is(tokens[1], "exact");
        var text = exact ? tokens.Length >= 3 ? tokens[2] : "" : tokens.Length >= 2 ? tokens[1] : "";
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio) || !double.IsFinite(ratio))
            return Fail("error: bad ratio");

        var master = FirstMaster();
        if (master is null)
            return Fail("error: no master");

        master.MasterFraction = Math.Clamp(exact ? ratio : master.MasterFraction + ratio, 0.05, 0.95);
        Recalculate();
        return Placed();
    }

    private IReadOnlyList<Placement> Roll(WindowId? active, bool next)
    {
        if (active is not WindowId window)
            return Fail("error: no focused window");

        var tile = Find(window);
        if (tile is null)
            return Fail("error: window couldnt be found");

        var oldMaster = tile.Master ? tile : FirstMaster();
        if (oldMaster is null)
            return Fail("error: no master");

        Tile? newer = null;
        if (next)
        {
            foreach (var node in _tiles)
            {
                if (!node.Master)
                {
                    newer = node;
                    break;
                }
            }
        }
        else
        {
            for (var i = _tiles.Count - 1; i >= 0; i--)
            {
                if (!_tiles[i].Master)
                {
                    newer = _tiles[i];
                    break;
                }
            }
        }

        if (newer is null)
            return Placed();

        var oldIndex = _tiles.IndexOf(oldMaster);
        newer.Master = true;
        var newIndex = _tiles.IndexOf(newer);
        if (newIndex < oldIndex)
            Rotate(_tiles, newIndex, newIndex + 1, oldIndex);
        else if (newIndex > oldIndex)
            Rotate(_tiles, oldIndex, newIndex, newIndex + 1);

        oldMaster.Master = false;
        oldIndex = _tiles.IndexOf(oldMaster);
        if (next)
        {
            if (oldIndex >= 0 && oldIndex < _tiles.Count)
                Rotate(_tiles, oldIndex, oldIndex + 1, _tiles.Count);
        }
        else if (oldIndex > 0)
        {
            Rotate(_tiles, 0, oldIndex, oldIndex + 1);
        }

        Recalculate();
        RequestFocus(WindowOf(newer));
        return Placed();
    }

    private Tile? NextTile(Tile current, bool next, bool loop)
    {
        var order = new List<Tile>(_tiles);
        if (!next)
            order.Reverse();

        var start = order.IndexOf(current);
        Tile? candidate = null;
        for (var i = Math.Max(0, start + 1); i < order.Count; i++)
        {
            if (order[i].Master == current.Master)
            {
                candidate = order[i];
                break;
            }
        }

        if (candidate is null)
        {
            foreach (var other in order)
            {
                if (!ReferenceEquals(other, current) && other.Master != current.Master)
                {
                    candidate = other;
                    break;
                }
            }
        }

        if (candidate is not null && !loop)
        {
            if (candidate.Master && next)
                return null;
            if (!candidate.Master && current.Master && !next)
                return null;
        }

        return candidate;
    }

    private static IReadOnlyList<MasterOrientation> CycleList(string[] tokens)
    {
        var cycle = new List<MasterOrientation>();
        for (var i = 1; i < tokens.Length; i++)
        {
            if (TryWord(tokens[i], out var orientation))
                cycle.Add(orientation);
        }

        return cycle;
    }

    private static bool TryWord(string text, out MasterOrientation orientation)
    {
        if (Is(text, "top"))
            orientation = MasterOrientation.Top;
        else if (Is(text, "right"))
            orientation = MasterOrientation.Right;
        else if (Is(text, "bottom"))
            orientation = MasterOrientation.Bottom;
        else if (Is(text, "left"))
            orientation = MasterOrientation.Left;
        else if (Is(text, "center"))
            orientation = MasterOrientation.Center;
        else
        {
            orientation = MasterOrientation.Left;
            return false;
        }

        return true;
    }

    private static bool Loop(string[] tokens) =>
        tokens.Length < 2 || !Is(tokens[1], "noloop");

    private bool Floating(WindowId window) =>
        _floating.Contains(Id(window));

    private void RequestFocus(WindowId window)
    {
        _focused = window;
        LayoutMessageFocus = window;
    }

    private IReadOnlyList<Placement> Fail(string error)
    {
        LayoutMessageError = error;
        return [];
    }

    private WindowId? _previousFocus;

    private static string[] Tokens(string arguments) =>
        arguments.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
}
