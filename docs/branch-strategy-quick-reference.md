# Branch Strategy Quick Reference

## Branch Overview

| Branch | Purpose | Version Pattern | Auto Release |
|--------|---------|-----------------|--------------|
| `main` | Production | 1.0.0 | ✅ Yes (from develop/hotfix) |
| `develop` | Development | 1.1.0-beta.1 | ❌ No |
| `documentation` | Docs only | 1.0.0-docs.1 | ❌ No |
| `feature/*` | New features | 1.1.0-alpha.name.1 | ❌ No |
| `bugfix/*` | Bug fixes | 1.1.0-bugfix.name.1 | ❌ No |
| `hotfix/*` | Emergency fixes | 1.0.1-hotfix.1 | ✅ Yes (to main) |

## Common Commands

### Start New Feature
```bash
git checkout develop && git pull
git checkout -b feature/my-feature
# work, commit, push
# PR to develop
```

### Fix a Bug
```bash
git checkout develop && git pull
git checkout -b bugfix/fix-issue-123
# work, commit, push
# PR to develop
```

### Update Documentation
```bash
git checkout documentation && git pull
git checkout -b docs/update-readme
# work, commit, push
# PR to documentation
```

### Emergency Hotfix
```bash
git checkout main && git pull
git checkout -b hotfix/critical-bug
# work, commit, push
# PR to main
# After merge, sync to develop:
git checkout develop && git merge main && git push
```

### Create Release
```bash
# On GitHub: Create PR from develop to main
# Title: "Release v1.x.x"
# After merge → Auto-creates GitHub Release
```

## Version Control via Commits

```bash
# Major version bump (breaking change)
git commit -m "Refactor API +semver: major"

# Minor version bump (new feature)
git commit -m "Add authentication +semver: minor"

# Patch version bump (bug fix)
git commit -m "Fix login bug +semver: patch"
```

## CI/CD Triggers

| Action | Builds Code | Creates Artifacts | Creates Release |
|--------|-------------|-------------------|-----------------|
| Push to develop | ✅ | ✅ | ❌ |
| Push to main (direct) | ✅ | ✅ | ❌ |
| develop → main | ✅ | ✅ | ✅ |
| hotfix → main | ✅ | ✅ | ✅ |
| documentation → main | ❌ | ❌ | ❌ |
| PR to develop | ✅ | ✅ | ❌ |
| PR to main | ✅ | ✅ | ❌ |
| PR to documentation | ❌ | ❌ | ❌ |

## Build Platforms

All builds create artifacts for:
- Windows x64
- macOS x64
- Linux x64
- Linux ARM64
- Linux ARM

## Quick Troubleshooting

### "GitVersion not found"
```bash
git fetch --unshallow
```

### "Workflow not triggering"
Check branch name matches: `main`, `develop`, or `documentation`

### "Release not created after merge"
Verify commit message contains "Merge" and "develop" or "hotfix"

## File Locations

- GitVersion config: `GitVersion.yml`
- Workflow: `.github/workflows/build.yaml`
- Full docs: `docs/branch-setup-guide.md`
- Detailed setup guide: `docs/branch-setup-guide.md`
