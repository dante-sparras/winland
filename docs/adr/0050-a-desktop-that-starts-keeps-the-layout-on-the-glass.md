# A desktop that starts keeps the layout on the glass, and the Layout and the Groups on the windows

A desktop that starts has no tile tree. Adding a workspace's windows one at a time in the order Windows lists them, which is stacking order, gives the first tile to the window used last: two windows side by side swap whenever the right one was used last, and a resized split goes back to half.

The windows have not moved since the last desktop placed them. So a workspace with no tiles reads the rectangle each of its windows covers and builds the tree from those (`TileRecall`). A straight line that no window crosses is a split, with the ratio its two sides show. Windows that overlap, such as a fullscreen one over its neighbors, are split in half in the order of their centers. The window with the keyboard keeps it. Every Workspace gets this as the desktop starts, not only the one in front: the windows of the others are hidden, and a hidden window has not moved either, so their rectangles are read where they are (ADR 0078). A window that opens later still splits the focused tile.

The glass does not show two things: a Workspace's Layout, and which windows share a tile as a Group. Without more, a scrolling Workspace came back as dwindle and a Group came back as plain tiles. A window already carries its Workspace (ADR 0078) and its caption buttons (ADR 0074), so it carries these as well. Winoma sets the window property `Winland.Tiling` (`TilingMark`) to one number:

- Whether the window's Workspace is scrolling.
- The number of the Group the window is in, unique on its Workspace, with the window's place in the Group and whether the Group shows it. A plain tile carries no Group.

A plain tile in a dwindle Workspace carries nothing, and the property is taken off. The mark is an ask of the window gateway (`MarkTiling`), set by Platform (`TilingProperty`), and read with `TilingMarks`.

The shell writes the mark for every window of a Workspace whenever its session changed: a Layout toggle, a Group made, a window that left its Group, a window that moved in or out. A window is asked only when what it carries is not what was last written. A Workspace that is not in front is written too, so a hidden Workspace keeps its Layout.

A desktop that starts reads the marks once, with the Workspaces' (`AdoptMarks`), and does two things with them:

- **The Layout is read before a tile is built.** Each Workspace takes the Layout most of its tiles carry, so the first rectangles asked of the gateway for a scrolling Workspace are scrolling ones. A window that moved Workspaces carries the Layout of the one it left until the next write, and the tiles of its Workspace outvote it. Free windows carry none and are not counted.
- **A Group is made after the tiles are.** The tiles come from the rectangles as above, then the members a Group names are joined in one tile, in the order the mark gives, showing the member the mark names, in the tile of that member. A member that is alive and not among the tiles yet, such as the one a replaced desktop left hidden and this desktop showed, is waited for over a few placements (`RegroupPatience`). A member that is gone, or that another Workspace holds now, is left out. A Group of fewer than two windows is no Group, and the window is a plain tile: no window is lost.

A replace and a Stop and a start give the same answers: after a replace the hidden members are taken over still hidden, shown by the start as any hidden window of the Workspace in front is, and hidden again by the placement behind the member their Group shows.

Signing out closes every window, so nothing carries a Layout across it. A Workspace is empty after a sign-in, and an empty Workspace has no Layout to keep.

Writing the tree to a file was the other choice. It was turned down because a desktop that is killed cannot write on its way out, so the file would be written on every change, and a file of window handles is wrong after a restart of Windows. The windows are always current and need no file. It would also bring back a floated window and a Pseudotile, which the windows still do not carry.

## Consequences

Any program at the same integrity can read, change, or remove the property. It is a note, not a guard: the worst a wrong value does is make a Workspace scrolling, or join two windows in a Group, one Chord away from undone. A number Winoma never writes reads as no mark.

An administrator window refuses the property from a desktop that is not one, and unlike the Workspace mark (ADR 0078) no elevated helper writes this one. Its Group comes back without it, and the log says `could not mark window N with its layout and group` once. Uninstall takes the property off every open window, as it does the Workspace mark (ADR 0067).

A Group's order and shown member, and a Workspace's Layout, come back. A floated window, a Pseudotile, full width, and a resized split's ratio beyond what the rectangles show do not.
