# Issue tracker: GitHub Issues

Issues are GitHub Issues on this repo. A bug or a task goes here when this repo's files change. `gh` from this checkout reaches them.

- **Publish**: `gh issue create --title "<what is wrong or wanted>" --body-file <file> --label needs-triage`. The body says what was run, what was seen, what was expected, and the log lines. One issue per problem.
- **Fetch**: `gh issue view <number> --comments`.
- **Triage state**: one label from `triage-labels.md`. `gh issue edit <number> --add-label ready-for-agent --remove-label needs-triage`.
- **Comment**: `gh issue comment <number> --body-file <file>`.
- **Close**: the commit that fixes it says `Fixes #<number>`, or `gh issue close <number> --comment "<how it was checked>"`.

An issue labelled `ready-for-agent` is specified well enough for an agent to fix without asking: the behavior wanted, how to see it, and what is out of scope.

A feature, and any intentional change to behavior, starts only after a maintainer comment approves the direction and the scope. The pull request links that comment. A link to the issue is not the approval.

A substantial bug fix links an issue a maintainer has triaged. A very small fix for an obvious bug can skip the issue, and the pull request says why. `CONTRIBUTING.md` is the rule.

## Wayfinding

Used by `/wayfinder`, and kept on disk, not on GitHub. The **map** is `.scratch/<effort>/map.md` (Notes, Decisions-so-far, Fog), with one **child** ticket per question in `issues/NN-<slug>.md`.

- A child has a `Type:` line (`research`, `prototype`, `grilling`, `task`) and a `Status:` line (`claimed` or `resolved`).
- `Blocked by: NN, NN` near the top. A ticket is unblocked when every file it lists is `resolved`.
- **Frontier**: the open, unblocked, unclaimed tickets. The lowest number wins.
- **Claim**: set `Status: claimed` and save before any work.
- **Resolve**: append the answer under `## Answer`, set `Status: resolved`, then add a context pointer (gist and link) to the map's Decisions-so-far.
- A `task` that is ready to build becomes a GitHub Issue on the repo it changes, and the child links it.
