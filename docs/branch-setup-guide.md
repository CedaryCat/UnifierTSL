# Branch Workflow Guide

> Languages: [English](./branch-setup-guide.md) | [简体中文](./branch-setup-guide.zh-cn.md)

## Summary

UnifierTSL uses one daily integration branch and one stable promotion branch:

- `develop` receives normal code and docs changes.
- `main` only receives stable promotions from `develop`.
- `release/<version>` is cut from `main` and is the branch family used to execute GitHub releases.

Official docs use `release/<version>` as the branch name. CI still accepts `releases/*` as a legacy compatibility alias, but new branches should use `release/*`.

## Official Branch Roles

| Branch | Role | Notes |
|:--|:--|:--|
| `develop` | Daily integration branch | Default target for normal PRs, including docs-only updates |
| `main` | Stable promotion branch | Updated when the current `develop` state is considered stable |
| `release/<version>` | Release execution branch | Cut from `main`; pushes publish alpha prereleases, manual dispatch creates RC or stable releases |

Optional short-lived working branches such as `feature/*`, `bugfix/*`, and `doc/*` are supported as convenience branches. They are not separate long-lived lanes and should merge back into `develop`.

## Local Setup

1. Track `main` and `develop` locally:

   ```bash
   git fetch origin
   git checkout main
   git pull --ff-only origin main
   git checkout develop
   git pull --ff-only origin develop
   ```

2. Keep `main` as the default branch on GitHub.
3. Protect `main` and `develop` with PR-based merges and required checks if the repository settings allow it.

## Day-to-Day Development

Base regular work on `develop`. That includes runtime code, docs, and workflow updates.

```bash
git checkout develop
git pull --ff-only origin develop
git checkout -b feature/my-change
# work, commit, push
git push -u origin feature/my-change
```

Open the PR against `develop`. For small repos or direct-maintainer work, committing directly to `develop` is also fine if that already matches the repository norm.

## Promote a Stable Snapshot

When `develop` reaches a stable state, open a PR from `develop` to `main`.

```text
develop -> main
```

`main` should stay reviewable and releasable. Do not use it as the normal branch for day-to-day iteration.

## Execute a Release

1. Start from the current stable `main`:

   ```bash
   git checkout main
   git pull --ff-only origin main
   git checkout -b release/0.2.1
   git push -u origin release/0.2.1
   ```

2. If `main` receives late stable fixes during release prep, refresh the release branch from `main` before continuing.
3. Pushes to `release/<version>` run the build matrix and automatically publish the next alpha prerelease.
4. When you are ready to publish an RC or stable build, open GitHub Actions and run `Build and Release` on the same `release/<version>` branch:
   - `release_channel=rc` creates the next `vX.Y.Z-rc.N` GitHub release.
   - `release_channel=stable` creates `vX.Y.Z` unless you provide an explicit `stable_tag`.
5. After the release line is complete, keep daily development moving on `develop`; the next release branch should be cut from a newer `main`.

## CI and Version Notes

| Event | Result |
|:--|:--|
| Docs-only push/PR to `main`, `develop`, or `release/*` | `docs-check.yaml` runs |
| Non-doc push/PR to `main`, `develop`, or `release/*` | `build.yaml` runs and uploads artifacts |
| Push to `release/*` | Build runs, then the alpha prerelease job creates a GitHub prerelease |
| Manual `workflow_dispatch` on `release/*` | Builds run, then Actions creates an RC or stable GitHub release |

GitVersion still understands optional short-lived branch families such as `feature/*`, `bugfix/*`, and `hotfix/*`, but the repository's documented default flow is still `develop -> main -> release/<version>`.
