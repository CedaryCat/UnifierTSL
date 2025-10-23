# Git Workflow Behavior Documentation

## Branch Architecture

This project uses a three-branch strategy:
- **main**: Production branch, only accepts merges from `develop` and `documentation`
- **develop**: Development branch, integration branch for daily development
- **documentation**: Documentation branch, dedicated to documentation maintenance

---

## Behavior 1: develop → main Merge

**Trigger Condition**: When `develop` branch is merged into `main` branch

**Execution Flow**:
1. ✅ **Trigger complete build workflow**
2. ✅ **Generate release packages for all platforms** (Windows/macOS/Linux x64/ARM/ARM64)
3. ✅ **Automatically create GitHub Release**
4. ✅ **Increment main branch GitVersion** (based on main's own version)

**Implementation Details**:
```yaml
# Implementation in build.yaml
release:
  if: github.event_name == 'push' && 
      github.ref == 'refs/heads/main' && 
      contains(github.event.head_commit.message, 'Merge') && 
      contains(github.event.head_commit.message, 'develop')
```
- **Condition Check**: Verifies merge from develop by checking push event, branch name, and commit message
- **Version Increment**: GitVersion config sets `prevent-increment-of-merged-branch-version: true` for `main` branch, ensuring increment based on main's current version rather than jumping to develop's version
- **Release Publishing**: Uses `softprops/action-gh-release` to automatically create tags and Releases

**Version Example**: When main is at 1.0.0, even if develop is at 1.5.0-beta.10, after merge main becomes 1.0.1

---

## Behavior 2: documentation → main Merge

**Trigger Condition**: When `documentation` branch is merged into `main` branch

**Execution Flow**:
1. ❌ **Does NOT trigger build workflow**
2. ❌ **Does NOT generate release packages**
3. ❌ **Does NOT create Release**
4. ❌ **Does NOT increment main branch GitVersion**

**Implementation Details**:
```yaml
# Implementation in build.yaml
build:
  if: github.ref == 'refs/heads/develop' || 
      github.ref == 'refs/heads/main' || 
      github.base_ref == 'develop'

release:
  if: contains(github.event.head_commit.message, 'develop')
```
- **Build Skip**: `build` job conditions only include develop-related checks, excludes documentation
- **Release Skip**: `release` job explicitly requires commit message to contain 'develop'
- **Version Preservation**: No build or tagging operations occur, main version remains unchanged

**Actual Effect**: Documentation updates can be safely merged to main without triggering any builds or version changes

---

## Behavior 3: Active Branch/PR → develop Merge

**Trigger Condition**: When feature branches (`feature/*`) or PR branches (`pr/*`) are merged into `develop` branch

**Execution Flow**:
1. ✅ **Trigger complete build workflow** (verify code builds correctly)
2. ✅ **Generate build artifacts for all platforms**
3. ✅ **Upload build artifacts as Artifacts** (but do NOT publish Release)
4. ✅ **Increment develop branch GitVersion**

**Implementation Details**:
```yaml
# Implementation in build.yaml
build:
  if: github.ref == 'refs/heads/develop' || 
      github.base_ref == 'develop'
```
- **Build Trigger**: Captures all PRs targeting develop via `github.base_ref == 'develop'`
- **Artifacts Upload**: Uses `actions/upload-artifact` to upload, but does NOT trigger `release` job
- **Version Increment**: GitVersion config uses `increment: Minor` for `develop` branch, version increments after each merge

**Version Example**: develop increments from 1.1.0-beta.1 to 1.2.0-beta.1

```yaml
# Configuration in GitVersion.yml
develop:
  mode: ContinuousDeployment
  tag: 'beta'
  increment: Minor
  prevent-increment-of-merged-branch-version: false
```

---

## Behavior 4: Active Branch/PR → documentation Merge

**Trigger Condition**: When any branch is merged into `documentation` branch

**Execution Flow**:
1. ✅ **Trigger documentation check workflow** (lightweight validation)
2. ❌ **Does NOT execute code build**
3. ❌ **Does NOT generate build artifacts**
4. ✅ **Increment documentation branch GitVersion**

**Implementation Details**:
```yaml
# Implementation in build.yaml
docs-check:
  if: github.ref == 'refs/heads/documentation' || 
      github.base_ref == 'documentation'
  
  steps:
    - name: Documentation Check
      run: |
        echo "Documentation branch - no build required"
```
- **Lightweight Check**: Only runs a simple validation step, no actual build execution
- **Version Increment**: GitVersion generates version numbers with `docs` tag for documentation branch

**Version Example**: documentation version numbers like 1.0.0-docs.1, 1.0.0-docs.2

```yaml
# Configuration in GitVersion.yml
documentation:
  tag: 'docs'
  increment: Patch
  prevent-increment-of-merged-branch-version: true
```

---

## Additional Behavior Notes

### Feature Branch Versioning
When pushing code to `feature/*` branches:
- Version format: `1.1.0-alpha.feature-name.1`
- No independent build trigger (only triggers when PR to develop)

### Hotfix Support
Configuration includes hotfix branch support:
```yaml
hotfix:
  regex: ^hotfix(es)?[/-]
  source-branches: ['main']
  increment: Patch
```
- Hotfix branches created directly from main
- Can merge directly back to main to trigger emergency release

---

## Version Evolution Example

Assuming initial state main is at `1.0.0`:

1. **Development Cycle Begins**
   - Create feature/new-api → version: `1.1.0-alpha.new-api.1`
   - Merge to develop → develop version: `1.1.0-beta.1`

2. **More Development**
   - feature/bug-fix merged to develop → develop version: `1.2.0-beta.1`
   - feature/enhancement merged to develop → develop version: `1.3.0-beta.1`

3. **Prepare for Release**
   - develop merged to main → main version: `1.0.1` (increments based on main)
   - Automatically creates Release v1.0.1

4. **Documentation Update**
   - Documentation merged to documentation → documentation version: `1.0.1-docs.1`
   - documentation merged to main → main version remains `1.0.1`

5. **Emergency Fix**
   - Create hotfix/critical → main version: `1.0.2`

---

# Key Configuration Snippets

## GitVersion Key Configuration

```yaml
branches:
  main:
    prevent-increment-of-merged-branch-version: true  # 防止版本跳跃
    increment: Patch                                   # 补丁级别递增
    is-mainline: true                                  # 主线分支
    
  develop:
    prevent-increment-of-merged-branch-version: false # 允许正常递增
    increment: Minor                                   # 次版本递增
    tag: 'beta'                                        # Beta 标签
    
  documentation:
    prevent-increment-of-merged-branch-version: true  # 不影响主版本
    increment: Patch                                   # 补丁级别递增
    tag: 'docs'                                        # 文档标签
```

## GitHub Actions Conditional Logic

```yaml
# 构建作业 - 只在代码变更时运行
build:
  if: |
    github.ref == 'refs/heads/develop' || 
    github.ref == 'refs/heads/main' || 
    github.base_ref == 'develop'

# 发布作业 - 只在 develop 合并到 main 时运行
release:
  if: |
    github.event_name == 'push' && 
    github.ref == 'refs/heads/main' && 
    contains(github.event.head_commit.message, 'Merge') && 
    contains(github.event.head_commit.message, 'develop')

# 文档检查 - 只在文档相关操作时运行
docs-check:
  if: |
    github.ref == 'refs/heads/documentation' || 
    github.base_ref == 'documentation'
```

---

# Verification Checklist

Before using this configuration, ensure:

✅ Repository has created `develop` and `documentation` branches  
✅ Branch protection rules are set (main only accepts PRs from develop and documentation)  
✅ GitVersion.yml is placed in repository root directory  
✅ build.yaml is placed in `.github/workflows/` directory  
✅ GITHUB_TOKEN permissions are configured (for creating Releases)  