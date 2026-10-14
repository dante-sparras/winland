# Issue tracker: GitHub Issues

Issues are GitHub Issues, one tracker per repo (ADR 0177): `dante-sparras/winoma`, `dante-sparras/winland`, and `dante-sparras/winshell`. A bug or a task goes on the repo whose code changes, wherever it was found. `gh` reaches any of them from any checkout with `-R`.

- **Publish**: `gh issue create -R dante-sparras/<repo> --title "<what is wrong or wanted>" --body-file <file> --label needs-triage`. The body says what was run, what was seen, what was expected, and the log lines with their `pid=`. One issue per problem.
- **Fetch**: `gh issue view <number> -R dante-sparras/<repo> --comments`.
- **Triage state**: one label from `triage-labels.md`. `gh issue edit <number> -R dante-sparras/<repo> --add-label ready-for-agent --remove-label needs-triage`.
- **Comment**: `gh issue comment <number> -R dante-sparras/<repo> --body-file <file>`.
- **Close**: the commit that fixes it says `Fixes dante-sparras/<repo>#<number>`, or `gh issue close <number> -R dante-sparras/<repo> --comment "<how it was checked>"`.

An issue labelled `ready-for-agent` is specified well enough for an agent in that repo to fix without asking: the behavior wanted, how to see it, and what is out of scope. A fix in Winland or Winshell reaches Winoma once it is released and Winoma moves its pin, so an issue on Winoma that waits on one names it: `Blocked by dante-sparras/winshell#12`.

## Wayfinding

Used by `/wayfinder`, and kept on disk, not on GitHub. The **map** is `.scratch/<effort>/map.md` (Notes, Decisions-so-far, Fog), with one **child** ticket per question in `issues/NN-<slug>.md`.

- A child has a `Type:` line (`research`, `prototype`, `grilling`, `task`) and a `Status:` line (`claimed` or `resolved`).
- `Blocked by: NN, NN` near the top. A ticket is unblocked when every file it lists is `resolved`.
- **Frontier**: the open, unblocked, unclaimed tickets. The lowest number wins.
- **Claim**: set `Status: claimed` and save before any work.
- **Resolve**: append the answer under `## Answer`, set `Status: resolved`, then add a context pointer (gist and link) to the map's Decisions-so-far.
- A `task` that is ready to build becomes a GitHub Issue on the repo it changes, and the child links it.
