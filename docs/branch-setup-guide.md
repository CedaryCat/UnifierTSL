# Branch Setup Guide

This document provides instructions for setting up the three-branch workflow strategy for UnifierTSL.

## Overview

The repository uses a three-branch strategy:
- **main**: Production branch, contains stable releases
- **develop**: Development branch, integration point for new features
- **documentation**: Documentation branch, for documentation updates

## Initial Setup

### 1. Create Required Branches

Run the following commands to create the `develop` and `documentation` branches:

```bash
# Create develop branch from main
git checkout main
git pull origin main
git checkout -b develop
git push -u origin develop

# Create documentation branch from main
git checkout main
git checkout -b documentation
git push -u origin documentation

# Return to main
git checkout main
```

### 2. Configure Branch Protection Rules (Recommended)

On GitHub, configure branch protection for `main`:

1. Go to: Settings → Branches → Add branch protection rule
2. Branch name pattern: `main`
3. Enable the following:
   - ✅ Require a pull request before merging
   - ✅ Require approvals (suggested: 1)
   - ✅ Dismiss stale pull request approvals when new commits are pushed
   - ✅ Require status checks to pass before merging
     - Add required check: `Build (linux-x64)`
     - Add required check: `Build (win-x64)`
   - ✅ Require conversation resolution before merging
   - ✅ Do not allow bypassing the above settings

Repeat for `develop` branch with similar settings.

### 3. Set Default Branch

Keep `main` as the default branch for the repository.

## Workflow Usage

### Feature Development

```bash
# Create a feature branch from develop
git checkout develop
git pull origin develop
git checkout -b feature/my-new-feature

# Make changes and commit
git add .
git commit -m "Add new feature"

# Push feature branch
git push -u origin feature/my-new-feature

# Create PR to develop on GitHub
```

### Bug Fixes

```bash
# Create a bugfix branch from develop
git checkout develop
git pull origin develop
git checkout -b bugfix/fix-issue-123

# Make changes and commit
git add .
git commit -m "Fix issue #123"

# Push and create PR to develop
git push -u origin bugfix/fix-issue-123
```

### Documentation Updates

```bash
# Create a docs branch from documentation
git checkout documentation
git pull origin documentation
git checkout -b docs/update-readme

# Make changes and commit
git add .
git commit -m "Update README documentation"

# Push and create PR to documentation
git push -u origin docs/update-readme
```

### Hotfix (Emergency Production Fix)

```bash
# Create hotfix branch from main
git checkout main
git pull origin main
git checkout -b hotfix/critical-bug

# Make changes and commit
git add .
git commit -m "Fix critical production bug"

# Push and create PR to main
git push -u origin hotfix/critical-bug

# After merging to main, merge back to develop
git checkout develop
git merge main
git push origin develop
```

### Release Process

When ready to release:

```bash
# Ensure develop is up to date
git checkout develop
git pull origin develop

# Create PR from develop to main on GitHub
# Title: "Release v1.x.x"
# After approval and merge, GitHub Actions will:
# 1. Build all platforms
# 2. Create version tags
# 3. Generate changelog
# 4. Publish GitHub Release with artifacts
```

## Version Management

Versions are automatically managed by GitVersion:

- **main**: Patch increments (1.0.0 → 1.0.1)
- **develop**: Minor increments with beta tag (1.1.0-beta.1 → 1.2.0-beta.1)
- **feature/***: Alpha versions (1.1.0-alpha.feature-name.1)
- **documentation**: Docs versions (1.0.0-docs.1)
- **hotfix/***: Patch increments

### Manual Version Control

Use commit messages to control version bumps:

```bash
# For a major version bump (breaking change)
git commit -m "Refactor API +semver: major"

# For a minor version bump (new feature)
git commit -m "Add user authentication +semver: minor"

# For a patch version bump (bug fix)
git commit -m "Fix login issue +semver: patch"
```

## CI/CD Behavior

### When pushing to `develop`:
- ✅ Runs full build for all platforms
- ✅ Uploads artifacts to GitHub Actions
- ✅ Increments develop version
- ❌ Does NOT create a GitHub Release

### When pushing to `main`:
- ✅ Runs full build for all platforms
- ✅ Uploads artifacts to GitHub Actions
- ⚠️ Creates GitHub Release ONLY if merge from develop/hotfix
- ✅ Increments main version

### When pushing to `documentation`:
- ✅ Runs lightweight documentation check
- ❌ Does NOT build code
- ❌ Does NOT create artifacts
- ✅ Increments documentation version

### Pull Requests:
- ✅ Builds code to verify it compiles
- ✅ Shows version that would be generated
- ❌ Does NOT create artifacts or releases

## Troubleshooting

### GitVersion not calculating version correctly

Ensure your repository has full history:
```bash
git fetch --unshallow
```

### Branch not triggering workflow

Check that:
1. Branch name matches the workflow triggers
2. `.github/workflows/build.yaml` is present in the branch
3. GitHub Actions is enabled for the repository

### Release not being created

Verify:
1. Merge was from `develop` to `main`
2. Commit message contains "Merge" and "develop"
3. Repository has `GITHUB_TOKEN` permissions for releases

## Reference

- [GitVersion Documentation](https://gitversion.net/docs/)
- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [Branch Strategy Proposal](./proposal-branch-strategy.md)
