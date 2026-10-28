# Releasing

Stable releases are cut by release-please (`.github/workflows/release-please.yml`). The version and the changelog come from the Conventional Commit titles on `main`.

The committed version is `0.0.0`, and a build without `-p:Version` is `0.0.0-dev`. Do not bump a version file. The last released version is in `.release-please-manifest.json`, which release-please updates.

## Order

1. Every push to `main` updates one open release PR, titled `chore(main): release X.Y.Z`. It bumps `.release-please-manifest.json` and adds the release's section to `CHANGELOG.md`. A `fix` is a patch, a `feat` is a minor version, and `!` is a breaking change. Before `1.0.0` a breaking change bumps the minor version.
2. CI runs on the release PR like on any other, because release-please opens it with `RELEASE_PLEASE_TOKEN`.
3. Merging the release PR makes a draft release and creates the tag `vX.Y.Z` on the merge commit.
4. The **Build and publish the release** job checks out that tag, publishes `Winland.Daemon` with `-p:Version=X.Y.Z`, uploads the assets to the draft, and publishes it as the latest release.

Do not publish the draft in the GitHub UI. A release published before its assets are uploaded has no zip, and `install.ps1` and `winland update` fail on it. If the job fails, re-run it. The upload replaces any asset already there.

To choose the version, add `Release-As: X.Y.Z` to the end of a squash commit message on `main`.

## What a release contains

The binaries are unsigned.

- `Winland-win-x64.zip`
- `version.json`
- `SHA256SUMS` for those two files

`version.json` has `commit`, `date`, and `version`. The same file is at the root of the zip. `date` is UTC.

The zip has one directory. `winland/` is `Winland.Daemon` published self-contained for `win-x64`, not single-file. Releases up to `v0.1.0` also had a `winlandctl/` directory. An install from one of those cannot `winland update` to a later release, because its install code still looks for `winlandctl`, so it is reinstalled from the zip once.

The `v0.1.2` zip was one flat self-contained folder of the old host (`winland.exe`, `Winland.Host.exe`, `Winland.Watch.exe`) plus `version.json`. Those projects are gone. The `version.json` fields are the same.

## Dev releases

`.github/workflows/dev-release.yml` publishes one rolling prerelease on the tag `dev` after CI passes on each push to `main`. Its version is the newest published `vX.Y.Z` with `-dev.<commits since>` added, such as `0.4.0-dev.12+a886990`. Immutable releases stay off, because that workflow moves the `dev` tag and replaces its assets.

## Tags

`.github/rulesets/protect-release-tags.json` blocks creating, moving, or deleting `v*` tags except for a repository admin (role id 5) and the GitHub Actions app (integration id 15368). release-please creates the tag with `RELEASE_PLEASE_TOKEN`, a fine-grained token of the maintainer, who is an admin. Editing the file does not change the live rule.

## The release token

`RELEASE_PLEASE_TOKEN` is a fine-grained personal access token for this repo only, with Contents, Pull requests, and Issues set to read and write. Store it with `gh secret set RELEASE_PLEASE_TOKEN --repo dante-sparras/winland`. It expires. When it has, release-please falls back to the built-in token, and the release PR's checks do not start until you close and reopen it.
