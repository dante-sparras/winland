# Winland's own docs are the spec

Status: Proposed (issue to be filed)

`docs/behavior.md` states how Winland behaves, and the tests hold it to that. No other project is the spec. A change asks what Windows users need and what Windows allows, not what Hyprland does.

Winland began as a match for one Hyprland commit, `e2d94f5` after v0.56.2. Matching it made every change a question about another program, kept options and commands alive that no Windows user reaches for, and recorded about a hundred keywords only to say why they do nothing. Winland is for Windows users who want a keyboard-driven tiling window manager, and knowing Hyprland is not expected of them.

The rebuilt reference cases stay as ordinary tests where they describe behavior Winland keeps, and the rest are deleted. The reference clone, the gap table and the conformance score go away. Hyprland is credited as Winland's origin in the README.
