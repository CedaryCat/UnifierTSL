# UnifierTSL

> 语言：[English](../README.md) | [简体中文](./README.zh-cn.md)

<p align="center">
  <em>基于 OTAPI USP 的实验友好型《泰拉瑞亚》服务器启动器，集成每实例控制台、发布助手与插件脚手架。</em>
</p>

<p align="center">
  <a href="../src/UnifierTSL.sln"><img alt=".NET 9.0" src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white"></a>
  <a href="../LICENSE"><img alt="许可证：GPL-3.0" src="https://img.shields.io/badge/License-GPL--3.0-green"></a>
  <img alt="平台" src="https://img.shields.io/badge/Platforms-Windows%20%7C%20Linux%20%7C%20macOS-2ea44f">
</p>

---

## 概览
UnifierTSL 致力于把 OTAPI 的 Unified Server Process 打包成顺手的工作流，让你无需反复调端口或维护脆弱脚本，就能尝试在同一主机上托管多个泰拉瑞亚世界。启动器会保持各组件生命周期的一致、自动路由玩家，并为每个世界启动独立的控制台客户端，确保输入输出互不干扰。

当前的解决方案集成了启动器、发布器、控制台客户端和示例插件。公共服务驻留于 `UnifiedServerCoordinator`，事件流经 `UnifierApi.EventHub`，而 `PluginHost.PluginOrchestrator` 负责探索在不触碰核心启动器代码的前提下实现热插拔式插件集成。

## 快速一览
- 支持在同一进程中运行多个泰拉瑞亚世界，并为每个实例提供独立的控制台窗口。
- 附带轻量级发布器，可打包示例插件与配置，实现可复用的分发产物。
- 提供正在演进的插件开发工具链，涵盖热重载、依赖分发与元数据辅助。
- 借助 .NET 9.0 工具链同时瞄准 Windows、Linux（x64/ARM）与 macOS。

