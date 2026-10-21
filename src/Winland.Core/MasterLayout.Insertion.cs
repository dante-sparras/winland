namespace Winland;

internal sealed partial class MasterLayout
{
    /// <summary>
    /// Tiles a Client. False, with the tile taken back out, when its maximum size cannot fill the share
    /// of the Monitor its role gives it; the caller floats it instead.
    /// </summary>
    private bool Adopt(Tile tile, bool firstMap, bool dragging, Point? pointer)
    {
        var centeredBefore = Dynamic() == MasterOrientation.Center && SlaveCount() >= _master.SlaveCountForCenter;
        var centerCursor = dragging && _master.DropAtCursor && centeredBefore;
        var dragOntoMaster = false;
        if (dragging && pointer is Point point && (_tiles.Count > 1 || !_master.DropAtCursor) && !centerCursor
            && Closest(point) is { Master: true })
            dragOntoMaster = true;

        // A moved window keeps its slot: new_status = master only applies to a window that opens.
        PlaceNew(tile, dragOntoMaster || (!dragging && _master.NewStatus == MasterNewStatus.Master));

        var forceMaster = dragOntoMaster;
        if (dragging && pointer is Point cursor && _master.DropAtCursor)
            forceMaster |= DropAtCursor(tile, cursor, centeredBefore);

        AssignRole(tile, firstMap, dragging, forceMaster);
        if (CannotFill(tile))
        {
            Detach(tile);
            return false;
        }

        Recalculate();
        return true;
    }

    // The share is measured on the whole Monitor, as wide as the master fraction for a master and
    // the rest for a slave, and as tall as the Monitor split evenly over the slaves.
    private bool CannotFill(Tile tile)
    {
        if (!_limits.TryGetValue(tile.Id, out var limits) || limits.Max is not { } max)
            return false;

        var monitor = _monitor.Width > 0 && _monitor.Height > 0 ? _monitor : _area;
        if (tile.Master)
            return max.Width < monitor.Width * tile.MasterFraction || max.Height < monitor.Height;

        return max.Width < monitor.Width * (1 - tile.MasterFraction)
            || max.Height < monitor.Height / (double)(_tiles.Count - 1);
    }

    // The size Winland gives a window that opens: the whole Monitor on an empty Workspace, the master's
    // with new_status master, half the Monitor beside a lone master, or a slave's share otherwise.
    private Rectangle? PredictedTile()
    {
        var monitor = _monitor.Width > 0 && _monitor.Height > 0 ? _monitor : _area;
        if (_tiles.Count == 0)
            return new Rectangle(0, 0, monitor.Width, monitor.Height);

        if (_tiles.Find(static tile => tile.Master) is not Tile master)
            return null;

        if (_master.NewStatus == MasterNewStatus.Master)
            return new Rectangle(0, 0, master.Box.Width, master.Box.Height);

        var slaves = _tiles.Count - MasterCount();
        return slaves == 0
            ? new Rectangle(0, 0, monitor.Width / 2, monitor.Height)
            : new Rectangle(0, 0, monitor.Width - master.Box.Width, monitor.Height / (slaves + 1));
    }

    private void PlaceNew(Tile tile, bool becomesMaster)
    {
        if (_master.NewOnActive != MasterNewOnActive.None && !becomesMaster)
        {
            var focus = Find(_focused);
            if (focus is not null && !(focus.Master && (MasterCount() == 1 || _master.NewStatus == MasterNewStatus.Slave)))
            {
                var index = _tiles.IndexOf(focus);
                if (_master.NewOnActive != MasterNewOnActive.Before)
                    index++;

                _tiles.Insert(Math.Clamp(index, 0, _tiles.Count), tile);
                return;
            }
        }

        if (_master.NewOnTop)
            _tiles.Insert(0, tile);
        else
            _tiles.Add(tile);
    }

    private bool DropAtCursor(Tile tile, Point pointer, bool centeredBefore)
    {
        if (centeredBefore && TryCenterDrop(tile, pointer, out var force))
            return force;

        if (_tiles.Count > 2)
        {
            ReorderToCursor(tile, pointer);
            return false;
        }

        return _tiles.Count == 2 && CursorOnMasterSide(pointer);
    }

