# Branch Workflow Quick Reference

> Languages: [English](./branch-strategy-quick-reference.md) | [简体中文](./branch-strategy-quick-reference.zh-cn.md)

## Branch Roles

| Branch | Use it for | Default target |
|:--|:--|:--|
| `develop` | Daily integration for code and docs | Normal PR base |
| `main` | Stable promotions only | Receives `develop` when stable |
| `release/<version>` | Release execution from `main` | GitHub release work |
| `feature/*`, `bugfix/*`, `doc/*` | Optional short-lived working branches | Merge back to `develop` |

Official docs use `release/<version>`. CI still accepts legacy `releases/*`, but do not create new branches with that prefix unless you need compatibility with older automation.

## Common Commands

### Start normal work

```bash
git checkout develop
git pull --ff-only origin develop
git checkout -b feature/my-change
git push -u origin feature/my-change
```

### Promote a stable snapshot

```text
develop -> main
```

### Cut a release branch

```bash
git checkout main
git pull --ff-only origin main
git checkout -b release/0.2.1
git push -u origin release/0.2.1
```

## Release Actions

1. Push to `release/<version>` to produce the next alpha prerelease automatically.
2. Open GitHub Actions, choose `Build and Release`, select the same `release/<version>` branch, then run:
   - `release_channel=rc` for the next RC release.
   - `release_channel=stable` for the stable GitHub release.

## CI Trigger Summary

| Change | Workflow result |
|:--|:--|
| Docs-only push/PR to `main`, `develop`, or `release/*` | `docs-check.yaml` |
| Non-doc push/PR to `main`, `develop`, or `release/*` | `build.yaml` + artifacts |
| Push to `release/*` | `build.yaml` + alpha prerelease |
| Manual `workflow_dispatch` on `release/*` | RC or stable GitHub release |

## Versioning Notes

- `develop` builds use the `beta` label.
- `release/*` builds use the `rc` release line and can also emit alpha prereleases on push.
- `main` remains the stable promotion branch.