> 小提示：想在五分钟内启动一个世界，直接跳到[快速开始](#快速开始)章节。

## 目录
- [概览](#概览)
- [快速一览](#快速一览)
- [核心能力](#核心能力)
- [技术栈概览](#技术栈概览)
- [系统协同方式](#系统协同方式)
  - [启动流程概览](#启动流程概览)
- [快速开始](#快速开始)
  - [选择你的方式](#选择你的方式)
  - [使用发布包](#使用发布包)
  - [从源码运行](#从源码运行)
- [启动器速查](#启动器速查)
- [服务器定义参数](#服务器定义参数)
- [发布包结构](#发布包结构)
- [项目结构](#项目结构)
- [自带组件](#自带组件)
- [插件特性概览](#插件特性概览)
- [开发者参考](#开发者参考)
- [Publisher 命令行](#publisher-命令行)
- [延伸阅读](#延伸阅读)

## 核心能力
- **多世界核心**：在单个进程中实验托管多个相互隔离的世界，配合状态跟踪与资源标注。
- **可调度的控制室**：在玩家在线时添加或撤下服务器上下文，努力保证所有人仍共享同一监听端口。
- **插件支持**：从结构化的 `plugins/` 目录加载 .NET 插件，支持元数据、JSON/TOML 配置与正在演进的热重载控制。
- **托管模块加载**：可收集的 `ModuleLoadContext` 实例提供热重载、依赖共享、自动 NuGet 下载与平台感知的原生解析。
- **统一日志**：`UnifierApi.LogCore` 暴露可插拔的过滤器、写入器与元数据注入器，可在不停机的情况下重定向或增强诊断信息。
- **内置 TShock 发行版**：包含针对 USP 调整过的 TShock 5.2.2，熟悉的权限、REST 端点、SSC 与命令都触手可及。
- **独立控制台**：为每个服务器上下文通过命名管道启动一个控制台客户端，保持输出着色与输入通道清晰。
- **发布流程**：帮助生成含 RID 资产、插件和默认配置的可复现包，方便稳定部署。
- **跨平台目标**：借助 .NET SDK 发布 `win-x64`、`linux-x64`、`linux-arm64`、`linux-arm` 与 `osx-x64`。

## 技术栈概览
想知道启动器背后用了什么？下面是关键组件，帮助你判断安装内容及其价值。

- **运行时**：基于 .NET 9.0，产出依赖框架的可执行文件，并由发布器生成特定 RID 的资源。
- **USP 核心**：构建于 OTAPI.UnifiedServerProcess 1.0.10，将原版服务器转化为统一的多世界宿主。
- **关键包**：

  | 包 | 版本 | 作用 |
  | --- | --- | --- |
  | OTAPI.USP | 1.0.13 | Unified Server Process 核心 |
  | ModFramework | 1.1.15 | 补丁阶段使用的 IL 修改框架 |
  | MonoMod.RuntimeDetour | 25.2.3 | 运行期方法钩子与跳转 |
  | Tomlyn | 0.19.0 | 启动器与插件使用的 TOML 配置解析 |
  | linq2db | 5.4.1 | 示例插件使用的数据库抽象层 |
  | Microsoft.Data.Sqlite | 9.0.0 | TShock 与示例插件使用的 SQLite 提供程序 |

> 温馨提示：升级 OTAPI 或 TShock 时记得关注它们的发布说明，确保启动器钩子、配置与插件依旧协同工作。

## 系统协同方式
可以把 UnifierTSL 看作一个协调层，让多个专职子系统保持同步：

- `UnifiedServerCoordinator` 负责客户端套接字、服务器上下文与多世界之间的包路由（`src/UnifierTSL/UnifiedServerCoordinator.cs`）。
- `UnifierApi` 提供生命周期钩子、处理参数解析，并实例化 `EventHub` 与插件编排器等公共基础设施（`src/UnifierTSL/UnifierApi.Internal.cs`）。
- `ServerContext` 描述每个托管的世界及其独立状态，确保传送与卸载互不干扰（`src/UnifierTSL/Servers/`）。
- `PluginHost.PluginOrchestrator` 负责加载插件、注册事件处理器并提供热加载机制，同时避免修改启动器代码（`src/UnifierTSL/PluginHost/`）。
- `Utilities.CLI` 为启动器与发布器解析原始参数以及嵌套的 `key:value` 对（`src/UnifierTSL/Utilities.cs`）。
- `Logging` 将 `Logger`、`RoleLogger` 与元数据注入器集中到同一条可热插拔的流水线上（`src/UnifierTSL/Logging/`），各子系统可以在复用或覆盖共享 `UnifierApi.LogCore` 的前提下克隆自己的日志器。
- `src/UnifierTSL/Network/` 下的网络补丁将 OTAPI 原语与协调器的期望桥接起来，保证 USP 数据包安全地流动。

> 想看整体流程？可以从各项目的 `Program.cs` 入手，再深入 `UnifiedServerCoordinator` 了解实例如何上线。

### 启动流程概览
启动器运行后大致会经历以下步骤：
1. 初始化程序集解析、本地化资源与 USP 网络补丁。
2. 拉起 `UnifiedServerCoordinator`、日志与公共基础设施，同时接上事件中心。
3. 按 `InitializationOrder` 递增顺序加载插件，让依赖按照既定顺序感知初始化结果。
4. 启动已配置的服务器、监听统一端口，并把控制台任务交给独立的客户端进程。

## 快速开始

> 前置条件：确认已安装 .NET 9.0 SDK（执行 `dotnet --list-sdks`），如未安装或版本过旧，可前往 <https://dotnet.microsoft.com/> 获取。

### 选择你的方式
- 想要带 TShock、示例插件和启动脚本的预制包，请选择[使用发布包](#使用发布包)。
- 需要调试插件、调整启动器或集成 CI/CD 时，选择[从源码运行](#从源码运行)；插件作者也可以只引用已发布的 NuGet 包，而无需留在仓库内。

> 命令执行目录说明：使用发布包时在解压后的目录运行命令，源码流程则默认在仓库根目录执行。

### 使用发布包
1. 下载与你平台匹配的 `utsl-<rid>.zip` 压缩包。
2. 在目标主机解压，目录中会出现 `lib/`、`plugins/`、`config/`、`app/` 等文件夹，以及对应平台的可执行文件（Windows 为 `UnifierTSL.exe`，其他平台为 `UnifierTSL`）。
3. 带参数启动服务器：
   - Windows（PowerShell）
     ```powershell
     .\UnifierTSL.exe -lang 7 -port 7777 -password changeme `
       -server "name:S1 worldname:S1 gamemode:3 size:1 evil:0 seed:\"for the worthy\"" `
       -server "name:S2 worldname:S2 gamemode:2 size:2"
     ```
   - Linux 或 macOS
     ```bash
     chmod +x UnifierTSL
     ./UnifierTSL -lang 7 -port 7777 -password changeme \
       -server "name:S1 worldname:S1 gamemode:3 size:1 evil:0 seed:\"for the worthy\"" \
       -joinserver first
     ```
4. 将自定义插件或配置放入解压目录下的 `plugins/` 与 `config/`，然后重启启动器即可生效。

> 发布渠道：若 Release 页为空，可下载最新的 GitHub Actions 构建产物，或按下文的发布流程自行生成。

### 从源码运行
当你需要审查、修改或自动化启动器时，可以按照以下步骤操作。

1. 克隆并还原依赖（已有仓库可跳过）
   ```bash
   git clone https://github.com/CedaryCat/UnifierTSL.git
   cd UnifierTSL
   dotnet restore src/UnifierTSL.sln
   ```
2. 生成发布包（推荐给运维场景）
   ```bash
   dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- \
     --rid win-x64 \
     --excluded-plugins ExamplePlugin
   ```
   - 生产环境请加上 `-c Release`。
   - 将 `win-x64` 替换为目标平台的 RID（如 `linux-x64` 或 `osx-x64`）。
   - 输出位于 `src/UnifierTSL.Publisher/bin/<Configuration>/net9.0/utsl-<RID>.zip`，即发布器产物的压缩副本。
   - 建议使用与本地 .NET 运行时一致的平台 RID，以保证生成的 AppHost 匹配本机。
3. 本地冒烟测试
   ```bash
   dotnet run --project src/UnifierTSL/UnifierTSL.csproj -- \
     -port 7777 -password changeme -server "name:Dev worldname:Dev"
   ```
   用于发布前的快速验证。
4. 观察控制台隔离效果
   - 运行第 3 步的启动器后，会为每个世界启动一个控制台客户端进程。
   - `UnifierTSL.ConsoleClient` 无法手动运行，它依赖启动器在创建进程时注入的管道参数。
> 想了解更多Publisher可控制的输出行为？参见[插件开发文档](dev-plugin.zh-cn.md#publisher发布器输出行为)的Publisher（发布器）输出行为部分

## 启动器速查
这些参数可用于安排启动器如何启动、保护与路由各个世界。

| 参数 | 说明 | 可选值 | 默认或备注 |
| --- | --- | --- | --- |
| `-listen`, `-port` | 设置 `UnifiedServerCoordinator` 的 TCP 端口 | 1 到 65535 的整数 | 若未提供，会在标准输入中反复询问直至得到合法值 |
| `-password` | 设置与客户端共享的连接密码 | 任意字符串，可带引号 | 若未提供，会在标准输入中提示 |
| `-autostart`, `-addserver`, `-server` | 排队等待启动的服务器定义 | 可重复；每个值使用下方描述的 `key:value` 对 | 配置无效时会在控制台给出警告 |
| `-joinserver` | 注册默认分配玩家的低优先级处理器 | `first`、`f`、`random`、`rnd` 或 `r` | 只采用第一个有效值 |
| `-culture`, `-lang`, `-language` | 覆盖服务器文本使用的泰拉瑞亚本地化 | `GameCulture._legacyCultures` 接受的整数 ID | 默认使用宿主配置的游戏语言 |

> 重要提醒：若没有插件通过 `EventHub.Coordinator.SwitchJoinServer` 注册默认世界，请传入 `-joinserver first`（或 `random`），否则新玩家可能无法自动进入有效世界而卡在选择界面。

## 服务器定义参数
每个 `-server`（或 `-autostart`、`-addserver`）值都是由空格分隔的 `key:value` 列表。支持的键在 `UnifierApi.AutoStartServer` 中解析。

> 快捷提示：包含空格的值（如世界名或种子）请使用引号包裹，避免被 shell 拆分。

| 键 | 用途 | 可选值 | 备注 |
| --- | --- | --- | --- |
| `name` | 友好的服务器标识 | 唯一字符串 | 必填；冲突会跳过该条目 |
| `worldname` | 要创建或加载的世界名 | 唯一字符串 | 必填；若和现有世界冲突，该条会被终止 |
| `seed` | 生成种子 | 任意字符串 | 选填；默认为空 |
| `gamemode` / `difficulty` | 世界难度 | `0`-`3`、`normal`、`expert`、`master`、`creative`，或 `n`、`e`、`m`、`c` | 默认为 `2`（大师） |
| `size` | 世界尺寸 | `1`-`3`、`small`、`medium`、`large`，或 `s`、`m`、`l` | 默认为 `3`（大型） |
| `evil` | 邪恶类型 | `0`-`2`、`random`、`corruption`、`crimson` | 默认为 `0`（随机） |

## 发布包结构
发布器会在 `bin/<Config>/net9.0/utsl-<rid>`目录构建发布包，展开后大致结构如下，可用于快速检查：

```
UnifierTSL.exe / UnifierTSL   # 针对平台的启动入口
app/
  UnifierTSL.ConsoleClient.*  # 为每个世界生成的控制台客户端
config/
  TShockAPI/                  # TShock 配置、数据库、SSC、MOTD、规则
  ExamplePlugin/              # 示例插件配置（不用可删除）
  CommandTeleport/            # 其他插件的状态与配置
lib/                          # 共享的托管库
plugins/
  ExamplePlugin.dll
  CommandTeleport.dll
  TShockAPI/
    TShockAPI.dll
    dependencies.json         # 模块加载器解析依赖后生成的记录
runtimes/                     # 各平台的原生资源（如 win-x64/）
start.bat / launch.sh         # 展示命令行用法的辅助脚本（可按需自建）
```

> 注意：请保持 `plugins/` 与 `config/` 中的内容一一对应，方便插件在部署后找到配置与依赖。

## 项目结构
浏览仓库时，可从以下目录着手：
- `src/UnifierTSL.sln` 汇总了启动器、控制台客户端、发布器与示例插件。
- `src/UnifierTSL/` 承载运行时入口，以及 `Module/`、`PluginHost/`、`Servers/`、`Network/` 等子系统。
- `src/UnifierTSL.ConsoleClient/` 包含每实例控制台隔离客户端及其命名管道协议，启动器负责调用它。
- `src/UnifierTSL.Publisher/` 将包输出到 `bin/<Config>/net9.0/utsl-<rid>.zip`，并包含目标 RID 的资源。
- `src/Plugins/` 提供已维护的示例（`ExamplePlugin`、`CommandTeleport`、`TShockAPI`），可作为新集成的脚手架。
- `doc/` 存放项目文档，包括本文与设计说明。

## 自带组件
UnifierTSL 随仓库一同提供以下配套项目，帮助你避免拆装各类工具：
- 启动器（`src/UnifierTSL/`）：负责 USP 托管、世界生命周期管理与通过 `UnifiedServerCoordinator` 的插件引导。
- 控制台客户端（`src/UnifierTSL.ConsoleClient/`）：借助命名管道隔离各世界的控制台 I/O，确保并行会话可读。
- 日志核心（`src/UnifierTSL/Logging/`）：集中管理共享 `Logger`，插件可通过 `UnifierApi.CreateLogger` 替换或扩展过滤器、写入器与元数据注入器。
- 发布器（`src/UnifierTSL.Publisher/`）：生成自包含的分发包，并允许按需排除插件。

## 插件特性概览
把插件 DLL 丢进 `plugins/` 即可被 UnifierTSL 识别，不过加载器会立即整理文件，方便运行时管理热重载与依赖。如果你发现文件不在原位置，请检查下面提到的子目录——UnifierTSL 只是把它放进了期望的布局。

### 运行时模块类型
- **核心模块**（`[CoreModule]`）：作为相关代码的锚点。加载器会创建 `plugins/<ModuleName>/`，将 DLL 存放其中，并赋予可回收的 `AssemblyLoadContext`。核心模块可通过 `[ModuleDependencies]` 声明 NuGet 或内嵌依赖，加载器会将依赖解压到 `lib/` 并在 `dependencies.json` 中记录版本。
- **依赖模块**（`[RequiresCoreModule("MainName")]`）：必须指向已存在的核心模块。它们会被移动到核心模块目录，并通过同一 `AssemblyLoadContext` 加载，自动复用核心模块声明的依赖，无法额外声明新的依赖。
- **独立模块**（没有 `[CoreModule]` 或 `[RequiresCoreModule]`）：在自己的上下文中加载，不能被依赖模块引用。除此之外行为与核心模块类似——若声明了 `[ModuleDependencies]`，会拥有独立目录与依赖分发；若未声明，则保持在原位置。

### `plugins/` 中发生了什么
- 初次扫描会同时遍历根目录与现有子目录。若检测到核心属性、依赖属性或 `RequiresCoreModule`，加载器会将 DLL（及其 PDB）移动到目标目录再加载。
- 具备依赖声明的核心模块与独立模块最终会位于 `plugins/<ModuleName>/<ModuleName>.dll`，并在需要时附带 `lib/`。
- 依赖模块会被放在核心模块旁，同时保留自己的文件名，因此像 `ExamplePlugin.Features.dll` 这样的插件既跟随 ExamplePlugin 目录，也保持易于识别。
- 未声明依赖的独立模块会停留在原位置，直到你添加元数据改变加载方式。

### 配置文件
- 每个插件在 `config/` 下拥有独立目录，名称为 DLL 不含扩展名的部分。这就是为何 `ExamplePlugin.Features.dll` 的配置位于 `config/ExamplePlugin.Features/`，而非 `config/ExamplePlugin/`。
- 插件可以决定手动编辑是否立即生效。配置系统会触发文件变更通知，但自动重载是可选行为；许多插件只有在调用 `TriggerReloadOnExternalChange(true)` 后，或通过游戏内命令，才会持久化新值。示例插件默认开启即时重载并记录变更，集成的 TShock 也会使用 UnifierTSL 的自动重载（上游 TShock 仍按自己的流程运行）。
- 如果某个插件没有自动重载，请重启该插件或启动器来安全应用修改。

### 内置示例
- **ExamplePlugin.dll**：核心模块，提供配置辅助与可供卫星插件复用的工具。
- **ExamplePlugin.Features.dll**：依赖模块，扩展主插件，只会在 ExamplePlugin 初始化完成后加载。
- **CommandTeleport.dll**：独立插件，可挂钩多世界协调器，既能独立运行也可声明自己的依赖。
- **TShockAPI.dll**：核心模块，在自身 `lib/` 目录中准备数据库驱动与 HTTP 组件，供依赖模块或运行时程序集解析使用。

### 将程序集作为共享依赖
- 可以直接把托管程序集（例如 `Newtonsoft.Json.dll`）丢进 `plugins/`，即便它们不是插件入口，加载器也会像处理其他模块一样发现它们。
- 当其他模块请求某个程序集时，UnifierTSL 会优先匹配已加载模块中的精确版本；若找不到，就查看请求模块声明的依赖；最后才在已整理的程序集里按名称匹配。这样可以避免在多个 `AssemblyLoadContext` 中重复加载同一 DLL，并有助于降低共享库的内存占用。

## 开发者参考
进行开发时，可以将以下命令加入常用列表：
- `dotnet restore src/UnifierTSL.sln` 还原全部解决方案依赖。
- `dotnet build src/UnifierTSL.sln -c Debug [-warnaserror]` 编译整个工作区，并可选开启分析器视为错误。
- `dotnet run --project src/UnifierTSL/UnifierTSL.csproj` 启动服务器；不带参数则使用交互式提示，或在 `--` 之后附加 `-port 7777` 等 CLI 参数。
- `dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64` 生成分发包，输出位于 `src/UnifierTSL.Publisher/bin/<Config>/net9.0/utsl-win-x64.zip`；需要时可加 `--excluded-plugins ExamplePlugin`。
- `dotnet run --project src/UnifierTSL.ConsoleClient/UnifierTSL.ConsoleClient.csproj` 需要启动器注入管道参数，建议直接运行启动器观察控制台隔离。

> 测试路线图：目前尚无自动化测试。可以先执行 `dotnet new xunit -n UnifierTSL.Tests -o tests/UnifierTSL.Tests` 创建测试项目，按运行时代码命名空间组织测试（例如 `Module/` 对应 `ModuleTests/`），并在提交新功能后运行 `dotnet test src/UnifierTSL.sln`。

## Publisher 命令行
借助以下参数即可在不改代码的情况下调整发布内容：

| 参数 | 说明 | 可选值 | 备注 |
| --- | --- | --- | --- |
| `--rid` | 传递给 `CoreAppBuilder`、`AppToolsPublisher` 与插件打包器的目标运行时标识 | 必填，单个值，如 `win-x64`、`linux-x64`、`osx-x64` | 省略或重复提供都会在 `Program.cs` 中抛出异常 |
| `--excluded-plugins` | 要跳过的插件名，以逗号分隔 | 选填；支持多次出现或逗号列表 | 在调用 `PluginsBuilder` 前会裁剪并去除空白 |

> 小技巧：在 CI 中结合 `--rid` 与环境变量 `DOTNET_CLI_TELEMETRY_OPTOUT`，可让日志更干净、构建结果更可复现。

## 延伸阅读
进一步了解可参考：
- [开发者概览](dev-overview.md)，深入了解架构、子系统与实现模式的技术细节。
- [插件开发指南](dev-plugin.md)，全面掌握插件开发、配置管理与热重载机制。
- [OTAPI Unified Server Process 文档](https://github.com/CedaryCat/OTAPI.UnifiedServerProcess)，了解多服务器运行模型。
- [TShock 上游仓库](https://github.com/Pryaxis/TShock)，掌握权限、REST 管理、SSC 与运营最佳实践。
- [DeepWiki AI 分析](https://deepwiki.com/CedaryCat/UnifierTSL)，AI 生成的项目探索（仅供参考，大体上准确）。