    private bool TryCenterDrop(Tile tile, Point pointer, out bool forceMaster)
    {
        forceMaster = false;
        var master = FirstMaster();
        if (master is null || ReferenceEquals(master, tile))
            return false;

        var box = master.Box;
        if (pointer.X >= box.X && pointer.X <= box.X + box.Width)
        {
            forceMaster = true;
            return true;
        }

        var dropRight = pointer.X > box.X + (box.Width / 2.0);
        var firstSideRight = _master.CenterFallback == MasterOrientation.Right;
        _tiles.Remove(tile);

        var slot = 0;
        var slaves = 0;
        foreach (var node in _tiles)
        {
            if (node.Master)
                continue;

            var nodeRight = (slaves % 2 == 0) == firstSideRight;
            if (nodeRight == dropRight && pointer.Y > node.Box.Y + (node.Box.Height / 2.0))
                slot++;

            slaves++;
        }

        var targetFirst = dropRight == firstSideRight;
        var totalSlaves = slaves + 1;
        var cap = targetFirst ? (totalSlaves + 1) / 2 : totalSlaves / 2;
        if (cap > 0)
            slot = Math.Min(slot, cap - 1);

        var slavePos = cap == 0 ? 0 : targetFirst ? (2 * slot) : (2 * slot) + 1;
        var insert = _tiles.Count;
        var counted = 0;
        for (var i = 0; i < _tiles.Count; i++)
        {
            if (counted == slavePos)
            {
                insert = i;
                break;
            }

            if (!_tiles[i].Master)
                counted++;
        }

        _tiles.Insert(insert, tile);
        return true;
    }

    private void ReorderToCursor(Tile tile, Point pointer)
    {
        var source = _tiles.IndexOf(tile);
        if (source < 0)
            return;

        var orientation = Dynamic();
        for (var i = 0; i < _tiles.Count; i++)
        {
            if (ReferenceEquals(_tiles[i], tile) || !Contains(_tiles[i].Box, pointer))
                continue;

            var insert = i;
            if (orientation is MasterOrientation.Left or MasterOrientation.Right)
            {
                if (pointer.Y > _tiles[i].Box.Y + (_tiles[i].Box.Height / 2.0))
                    insert++;
            }
            else if (orientation is MasterOrientation.Top or MasterOrientation.Bottom)
            {
                if (pointer.X > _tiles[i].Box.X + (_tiles[i].Box.Width / 2.0))
                    insert++;
            }

            if (insert > source)
                insert--;
            if (insert == source)
                return;

            _tiles.RemoveAt(source);
            _tiles.Insert(Math.Clamp(insert, 0, _tiles.Count), tile);
            return;
        }
    }

    private bool CursorOnMasterSide(Point pointer)
    {
        var master = FirstMaster();
        if (master is null)
            return false;

        var box = master.Box;
        var midX = box.X + (box.Width / 2.0);
        var midY = box.Y + (box.Height / 2.0);
        return Dynamic() switch
        {
            MasterOrientation.Right => pointer.X > midX,
            MasterOrientation.Top => pointer.Y < midY,
            MasterOrientation.Bottom => pointer.Y > midY,
            _ => pointer.X < midX,
        };
    }

    private void AssignRole(Tile tile, bool firstMap, bool dragging, bool forceMaster)
    {
        var fraction = _master.Mfact;
        var focused = Find(_focused);
        var opening = focused ?? FirstMaster();
        var makeMaster = forceMaster
            || (!dragging && _master.NewStatus == MasterNewStatus.Master)
            || _tiles.Count == 1
            || (_tiles.Count > 2 && !firstMap && focused is { Master: true } && !ReferenceEquals(focused, tile))
            || (_master.NewStatus == MasterNewStatus.Inherit && !dragging && opening is { Master: true } && !ReferenceEquals(opening, tile));

        if (!makeMaster)
        {
            tile.Master = false;
            tile.MasterFraction = fraction;
            return;
        }

        if (_master.NewOnActive == MasterNewOnActive.Before)
        {
            for (var i = _tiles.Count - 1; i >= 0; i--)
            {
                if (_tiles[i].Master && !ReferenceEquals(_tiles[i], tile))
                {
                    _tiles[i].Master = false;
                    fraction = _tiles[i].MasterFraction;
                    break;
                }
            }
        }
        else
        {
            foreach (var node in _tiles)
            {
                if (node.Master && !ReferenceEquals(node, tile))
                {
                    node.Master = false;
                    fraction = node.MasterFraction;
                    break;
                }
            }
        }

        tile.Master = true;
        tile.MasterFraction = fraction;
    }

    private void Detach(Tile tile)
    {
        var masters = MasterCount();
        if (tile.Master && (masters <= 1 || _master.AllowSmallSplit))
        {
            foreach (var other in _tiles)
            {
                if (!other.Master)
                {
                    other.Master = true;
                    other.MasterFraction = tile.MasterFraction;
                    break;
                }
            }
        }

        _tiles.Remove(tile);
        if (MasterCount() == _tiles.Count && masters > 1)
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

        if (_tiles.Count == 1)
            _tiles[0].Master = true;

        Recalculate();
    }
}
