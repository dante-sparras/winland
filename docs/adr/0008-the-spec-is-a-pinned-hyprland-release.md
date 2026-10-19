# The spec is a pinned Hyprland version

Winland matches one Hyprland commit, set in `scripts/dev.ps1`. The manual and source cloned there are the spec, and the Gaps and the conformance cases are listed against that commit. Moving to a newer commit is its own change, driven by a diff of what Hyprland changed.

The pin is `e2d94f5`, on Hyprland's `main` a few days after the v0.56.2 tag. Its test suite has every case `docs/conformance.md` lists, and v0.56.2 has only part of them, so the commit is the spec rather than the tag. A release tag is pinned again when one has those cases. The manual and the code name it "the pinned release".

Hyprland changes quickly, and tracking `main` would make "matches Hyprland" mean something different each week.
