# UnifierTSL

> 语言：[English](../README.md) | [简体中文](./README.zh-cn.md)

<p align="center">
  <img src="./assets/readme/hero.svg" alt="UnifierTSL hero" width="100%">
</p>

<p align="center">
  <a href="#快速开始"><img alt="Quick Start" src="https://img.shields.io/badge/Quick_Start-5分钟启动-06B6D4?style=for-the-badge"></a>
  <a href="https://github.com/CedaryCat/UnifierTSL/releases"><img alt="Releases" src="https://img.shields.io/badge/Releases-下载发布包-0EA5E9?style=for-the-badge&logo=github"></a>
  <a href="./dev-plugin.zh-cn.md"><img alt="Plugin Guide" src="https://img.shields.io/badge/插件文档-开发_迁移指南-10B981?style=for-the-badge"></a>
  <a href="#架构"><img alt="Architecture" src="https://img.shields.io/badge/架构-系统流程-F97316?style=for-the-badge"></a>
</p>

<p align="center">
  <a href="https://github.com/CedaryCat/UnifierTSL/actions/workflows/build.yaml"><img alt="Build" src="https://img.shields.io/github/actions/workflow/status/CedaryCat/UnifierTSL/build.yaml?branch=main&label=build"></a>
  <a href="https://github.com/CedaryCat/UnifierTSL/actions/workflows/docs-check.yaml"><img alt="Docs Check" src="https://img.shields.io/github/actions/workflow/status/CedaryCat/UnifierTSL/docs-check.yaml?label=docs-check"></a>
  <a href="../src/UnifierTSL.slnx"><img alt=".NET 9.0" src="https://img.shields.io/badge/.NET-9.0-2563EB?logo=dotnet&logoColor=white"></a>
  <a href="../LICENSE"><img alt="许可证: GPL-3.0" src="https://img.shields.io/badge/License-GPL--3.0-16A34A"></a>
  <img alt="平台" src="https://img.shields.io/badge/Platforms-win--x64%20%7C%20linux--x64%20%7C%20linux--arm64%20%7C%20linux--arm%20%7C%20osx--x64-0F766E">
</p>

<p align="center">
  <em>基于 OTAPI USP 的实验友好型 Terraria 服务器启动器，包含多世界协调、独立控制台、插件宿主与发布工具链。</em>
</p>

---

<p align="center">
  <img src="./assets/readme/quick-glance.svg" alt="UnifierTSL quick glance" width="100%">
</p>

<table>
  <tr>
    <td><strong>单入口，多世界运行</strong><br>在一个启动器进程中统一协调多个 Terraria 世界。</td>
    <td><strong>可回收插件上下文</strong><br>模块级加载上下文、依赖分发与结构化配置管理。</td>
  </tr>
  <tr>
    <td><strong>可复现发布输出</strong><br>本地按 RID 生成目录产物，CI 再打包成发布归档。</td>
    <td><strong>内置 TShock 迁移基线</strong><br>以固定上游仓库/分支/提交/版本进行追踪。</td>
  </tr>
</table>

