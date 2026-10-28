# Contributing

## Before you start

- **Found a bug?** [Open an issue](https://github.com/dante-sparras/winland/issues/new/choose).
- **Have an idea?** [Open a feature request](https://github.com/dante-sparras/winland/issues/new?template=feature_request.yml).
- **Search first.** Someone may already have reported it, or be working on it.
- **Found a vulnerability?** Don't open an issue; follow [SECURITY.md](.github/SECURITY.md).

Before you build a feature or change how something behaves, open a feature request and wait for the maintainer to agree in it. Link that issue in your PR. A small fix for an obvious bug needs no prior issue; just explain the bug in the PR.

## Set up

The [development runbook](docs/development.md#first-checkout) covers the prerequisites, the first checkout, environment setup, and every command.

## Make your change

**One problem per PR.** If you fixed two unrelated things, open two PRs. A cleanup or refactor you noticed along the way gets its own PR too, unless the fix depends on it.

**Include everything that problem needs**: the code, its tests, and any docs that would otherwise be wrong.

**Run the checks CI runs** before you open the PR, and make them pass. The [commands](docs/development.md#commands) are in the runbook.

## Open a PR

### Branch name

`<type>/<short-description>`, lowercase, using a type from the table below. For example, `fix/duplicate-orders` or `feat/csv-export`.

### PR title

A [Conventional Commit](https://www.conventionalcommits.org) in plain language: `<type>(<optional scope>): <what changed>`, starting lowercase. For example, `fix(api): retries no longer duplicate orders`.

| Type | Use it for |
| --- | --- |
| `feat` | A new feature |
| `fix` | A bug fix |
| `docs` | Documentation only |
| `style` | Formatting, with no change in behavior |
| `refactor` | Restructuring code, with no change in behavior |
| `perf` | A performance improvement |
| `test` | Adding or fixing tests |
| `build` | The build system or dependencies |
| `ci` | CI configuration |
| `chore` | Maintenance that fits nowhere else |
| `revert` | Undoing an earlier change |

For a breaking change, add `!` before the colon: `feat(api)!: remove the v1 endpoints`.

Commits on your branch can use any message; the squash merge replaces them with the PR title. Using the same format for them is still a good habit.

CI checks the PR title. It also checks the branch name, but only as a reminder; it doesn't block merging.

### PR description

Fill in the PR template:

- **Problem**: what's wrong or missing, and how to reproduce it.
- **Change**: how your PR solves it.
- **Scope and approval**: the issue it comes from, if any.
- **Verification**: how you checked that it works, and what you saw. Name the tests or manual steps you ran, and anything you couldn't check. "Tests pass" on its own isn't enough. For visible changes, add before/after screenshots, or a short recording when motion matters. Attach them to the PR; don't commit them to the repo.

If an AI agent did the work, end the description with the model and the tool you used, such as "Claude Opus 5.5 in Claude Code".

## Review and merge

- Every change reaches `main` through a PR, and the required checks (`All required checks pass (v1)`) must pass first.
- PRs are squash-merged. Your PR title becomes the commit message on `main`, so make it readable on its own.
- Releases are cut from those commits, so your PR title is also your line in the changelog.
- A PR that doesn't follow this guide may be closed with a note saying what's missing. Fix it and ask for another look, or open a new PR.
