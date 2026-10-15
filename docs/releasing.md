# Releasing

Nobody installs this repo from a channel yet. There is no update command and no publish workflow. The rules below are the git policy for when a release exists. They are the small-repository procedure, not the Hermes release machine.

## Channels

| Channel | Git target | Who it is for |
| --- | --- | --- |
| `stable` | The latest published `vX.Y.Z`, that exact commit | The default once something is published |
| `main` | Tip of `main` | Every commit |

A published release is an annotated tag `vX.Y.Z` and a GitHub release on that tag. The tag is the pointer. A branch named `release/X.Y.Z` is not the published commit unless the tag points at it.

## Cut from `main`

1. `git fetch origin main` and confirm the commit is on `origin/main`.
2. Confirm it descends from the previous `vX.Y.Z`: `git merge-base --is-ancestor <previous> <commit>`.
3. Choose the bump. Patch unless you mean a minor or major.
4. Tag an annotated release candidate, push it, and open a **draft** GitHub release. Dispatch **CI** from `main` with **Release run** on. That run is the strict gate, and it includes mutation testing. The draft stays unpublished.
5. When that run is green, create the annotated `vX.Y.Z` on the same commit, point the GitHub release at it, and publish.
6. Do not press Publish on the draft in the GitHub UI. That skips the receipt tag.

Canary tags are not cut. Add one when someone asks for a prerelease channel.

## Tags

`.github/rulesets/protect-release-tags.json` blocks creation, update, and deletion of `v*`, `rc.*`, and `abandoned-rc.*` except for a repository admin. This account is a user, so the bypass actor is repository role `admin` (id 5), not `OrganizationAdmin`. An admin can still move a tag. Anyone else with push access cannot.

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
4. Cut the release from that commit with the same steps as `main`. Dispatch CI from `main`, not from the release branch.
5. Merge `release/1.4.2` back into `main` before the next release cut from `main`.
6. Leave the release branch until you no longer need another patch from it. Deletion is an admin action.

Check the tag, not the branch name, to see what shipped.
