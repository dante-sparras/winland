# Winland matches Hyprland's names and semantics, and its code never says Hyprland

Winland's config syntax, option names, dispatchers and concepts match Hyprland's, so its manual is the spec. Anything that cannot match is a Gap, documented as such. The product is Winland, so no source file, identifier, file name or user-visible string contains "hyprland". The config file is `winland.conf`, and the control tool is `winlandctl`.

Hyprland's manual covers Wayland-only options and shell commands that have no Windows meaning. Each such keyword is ignored, warned about or rejected on a per-keyword basis, and recorded as a Gap.
