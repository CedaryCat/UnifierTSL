# 分支工作流速查

> Languages: [English](./branch-strategy-quick-reference.md) | [简体中文](./branch-strategy-quick-reference.zh-cn.md)

## 分支角色

| 分支 | 用途 | 默认目标 |
|:--|:--|:--|
| `develop` | 代码与文档的日常集成 | 常规 PR 基线 |
| `main` | 仅承接稳定晋升 | 在稳定后接收 `develop` |
| `release/<version>` | 从 `main` 切出的发版执行分支 | GitHub 发版工作 |
| `feature/*`、`bugfix/*`、`doc/*` | 可选的短生命周期工作分支 | 最终回到 `develop` |

文档中的标准写法统一使用 `release/<version>`。CI 仍兼容历史上的 `releases/*`，但除非你需要兼容旧自动化，否则不要再创建这种前缀的新分支。

## 常用命令

### 开始日常工作

```bash
git checkout develop
git pull --ff-only origin develop
git checkout -b feature/my-change
git push -u origin feature/my-change
```

### 晋升稳定快照

```text
develop -> main
```

### 切发版分支

```bash
git checkout main
git pull --ff-only origin main
git checkout -b release/0.2.1
git push -u origin release/0.2.1
```

## 发版动作

1. 向 `release/<version>` 推送，会自动产出下一个 alpha 预发布。
2. 打开 GitHub Actions，选择 `Build and Release`，在同一个 `release/<version>` 分支上手动运行：
   - `release_channel=rc` 发布下一个 RC。
   - `release_channel=stable` 发布正式版 GitHub Release。

## CI 触发速览

| 改动类型 | 工作流结果 |
|:--|:--|
| 向 `main`、`develop`、`release/*` 推送或发起仅文档变更 PR | `docs-check.yaml` |
| 向 `main`、`develop`、`release/*` 推送或发起非文档变更 PR | `build.yaml` + 构建产物 |
| 向 `release/*` 推送 | `build.yaml` + alpha 预发布 |
| 在 `release/*` 上手动触发 `workflow_dispatch` | RC 或 stable GitHub Release |

## 版本说明

- `develop` 构建使用 `beta` 标记。
- `release/*` 走 `rc` 发版线，并且推送时还会生成 alpha 预发布。
- `main` 保持为稳定晋升分支。
