# Winland keeps what Windows users need, and its own docs are the spec

`docs/behavior.md` states how Winland behaves, and the tests hold it to that. No other project is the spec. A change asks what Windows users need and what Windows allows, not what Hyprland does.

A feature, Option, Dispatcher, rule effect or keyword stays when the starter config uses it or a Windows user who has never heard of Hyprland would plausibly ask for it. When that is unclear, it goes, and an issue brings it back when someone asks. A removed keyword is deleted outright: ADR 0010 already makes it warn and be skipped, so there is no deprecation period and no list of removed names. A removed Option leaves its default as the only behavior. Duplicate spellings go and one name stays, and the kept names do not change (ADR 0002), so the Event stream and the queries that bars and scripts read stay as they are for every kept feature.

Winland began as a match for one Hyprland commit, `e2d94f5` after v0.56.2. Matching it made every change a question about another program, and kept Options, Dispatchers and whole features alive that no Windows user reaches for. Each one costs a page of `behavior.md`, tests, and state in `WindowManager`, and growing back from a small model costs one issue.

The rebuilt reference cases stay as ordinary tests where they describe behavior Winland keeps, and the rest are deleted. The reference clone, the gap table and the conformance score are gone. Hyprland is credited as Winland's origin in the README.
