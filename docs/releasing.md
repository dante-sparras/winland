# Releasing

The committed version is `0.0.0`. A build without `-p:Version` is `0.0.0-dev`. Do not bump a version file.

The release version is the newest published, non-prerelease GitHub release whose tag is exactly `vMAJOR.MINOR.PATCH`, bumped at cut time. With no such release the base is `0.0.0`. The first release is cut with `-Bump minor` and is `0.1.0`.

`scripts/release.ps1` cuts, publishes, abandons, and prints status. `.github/workflows/stable-release.yml` admits the claim, runs release CI, builds the artifact, and uploads it to the draft. It does not publish. A **Run workflow** from the GitHub UI cannot ship a stable release.

## Order

1. `release` pushes an annotated tag `rc.<N>-vX.Y.Z`. The message is a JSON claim: commit, attempt, version, autopublish, and the time. A new claim sets autopublish to false. The workflow ignores that field.
2. It opens one draft release on that tag. The notes run from the previous published `vX.Y.Z` to this commit. An `rc.*` tag is not that previous release. The first release has no previous `vX.Y.Z`, so the notes start at the beginning of the history. A warning block sits at the top and the bottom of the body.
3. It dispatches **Stable Release** from `main` with that tag.
4. The workflow checks the claim, checks that the commit is on `origin/main`, and checks that the draft exists.
5. It runs CI for that commit with `release: true`, so mutation testing runs and a skipped lane fails. Mutation testing is one job per mutation run and then a score gate; any of them failing fails CI (see [CONTRIBUTING.md](../CONTRIBUTING.md#mutation-testing)). Then it builds the artifact.
6. It uploads `Winland-win-x64.zip`, `version.json`, and `SHA256SUMS` to the draft.
7. It stops. The draft stays a draft. `scripts/release.ps1 publish` creates the annotated tag `vX.Y.Z` on the same commit, points the release at that tag, strips the warnings, and publishes.

Do not press Publish in the GitHub UI. That skips the receipt tag.

Exactly one attempt may be outstanding. An attempt is outstanding when its `rc.*` tag has neither a final `vX.Y.Z` nor an `abandoned-rc.<N>-vX.Y.Z` marker. `release` refuses while one is outstanding and prints the workflow run, when one exists, and the `abandon` command.

## Commands

```powershell
pwsh -NoProfile -File scripts/release.ps1 status
pwsh -NoProfile -File scripts/release.ps1 release -Bump minor
pwsh -NoProfile -File scripts/release.ps1 publish -Version 0.1.0
pwsh -NoProfile -File scripts/release.ps1 abandon rc.1-v0.1.0
```

`-Bump` is `patch` (the default), `minor`, or `major`. `-Commit` selects a commit that is already on `origin/main` and that descends from the newest published tag. `-DryRun` prints every mutation and performs none.

`publish` is idempotent. After a partial failure, run it again. It reads each step before repeating it.

`status` prints the newest published version, any outstanding attempt, and the next version for each bump.

## What a release contains

The binaries are unsigned.

- `Winland-win-x64.zip`
- `version.json`
- `SHA256SUMS` for those two files

`version.json` has `commit`, `date`, and `version`. The same file is at the root of the zip. `date` is UTC.

The zip has two directories. `winland/` is `Winland.Daemon` published self-contained for `win-x64`, not single-file. `winlandctl/` is `winlandctl` published the same way.

The `v0.1.2` zip was one flat self-contained folder of the old host (`winland.exe`, `Winland.Host.exe`, `Winland.Watch.exe`) plus `version.json`. Those projects are gone. The two current programs do not share one runtime folder, so the zip has two directories. The `version.json` fields are the same.

## Tags

`.github/rulesets/protect-release-tags.json` blocks creation, update, and deletion of `v*`, `rc.*`, and `abandoned-rc.*` except for a repository admin (role id 5) and the GitHub Actions app (integration id 15368). `scripts/release.ps1 publish` creates the receipt tag as the maintainer, who is an admin. Apply that bypass on the live ruleset. Editing the file does not change the live rule.

A tag pushed by `GITHUB_TOKEN` does not start a workflow. This flow does not use a tag trigger.

## A patch without the rest of `main`

Use a `release/*` branch only when `X.Y.(Z+1)` must ship without every commit on `main`.

1. The branch ruleset already covers `release/*`. Pull-request CI runs. A push to `release/*` does not.
2. Cut the branch from the published tag:

   ```bash
   git fetch origin tag v1.4.1
   git switch -c release/1.4.2 v1.4.1
   git push -u origin release/1.4.2
   ```

3. One fix per pull request into `release/1.4.2`. Do not merge `main` into it.
4. Merge `release/1.4.2` into `main`. The patch commit is then on `origin/main` and does not contain later `main` commits.
5. Cut that commit: `scripts/release.ps1 release -Commit <patch sha>`. The workflow still starts from `main` and checks out the claim commit.
6. Leave the release branch until you no longer need another patch from it. Deletion is an admin action.

Check the tag, not the branch name, to see what shipped.