## 目录
- [概览](#概览)
- [核心能力](#核心能力)
- [版本矩阵与基线](#版本矩阵与基线)
- [架构](#架构)
- [按角色选择入口](#按角色选择入口)
- [快速开始](#快速开始)
  - [前置要求](#前置要求)
  - [使用发布包](#使用发布包)
  - [从源码运行](#从源码运行)
- [启动器参数速查](#启动器参数速查)
- [服务器定义键](#服务器定义键)
- [发布产物生命周期](#发布产物生命周期)
  - [Publisher 本地初始输出](#publisher-本地初始输出)
  - [首次启动后的插件重排布局](#首次启动后的插件重排布局)
  - [CI 构建产物与 Release 命名](#ci-构建产物与-release-命名)
- [Publisher CLI 参考](#publisher-cli-参考)
- [项目结构](#项目结构)
- [插件特性概览](#插件特性概览)
- [开发者参考](#开发者参考)
- [延伸阅读](#延伸阅读)

## 概览
UnifierTSL 将 OTAPI Unified Server Process 封装为更易用的运维与开发工作流，目标是在一台主机上稳定托管多个 Terraria 世界。启动器负责世界生命周期、玩家路由，并为每个世界上下文创建独立控制台客户端，让输入输出互不干扰。

仓库内同时包含启动器、发布器、控制台客户端和维护中的插件样例。共享服务由 `UnifiedServerCoordinator` 统筹，事件流经 `UnifierApi.EventHub`，插件加载由 `PluginHost.PluginOrchestrator` 协调。

## 核心能力
- **多世界协调**：在单进程中启动并隔离多个世界。
- **动态路由控制**：通过协调器事件设定默认入服策略。
- **插件托管**：从 `plugins/` 加载 .NET 模块，支持配置注册和依赖分发。
- **可回收模块上下文**：基于 `ModuleLoadContext` 进行可卸载插件域管理。
- **统一日志管线**：`UnifierApi.LogCore` 支持可插拔过滤器/写入器/元数据注入。
- **内置 TShock 迁移构建**：提供适配 USP 运行时的 TShock 基线。
- **独立控制台隔离**：每个世界由启动器拉起专属控制台客户端进程。
- **按 RID 发布**：Publisher 生成可复现的目标运行时目录产物。

  | 包 | 版本 | 作用 |
  | --- | --- | --- |
  | OTAPI.USP | 1.1.0-pre-release | Unified Server Process 核心 |
  | ModFramework | 1.1.15 | 补丁阶段使用的 IL 修改框架 |
  | MonoMod.RuntimeDetour | 25.2.3 | 运行期方法钩子与跳转 |
  | Tomlyn | 0.19.0 | 启动器与插件使用的 TOML 配置解析 |
  | linq2db | 5.4.1 | 示例插件使用的数据库抽象层 |
  | Microsoft.Data.Sqlite | 9.0.0 | TShock 与示例插件使用的 SQLite 提供程序 |
## 版本矩阵与基线
以下值来自仓库内当前项目文件。

| 项目 | 值 | 来源 |
| --- | --- | --- |
| 目标框架 | `.NET 9.0` | `src/UnifierTSL/*.csproj` |
| OTAPI USP 包版本 | `1.1.0-pre-release-upstream.24` | `src/UnifierTSL/UnifierTSL.csproj` |
| ModFramework | `1.1.15` | `src/UnifierTSL/UnifierTSL.csproj` |
| MonoMod.RuntimeDetour | `25.2.3` | `src/UnifierTSL/UnifierTSL.csproj` |
| Tomlyn | `0.19.0` | `src/UnifierTSL/UnifierTSL.csproj` |
| linq2db | `5.4.1` | `src/UnifierTSL/UnifierTSL.csproj` |
| Microsoft.Data.Sqlite | `9.0.0` | `src/UnifierTSL/UnifierTSL.csproj` |
| 内置 TShock 主线版本 | `5.9.9` | `src/Plugins/TShockAPI/TShockAPI.csproj` |
| TShock 同步分支 | `general-devel` | `src/Plugins/TShockAPI/TShockAPI.csproj` |
| TShock 同步提交 | `cd68321fcc7b7b2a02d8ed6449910c4763b45350` | `src/Plugins/TShockAPI/TShockAPI.csproj` |

## 架构
<p align="center">
  <img src="./assets/readme/arch-flow.svg" alt="UnifierTSL architecture flow" width="100%">
</p>

核心职责分层：
- `Program.cs`：启动入口与引导流程。
- `UnifierApi`：初始化事件中心、插件编排器与命令行处理。
- `UnifiedServerCoordinator`：监听端口、客户端接入、跨世界路由。
- `ServerContext`：隔离每个世界运行时状态。
- `PluginHost`：插件发现、加载、依赖提取与生命周期管理。

## 按角色选择入口
| 角色 | 建议入口 | 目的 |
| --- | --- | --- |
| 服主/运维 | [快速开始](#快速开始) | 最短路径启动多世界托管。 |
| 插件开发者 | [插件开发与迁移指南](./dev-plugin.zh-cn.md) | 按项目规范完成开发、迁移、配置与热重载。 |

## 快速开始
### 前置要求
根据你的使用方式选择依赖：

- **只使用发布包**：
  - 目标主机安装 `.NET 9` Runtime。
- **源码运行或本地 Publisher 打包**：
  - 安装 `.NET 9 SDK`（`dotnet --list-sdks`）。
  - 确保 `msgfmt` 在 `PATH`（Publisher 生成 `.mo` 依赖该工具）。

### 使用发布包
1. 在 GitHub Releases 下载对应 RID 的资产。
   - Windows: `utsl-<rid>-v<semver>.zip`
   - Linux/macOS: `utsl-<rid>-v<semver>.tar.gz`
2. 解压到目标主机。
3. 通过参数启动。

Windows（PowerShell）：
```powershell
.\UnifierTSL.exe -lang 7 -port 7777 -password changeme `
  -server "name:S1 worldname:S1 gamemode:3 size:1 evil:0 seed:\"for the worthy\"" `
  -server "name:S2 worldname:S2 gamemode:2 size:2" `
  -joinserver first
```

Linux/macOS：
```bash
chmod +x UnifierTSL
./UnifierTSL -lang 7 -port 7777 -password changeme \
  -server "name:S1 worldname:S1 gamemode:3 size:1 evil:0 seed:\"for the worthy\"" \
  -joinserver first
```

### 从源码运行
适用于本地调试、CI 集成、或定制发布流程。

1. 克隆并还原依赖。
```bash
git clone https://github.com/CedaryCat/UnifierTSL.git
cd UnifierTSL
dotnet restore src/UnifierTSL.slnx
```

2. 先构建一次。
```bash
dotnet build src/UnifierTSL.slnx -c Debug
```

3. 运行 Publisher 生成本地产物。
```bash
dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- \
  --rid win-x64 \
  --excluded-plugins ExamplePlugin,ExamplePlugin.Features
```

4. 直接冒烟启动启动器。
```bash
dotnet run --project src/UnifierTSL/UnifierTSL.csproj -- \
  -port 7777 -password changeme -server "name:Dev worldname:Dev" -joinserver first
```

说明：
- Publisher 默认本地输出目录：`src/UnifierTSL.Publisher/bin/<Configuration>/net9.0/utsl-<rid>/`。
- `UnifierTSL.ConsoleClient` 不支持手工独立运行，必须由启动器注入管道参数。

## 启动器参数速查
| 参数 | 说明 | 取值 | 默认/备注 |
| --- | --- | --- | --- |
| `-listen`, `-port` | 设置协调器监听端口 | 可解析为整数 | 省略时从 STDIN 交互读取 |
| `-password` | 设置客户端连接密码 | 任意字符串 | 省略时从 STDIN 交互读取 |
| `-autostart`, `-addserver`, `-server` | 增加一个或多个服务器定义 | 可重复，值为 `key:value` 组合 | 非法定义会被拒绝并提示 |
| `-joinserver` | 默认入服策略（最低优先级） | `first`, `f`, `random`, `rnd`, `r` | 仅第一个有效值生效 |
| `-culture`, `-lang`, `-language` | 覆盖 Terraria 语言 | 旧版 culture ID 或 culture 名称 | 未命中时回退到主机文化匹配 |

重要提示：
- 除非插件已通过 `EventHub.Coordinator.SwitchJoinServer` 接管路由，否则请显式传 `-joinserver first`（或 `random`）。

## 服务器定义键
每个 `-server` 参数值都是由空格分隔的 `key:value` 列表，解析入口为 `UnifierApi.AutoStartServer`。

| 键 | 作用 | 支持值 | 备注 |
| --- | --- | --- | --- |
| `name` | 服务器标识名 | 唯一字符串 | 必填 |
| `worldname` | 世界名（加载或生成） | 唯一字符串 | 必填 |
| `seed` | 世界种子 | 任意字符串 | 选填 |
| `gamemode` / `difficulty` | 世界难度 | `0`-`3`、`normal`、`expert`、`master`、`creative`、`n`、`e`、`m`、`c` | 默认 `2` |
| `size` | 世界尺寸 | `1`-`3`、`small`、`medium`、`large`、`s`、`m`、`l` | 默认 `3` |
| `evil` | 邪恶类型 | `0`-`2`、`random`、`corruption`、`crimson` | 默认 `0` |

## 发布产物生命周期
### Publisher 本地初始输出
Publisher 本地输出的是目录结构，不会自动打成 zip：

```
utsl-<rid>/
  UnifierTSL(.exe)
  UnifierTSL.pdb
  app/
    UnifierTSL.ConsoleClient(.exe)
    UnifierTSL.ConsoleClient.pdb
  i18n/
  lib/
  plugins/
    TShockAPI.dll
    TShockAPI.pdb
    CommandTeleport.dll
    CommandTeleport.pdb
  runtimes/
```

默认初始输出中不包含 `config/`。

### 首次启动后的插件重排布局
首次启动后，模块加载器会按属性（`[CoreModule]`、`[RequiresCoreModule]`、依赖声明）重排插件文件。

常见重排结果示意：

```
plugins/
  TShockAPI/
    TShockAPI.dll
    dependencies.json
    lib/
  CommandTeleport.dll
config/
  TShockAPI/
  CommandTeleport/
```

`dependencies.json` 会在模块加载与依赖分发阶段生成或更新。

### CI 构建产物与 Release 命名
GitHub Actions 分两层命名：
- Workflow artifact：`utsl-<rid>-<semver>`
- Release 归档：
  - Windows：`utsl-<rid>-v<semver>.zip`
  - Linux/macOS：`utsl-<rid>-v<semver>.tar.gz`

## Publisher CLI 参考
| 参数 | 说明 | 取值 | 备注 |
| --- | --- | --- | --- |
| `--rid` | 目标运行时标识 | 必填且只能一个，如 `win-x64`、`linux-x64`、`osx-x64` | 缺失或重复会抛异常 |
| `--excluded-plugins` | 排除的插件项目名 | 逗号分隔，或多次传入 | 构建前会 trim |
| `--output-path` | 输出基路径 | 绝对或相对路径 | 默认 `src/UnifierTSL.Publisher/bin/<Configuration>/net9.0` |
| `--use-rid-folder` | 是否在输出路径下附加 `utsl-<rid>` 子目录 | `true` / `false` | 默认 `true` |
| `--clean-output-dir` | 输出前是否清空目录 | `yes`/`no` 或 `true`/`false` | 默认 `true` |

Publisher 产物为 framework-dependent（在 app-tools 发布步骤中 `SelfContained=false`）。

## 项目结构
- `src/UnifierTSL.slnx`：启动器、控制台客户端、发布器、样例插件。
- `src/UnifierTSL/`：运行时入口与核心子系统（`Module/`、`PluginHost/`、`Servers/`、`Network/`、`Logging/`）。
- `src/UnifierTSL.ConsoleClient/`：每实例控制台客户端与命名管道协议。
- `src/UnifierTSL.Publisher/`：本地打包逻辑。
- `src/Plugins/`：维护中的示例与内置模块（`ExamplePlugin`、`ExamplePlugin.Features`、`CommandTeleport`、`TShockAPI`）。
- `docs/`：项目文档。

## 插件特性概览
- 将插件程序集放入 `plugins/` 即可进入发现流程。
- 带 `[CoreModule]` 的模块会被整理到独立目录。
- 带 `[RequiresCoreModule("...")]` 的模块会复用核心模块上下文加载。
- 声明依赖的模块会在本地 `lib/` 分发依赖，并以 `dependencies.json` 跟踪。
- 配置通过注册器进入 `config/<PluginName>/`。
- 配置外部变更自动重载是按插件显式启用（`TriggerReloadOnExternalChange(true)`）。

## 开发者参考
常用命令：

```bash
dotnet restore src/UnifierTSL.slnx
dotnet build src/UnifierTSL.slnx -c Debug
dotnet run --project src/UnifierTSL/UnifierTSL.csproj -- -port 7777 -password changeme -joinserver first
dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64
dotnet test src/UnifierTSL.slnx
```

当前状态：
- 仓库尚未内置自动化测试项目。

## 延伸阅读
- [开发者总览](./dev-overview.zh-cn.md)
- [插件开发与迁移指南](./dev-plugin.zh-cn.md)
- [OTAPI Unified Server Process](https://github.com/CedaryCat/OTAPI.UnifiedServerProcess)
- [TShock 上游仓库](https://github.com/Pryaxis/TShock)
- [DeepWiki AI 分析](https://deepwiki.com/CedaryCat/UnifierTSL)（仅供参考）
