# 分支工作流指南

> Languages: [English](./branch-setup-guide.md) | [简体中文](./branch-setup-guide.zh-cn.md)

## 摘要

UnifierTSL 当前使用一条日常集成分支和一条稳定晋升分支：

- `develop` 承接常规代码与文档更新。
- `main` 只承接来自 `develop` 的稳定晋升。
- `release/<version>` 从 `main` 切出，用于执行 GitHub 发版。

文档中的标准写法统一使用 `release/<version>`。CI 仍兼容历史上的 `releases/*` 前缀，但新分支应统一使用 `release/*`。

## 官方分支角色

| 分支 | 角色 | 说明 |
|:--|:--|:--|
| `develop` | 日常集成分支 | 常规 PR 的默认目标，包括仅文档改动 |
| `main` | 稳定晋升分支 | 当 `develop` 达到稳定状态时接收晋升 |
| `release/<version>` | 发版执行分支 | 从 `main` 切出；推送会生成 alpha 预发布，手动触发可发布 RC 或 stable |

`feature/*`、`bugfix/*`、`doc/*` 这类短生命周期工作分支仍可作为便利分支使用，但它们不是独立的长期泳道，最终都应回到 `develop`。

## 本地准备

1. 在本地跟踪 `main` 和 `develop`：

   ```bash
   git fetch origin
   git checkout main
   git pull --ff-only origin main
   git checkout develop
   git pull --ff-only origin develop
   ```

2. GitHub 默认分支保持为 `main`。
3. 如果仓库设置允许，建议给 `main` 和 `develop` 打开基于 PR 的合并和必需检查。

## 日常开发

常规工作统一基于 `develop`，包括运行时代码、文档和工作流更新。

```bash
git checkout develop
git pull --ff-only origin develop
git checkout -b feature/my-change
# work, commit, push
git push -u origin feature/my-change
```

PR 目标统一指向 `develop`。如果仓库维护方式本来就允许直接提交到 `develop`，小团队场景下继续保持也可以。

## 晋升稳定快照

当 `develop` 到达可稳定发布的状态时，从 `develop` 向 `main` 发起 PR：

```text
develop -> main
```

`main` 应始终保持可评审、可发版，不应作为日常迭代分支使用。

## 执行发版

1. 从当前稳定的 `main` 切出发版分支：

   ```bash
   git checkout main
   git pull --ff-only origin main
   git checkout -b release/0.2.1
   git push -u origin release/0.2.1
   ```

2. 如果发版准备期间 `main` 又接收了新的稳定修复，先用最新的 `main` 刷新该发版分支，再继续发版流程。
3. 每次向 `release/<version>` 推送，都会跑完整 build matrix，并自动创建下一个 alpha 预发布。
4. 当你准备发布 RC 或正式版时，在 GitHub Actions 中对同一个 `release/<version>` 分支手动运行 `Build and Release`：
   - `release_channel=rc` 会创建下一个 `vX.Y.Z-rc.N` GitHub Release。
   - `release_channel=stable` 会创建 `vX.Y.Z`，除非你显式提供 `stable_tag`。
5. 当前发版线结束后，日常开发继续留在 `develop`；下一条发版线再从更新后的 `main` 重新切出。

## CI 与版本说明

| 事件 | 结果 |
|:--|:--|
| 向 `main`、`develop`、`release/*` 提交或发起仅文档变更 PR | 运行 `docs-check.yaml` |
| 向 `main`、`develop`、`release/*` 提交或发起非文档变更 PR | 运行 `build.yaml` 并上传构建产物 |
| 向 `release/*` 推送 | 先构建，再自动创建 alpha GitHub 预发布 |
| 在 `release/*` 上手动触发 `workflow_dispatch` | 构建后创建 RC 或 stable GitHub Release |

GitVersion 仍然支持 `feature/*`、`bugfix/*`、`hotfix/*` 这类可选短生命周期分支，但仓库文档里的默认主流程固定为 `develop -> main -> release/<version>`。
