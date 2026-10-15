# The config is the line-based syntax first, and Lua comes later

Winland reads `winland.conf` in Hyprland's line-based syntax (`bind = SUPER, Q, killactive`, `monitor = ...`, `windowrulev2 = ...`). It does not read Lua configs yet.

The pinned release, Hyprland v0.56, documents a Lua configuration (`hl.bind`, `hl.monitor`, `hl.window_rule`), and its manual no longer shows the line-based form. The source still links the line-based parser, and most existing Hyprland configs and dotfiles use it. Finishing Winland on the line-based syntax first gets existing configs working. Lua needs a Lua runtime inside Winland and an implementation of the `hl.*` API, which is a milestone of its own, so it is deferred and not dropped.

Until Lua lands, a Hyprland feature that exists only in the Lua form is a Gap. The pinned manual and the line-based syntax can disagree. The source under `docs/agents/hyprland/src/` decides which keywords exist and what their defaults are.
