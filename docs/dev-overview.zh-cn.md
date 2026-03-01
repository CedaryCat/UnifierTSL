# UnifierTSL 开发者概览

欢迎！这份文档会带你快速过一遍 UnifierTSL 运行时在 OTAPI Unified Server Process（USP）之上的整体构成：关键子系统、它们怎么协作，以及你可以直接用的公共 API。如果你还没看过 [README](README.zh-cn.md) 或 USP 的 [Developer-Guide.md](https://github.com/CedaryCat/OTAPI.UnifiedServerProcess/blob/main/docs/Developer-Guide.md)，建议先从那两份开始。

## 快速导航

- [1. 运行时架构](#1-runtime-architecture)
  - [1.1 分层](#11-layering)
  - [1.2 启动流程](#12-boot-flow)
  - [1.3 主要组件](#13-major-components)
- [2. 核心服务和子系统](#2-core-services--subsystems)
  - [2.1 事件中心](#21-event-hub)
  - [2.2 模块系统](#22-module-system)
  - [2.3 插件宿主编排](#23-plugin-host-orchestration)
  - [2.4 网络和协调器](#24-networking--coordinator)
  - [2.5 日志记录基础设施](#25-logging-infrastructure)
  - [2.6 配置服务](#26-configuration-service)
- [3. USP 集成点](#3-usp-integration-points)
- [4. 公共 API 接口](#4-public-api-surface)
  - [4.1 门面 (`UnifierApi`)](#41-facade-unifierapi)
  - [4.2 事件载荷与辅助类型](#42-event-payloads--helpers)
  - [4.3 模块和插件类型](#43-module--plugin-types)
  - [4.4 网络 API](#44-networking-apis)
  - [4.5 日志与诊断](#45-logging--diagnostics)
- [5. 运行时生命周期和操作](#5-runtime-lifecycle--operations)
  - [5.1 启动顺序](#51-startup-sequence)
  - [5.2 运行时操作](#52-runtime-operations)
  - [5.3 关机和重新加载](#53-shutdown--reload)
  - [5.4 诊断](#54-diagnostics)
- [6. 可扩展性指南和最佳实践](#6-extensibility-guidelines--best-practices)

<a id="1-runtime-architecture"></a>
## 1. 运行时架构

<a id="11-layering"></a>
### 1.1 分层
- **USP (OTAPI.UnifiedServerProcess)** – 这是从 OTAPI 补丁演进而来的服务端运行时层。它提供按服务器实例隔离的上下文（`RootContext`），并暴露 TrProtocol 数据包模型、可 detour 的挂钩等运行时契约。Unifier 对 Terraria 状态的运行时访问都应经过这一层。
- **UnifierTSL Core** – 启动器本身，加上编排、多服务器协调、日志记录、配置、模块加载和 `Plugin Host（插件宿主）`。
- **模块和插件** – 你的程序集，暂存于 `plugins/` 下。它们可以是核心宿主或功能卫星，并且可以嵌入依赖项（托管、本机、NuGet）以便加载程序自动提取。
- **控制台客户端/发布者** – 与运行时并存并共享相同子系统的工具项目。

<a id="12-boot-flow"></a>
### 1.2 启动流程
1. `Program.cs` 调用 `UnifierApi.HandleCommandLinePreRun(args)`，随后调用 `UnifierApi.PrepareRuntime(args)` 解析启动器覆盖参数、加载 `config/config.json`、合并启动设置并配置持久日志后端。
2. `Initializer.Initialize()` 与 `UnifierApi.InitializeCore()` 设置全局服务（日志记录、事件中心、模块加载器）并初始化 `PluginOrchestrator`。
3. 通过 `ModuleAssemblyLoader` 发现并预加载模块 — 暂存程序集并提取依赖 blob。
4. 插件宿主（内置 .NET 宿主 + 从已加载模块中发现并通过 `[PluginHost(...)]` 标注的自定义宿主）负责发现、加载和初始化插件。
5. `UnifierApi.CompleteLauncherInitialization()` 补全缺失的交互式端口/密码输入，同步最终生效的运行时快照，并触发 `EventHub.Launcher.InitializedEvent`。
6. `UnifiedServerCoordinator` 打开侦听套接字并为每个配置的世界启动 `ServerContext`。
7. 协调器成功启动后，`UnifierApi.StartRootConfigMonitoring()` 才会启用启动器根配置的热重载监视。
8. 事件桥（聊天、游戏、协调器、网络游戏、服务器）通过 detour 挂接到 USP/Terraria，并将事件统一送入 `EventHub`。

<a id="13-major-components"></a>
### 1.3 主要组件
- `UnifierApi` – 获取记录器、事件、插件宿主和窗口标题助手的主要入口点。
- `UnifiedServerCoordinator` – 管理共享泰拉瑞亚状态和连接生命周期的多服务器路由器。
- `ServerContext` – 每个世界的 USP `RootContext` 子类，并接入日志、数据包接收器和扩展插槽。
- `PluginOrchestrator` + 宿主 – 处理插件发现、加载、初始化排序和关闭/卸载。
- `ModuleAssemblyLoader` – 负责模块暂存、依赖项提取、可收集的加载上下文和卸载顺序。
- `EventHub` – 中央事件注册表，将 MonoMod detour 接入按优先级排序的事件管道。
- `Logging` 子系统 – 轻量级、分配友好的日志记录，具有元数据注入和可插拔写入器。

<a id="2-core-services--subsystems"></a>
## 2. 核心服务和子系统

<a id="21-event-hub"></a>
### 2.1 事件中心

事件系统是 UnifierTSL 发布/订阅（pub/sub）模型的核心：一条高性能、按优先级排序、零堆分配且类型安全的事件管道。

<details>
<summary><strong>展开事件中心实现细节深挖</strong></summary>

#### 架构概述

**`EventHub` (src/UnifierTSL/EventHub.cs)** 将所有事件提供程序收集在一处，并按域分组：
```csharp
public class EventHub
{
    public readonly LauncherEventHandler Launcher = new();
    public readonly ChatHandler Chat = new();
    public readonly CoordinatorEventBridge Coordinator = new();
    public readonly GameEventBridge Game = new();
    public readonly NetplayEventBridge Netplay = new();
    public readonly ServerEventBridge Server = new();
}
```

你可以访问如下事件：`UnifierApi.EventHub.Game.PreUpdate`、`UnifierApi.EventHub.Chat.MessageEvent` 等。

一旦启动器参数最终确定（包括交互式提示），`UnifierApi.EventHub.Launcher.InitializedEvent` 就会从 `UnifierApi.CompleteLauncherInitialization()` 触发——就在 `UnifiedServerCoordinator.Launch(...)` 开始接受客户端之前。

#### 事件提供者类型

有**四种提供程序类型**，每种类型都针对可变性和取消的不同需求进行了调整：

|提供商类型 |事件数据|取消 |使用案例|
|--------------|------------|--------------|----------|
| `ValueEventProvider<T>` |可变 (`ref T`) |是（`Handled` 标志）|处理程序修改数据并可以取消操作（例如聊天命令、传输）的事件 |
| `ReadonlyEventProvider<T>` |不可变 (`in T`) |是（`Handled` 标志）|处理程序检查数据并可以否决操作（例如连接验证）的事件 |
| `ValueEventNoCancelProvider<T>` |可变 (`ref T`) |没有 |处理程序可能需要修改共享状态的信息事件 |
| `ReadonlyEventNoCancelProvider<T>` |不可变 (`in T`) |没有 |用于生命周期/遥测的纯通知事件（例如 `PreUpdate`、`PostUpdate`）|

所有事件参数都是 `ref struct` 类型，存在于堆栈中 — 无堆分配，无 GC 压力。

#### 优先级系统与处理程序注册

处理程序以**优先级升序**运行（数字越小=优先级越高）：
```csharp
public enum HandlerPriority : byte
{
    Highest = 0,
    VeryHigh = 10,
    Higher = 20,
    High = 30,
    AboveNormal = 40,
    Normal = 50,      // 默认
    BelowNormal = 60,
    Low = 70,
    Lower = 80,
    VeryLow = 90,
    Lowest = 100
}
```

**注册API**：
```csharp
// 基础注册（ValueEventProvider 的 Normal 优先级）
UnifierApi.EventHub.Chat.ChatEvent.Register(OnChat);

// 显式指定优先级
UnifierApi.EventHub.Netplay.ConnectEvent.Register(OnConnect, HandlerPriority.Higher);

// 使用过滤选项（仅在已处理时运行）
UnifierApi.EventHub.Game.GameHardmodeTileUpdate.Register(OnTileUpdate,
    HandlerPriority.Low, FilterEventOption.Handled);

// 取消注册（传入同一委托引用）
UnifierApi.EventHub.Chat.ChatEvent.UnRegister(OnChat);
```

**底层**（src/UnifierTSL/Events/Core/ValueEventBaseProvider.cs:28-68）：
- 使用**volatile 快照数组**，因此调用期间的读取是无锁的
- **二分搜索插入**使处理程序自动按优先级排序
- **写入时复制**：注册创建一个新数组，因此对旧快照的持续调用不会中断
- 仅修改受 `Lock _sync` 保护

#### 过滤和取消机制

**FilterEventOption** 根据事件状态控制处理程序执行：
```csharp
public enum FilterEventOption : byte
{
    Normal = 1,      // 仅在未处理时执行
    Handled = 2,     // 仅在已处理时执行（例如清理/日志）
    All = 3          // 始终执行（Normal | Handled）
}
```

**取消模式**：
- `Handled = true`：将事件标记为“已消耗”（概念上取消操作）
- `StopPropagation = true`：停止执行剩余的处理程序
- 不同的提供商对 `Handled` 的解释不同：
  - `ReadonlyEventProvider`：向调用者返回布尔值 (`out bool handled`)
  - `ValueEventProvider`：调用者通过 `Invoke(ref data, out bool handled)` 的 `out bool handled` 获取取消结果（由处理程序设置 `args.Handled`）
  - 不可取消提供商：未暴露 `Handled` 标志

**示例 - 聊天命令拦截**：
```csharp
UnifierApi.EventHub.Chat.MessageEvent.Register(
    (ref ReadonlyEventArgs<MessageEvent> args) =>
    {
        if (args.Content.Text.StartsWith("!help"))
        {
            SendHelpText(args.Content.Sender);
            args.Handled = true;  // 阻止后续处理
        }
    },
    HandlerPriority.Higher);
```

#### 事件桥 — MonoMod 集成

事件桥是 **MonoMod 运行时 detour** 与事件系统之间的粘合层，它们会把底层挂钩转换为类型化事件调用：

**GameEventBridge** (src/UnifierTSL/Events/Handlers/GameEventBridge.cs)：
```csharp
public class GameEventBridge
{
    public readonly ReadonlyEventNoCancelProvider<ServerEvent> GameInitialize = new();
    public readonly ReadonlyEventNoCancelProvider<ServerEvent> GamePostInitialize = new();
    public readonly ReadonlyEventNoCancelProvider<ServerEvent> PreUpdate = new();
    public readonly ReadonlyEventNoCancelProvider<ServerEvent> PostUpdate = new();
    public readonly ReadonlyEventProvider<GameHardmodeTileUpdateEvent> GameHardmodeTileUpdate = new();

    public GameEventBridge() {
        On.Terraria.Main.Initialize += OnInitialize;
        On.Terraria.NetplaySystemContext.StartServer += OnStartServer;
        On.Terraria.Main.Update += OnUpdate;
        On.OTAPI.HooksSystemContext.WorldGenSystemContext.InvokeHardmodeTilePlace += OnHardmodeTilePlace;
        On.OTAPI.HooksSystemContext.WorldGenSystemContext.InvokeHardmodeTileUpdate += OnHardmodeTileUpdate;
    }

    private void OnInitialize(...) {
        GameInitialize.Invoke(new(root.ToServer())); // 在 Terraria.Main.Initialize 原始逻辑之前
        orig(self, root);
    }

    private void OnStartServer(...) {
        orig(self);
        GamePostInitialize.Invoke(new(self.root.ToServer())); // 在 NetplaySystemContext.StartServer 之后
    }

    private void OnUpdate(On.Terraria.Main.orig_Update orig, Main self, RootContext root, GameTime gameTime) {
        ServerEvent data = new(root.ToServer());
        PreUpdate.Invoke(data);      // 原始逻辑前
        orig(self, root, gameTime);  // 执行 Terraria 原始逻辑
        PostUpdate.Invoke(data);     // 原始逻辑后
    }

    private bool OnHardmodeTilePlace(...) {
        GameHardmodeTileUpdateEvent data = new(x, y, type, self.root.ToServer());
        GameHardmodeTileUpdate.Invoke(data, out bool handled);
        return !handled;
    }

    private bool OnHardmodeTileUpdate(...) {
        GameHardmodeTileUpdateEvent data = new(x, y, type, self.root.ToServer());
        GameHardmodeTileUpdate.Invoke(data, out bool handled);
        return !handled;  // 若已处理则返回 false 以取消
    }
}
```
`GameHardmodeTileUpdate` 会把原始 OTAPI 提供的两个困难模式事件（`HardmodeTilePlace` 和 `HardmodeTileUpdate`）聚合到同一个事件提供程序里。
这两个 hook 对应 Terraria 困难模式下的图格感染/生长路径（例如邪恶/神圣蔓延与水晶碎块生长），因此可以在一个处理器里统一策略。
USP 上下文化后，这两个事件从静态入口变成上下文实例上的成员，可按实例订阅，但不利于全局统一注册。通过 MonoMod detour 去 hook 其事件入口函数（`InvokeHardmodeTilePlace` 和 `InvokeHardmodeTileUpdate`）后，就能汇聚为一个全局事件；其中 `self` 作为上下文实例携带当前事件关联服务器的根上下文，桥接层再通过 `self.root.ToServer()` 传出 `ServerContext`。

**ChatHandler** (src/UnifierTSL/Events/Handlers/ChatHandler.cs)：
```csharp
public class ChatHandler
{
    public readonly ValueEventProvider<ChatEvent> ChatEvent = new();
    public readonly ReadonlyEventProvider<MessageEvent> MessageEvent = new();

    public ChatHandler() {
        On.Terraria.Chat.Commands.SayChatCommand.ProcessIncomingMessage += ProcessIncomingMessage;
        On.OTAPI.HooksSystemContext.MainSystemContext.InvokeCommandProcess += ProcessConsoleMessage;
    }
}
```

**NetplayEventBridge** (src/UnifierTSL/Events/Handlers/NetplayEventBridge.cs)：
- `ConnectEvent` (可取消) - 在客户端握手期间触发
- `ReceiveFullClientInfoEvent`（可取消）- 收到客户端元数据后
- `LeaveEvent`（信息性）- 客户端断开连接通知
- `SocketResetEvent`（信息性） - 套接字清理通知

**CoordinatorEventBridge** (src/UnifierTSL/Events/Handlers/CoordinatorEventBridge.cs)：
- `CheckVersion`（可变） - 在挂起的连接身份验证期间检查 `ClientHello` 版本
- `SwitchJoinServerEvent` (可变) - 选择加入玩家的目标服务器
- `ServerCheckPlayerCanJoinIn` (可变) - 在选择候选服务器之前使用的每服务器准入挂钩
- `JoinServer`（信息性） - 一旦玩家绑定到目标服务器就会引发
- `PreServerTransfer`（可取消） - 在服务器之间转移玩家之前
- `PostServerTransfer`（信息性）- 成功转移后
- `CreateSocketEvent` (可变) - 自定义套接字创建
- `Started`（信息性） - 协调器启动和启动日志记录完成后触发
- `LastPlayerLeftEvent`（信息性） - 从 `ActiveConnections > 0` 过渡到 `0` 时触发

**ServerEventBridge** (src/UnifierTSL/Events/Handlers/ServerEventBridge.cs)：
- `CreateConsoleService` (可变) - 提供自定义控制台实现
- `AddServer` / `RemoveServer`（信息性） - 服务器生命周期通知
- `ServerListChanged`（信息性） - 聚合服务器列表更改

#### 事件内容层次结构

事件载荷使用类型化接口来携带上下文：
```csharp
public interface IEventContent { }  // 基础标记接口

public interface IServerEventContent : IEventContent
{
    ServerContext Server { get; }  // 服务器作用域事件
}

public interface IPlayerEventContent : IServerEventContent
{
    int Who { get; }  // 玩家作用域事件（包含服务器上下文）
}
```

示例：
```csharp
// 基础事件（无上下文）
public readonly struct MessageEvent(...) : IEventContent { ... }

// 服务器作用域
public readonly struct ServerEvent(ServerContext server) : IServerEventContent { ... }

// 玩家作用域（继承服务器上下文）
public readonly struct LeaveEvent(int plr, ServerContext server) : IPlayerEventContent
{
    public int Who { get; } = plr;
    public ServerContext Server { get; } = server;
}
```

</details>

#### 性能特点

**处理程序调用**：
- 快照读取：O(1) 易失性访问（无锁）
- 处理程序迭代：O(n)，其中 n = 处理程序计数
- 过滤器检查：每个处理程序 O(1) 按位 AND

**内存**：
- 事件参数：堆栈分配 `ref struct` — 无堆分配
- 处理程序快照：不可变数组，GC 友好（长寿命 Gen2）
- 注册：O(log n) 二分查找 + O(n) 数组复制

**在实践中**，实际成本取决于你拥有多少个处理程序、哪些过滤器处于活动状态以及你的处理程序逻辑有多重。只要你的处理程序执行相同的操作，管道就会避免每次调用堆分配。

#### 最佳实践

1. **对共享/高频挂钩点优先接入 `EventHub`** - 它提供处理器级别的优先级、过滤和更可控的执行顺序。原始 MonoMod detour 只能按 detour 注册顺序（通常后注册先执行）组合，粒度更偏“插件级”，在多插件交互时容易出现顺序冲突。若该挂钩具有通用价值，优先向核心补充 `EventHub` 提供程序
2. **记住 `ref struct` 规则** - 事件参数无法在闭包或异步方法中捕获，因此请同步从它们中获取你需要的内容
3. **不要阻塞处理程序** — 它们在游戏线程上运行。使用 `Task.Run()` 减轻繁重的工作
4. **关闭时取消注册** — 始终在插件的 dispose hook (`DisposeAsync`/`DisposeAsync(bool isDisposing)`) 中调用 `UnRegister()` 以避免泄漏
5. **选择正确的提供商类型** — 尽可能使用只读/不可取消变体；它们更轻
6. **谨慎使用 `Highest` 优先级** - 将其保存到关键基础设施（如权限检查）

<details>
<summary><strong>展开自定义事件提供器扩展示例</strong></summary>

#### 高级：自定义事件提供程序

要添加新事件，请遵循以下模式：
```csharp
// 1. 定义事件内容结构
public readonly struct MyCustomEvent(ServerContext server, int data) : IServerEventContent
{
    public ServerContext Server { get; } = server;
    public int Data { get; } = data;
}

// 2. 在对应桥接器中创建提供程序
public class MyEventBridge
{
    public readonly ValueEventProvider<MyCustomEvent> CustomEvent = new();

    public MyEventBridge() {
        On.Terraria.Something.Method += OnMethod;
    }

    private void OnMethod(...) {
        MyCustomEvent data = new(server, 42);
        CustomEvent.Invoke(ref data, out bool handled);
        if (handled) return;  // 遵循取消标记
        // ... 原始逻辑
    }
}

// 3. 添加到 EventHub
public class EventHub
{
    public readonly MyEventBridge MyEvents = new();
}
```

有关实际示例，请查看 `src/Plugins/TShockAPI/Handlers/MiscHandler.cs` 和 `src/Plugins/CommandTeleport`。

</details>

<a id="22-module-system"></a>
### 2.2 模块系统

模块系统负责加载插件 DLL、引入依赖项、热重载以及卸载时的清理。它位于原始 DLL 和插件宿主之间，使用可收集的 `AssemblyLoadContext`，因此可以在运行时交换模块。

<details>
<summary><strong>展开模块系统实现细节深挖</strong></summary>

#### 模块类型和组织

**三个模块类别** (src/UnifierTSL/Module/ModulePreloadInfo.cs)：

1. **核心模块** (`[assembly: CoreModule]`)：
   - 相关组件的锚点
   - 获取专用子目录：`plugins/<ModuleName>/`
   - 装载于隔离的 `ModuleLoadContext` （可回收）
   - 可以通过 `[assembly: ModuleDependencies<TProvider>]` 声明依赖关系
   - 其他模块可以通过 `[assembly: RequiresCoreModule("ModuleName")]` 依赖它们

2. **卫星模块** (`[assembly: RequiresCoreModule("CoreModuleName")]`)：
   - 必须引用现有的核心模块
   - 暂存于核心模块目录：`plugins/<CoreModuleName>/SatelliteName.dll`
   - **共享核心模块的`ModuleLoadContext`**（关键：共享类型，协调卸载）
   - 无法声明自己的依赖项（从核心模块继承）
   - 核心模块初始化后加载

3. **独立模块**（无特殊属性）：
   - 留在 `plugins/` 根目录
   - 加载到隔离的 `ModuleLoadContext` 中
   - 不能作为卫星模块的依赖目标
   - 如果需要可以声明依赖项

**模块发现与暂存** (src/UnifierTSL/Module/ModuleAssemblyLoader.cs:43-175)：

加载器**在发现期间实际上并不加载程序集** - 它只是通过 `MetadataLoadContext` 读取 PE 标头：
```csharp
public ModulePreloadInfo PreloadModule(string dll)
{
    // 1. 在不加载程序集的情况下读取 PE 头
    using PEReader peReader = MetadataBlobHelpers.GetPEReader(dll);
    MetadataReader metadataReader = peReader.GetMetadataReader();

    // 2. 提取程序集名称
    AssemblyDefinition asmDef = metadataReader.GetAssemblyDefinition();
    string moduleName = metadataReader.GetString(asmDef.Name);

    // 3. 通过 PE 元数据检查特性
    bool isCoreModule = MetadataBlobHelpers.HasCustomAttribute(metadataReader, "CoreModuleAttribute");
    bool hasDependencies = MetadataBlobHelpers.HasCustomAttribute(metadataReader, "ModuleDependenciesAttribute");
    string? requiresCoreModule = TryReadAssemblyAttributeData(metadataReader, "RequiresCoreModuleAttribute");

    // 4. 确定暂存位置
    string newLocation;
    if (!hasDependencies && !isCoreModule && requiresCoreModule is null) {
        newLocation = Path.Combine(loadDirectory, fileName);  // 独立模块：保留在根目录
    } else {
        string moduleDir = Path.Combine(loadDirectory,
            (hasDependencies || isCoreModule) ? moduleName : requiresCoreModule!);
        Directory.CreateDirectory(moduleDir);
        newLocation = Path.Combine(moduleDir, Path.GetFileName(dll));
    }

    // 5. 移动文件（保留时间戳）并生成签名
    CopyFileWithTimestamps(dll, newLocation);
    CopyFileWithTimestamps(dll.Replace(".dll", ".pdb"), newLocation.Replace(".dll", ".pdb"));
    File.Delete(dll);  // 删除原文件

    return new ModulePreloadInfo(FileSignature.Generate(newLocation), ...);
}
```

`PreloadModules()` 在字典中对 `dll.Name` 发现的 DLL 进行索引。如果 `plugins/` 子目录（或子目录和根目录之间）有重复的名称，则后索引项会覆盖前项。由于根级文件在子目录之后建立索引，因此根目录中的 `plugins/<Name>.dll` 将覆盖之前在子文件夹中找到的同名文件。

**验证规则**：
- 不能同时为 `CoreModule` 和 `RequiresCoreModule`
- `RequiresCoreModule` 模块无法声明依赖项
- `RequiresCoreModule` 必须指定核心模块名称

#### 文件签名更改检测

**FileSignature** (src/UnifierTSL/FileSystem/FileSignature.cs) 通过三个级别的检查来跟踪模块更改：

```csharp
public record FileSignature(string FilePath, string Hash, DateTime LastWriteTimeUtc)
{
    // 级别 1：最快 - 检查路径 + 时间戳
    public bool QuickEquals(string filePath) {
        return FilePath == filePath && LastWriteTimeUtc == File.GetLastWriteTimeUtc(filePath);
    }

    // 级别 2：中等 - 检查时间戳 + SHA256 哈希
    public bool ExactEquals(string filePath) {
        if (LastWriteTimeUtc != File.GetLastWriteTimeUtc(filePath)) return false;
        return Hash == ComputeHash(filePath);
    }

    // 级别 3：最慢/最全面 - 仅检查 SHA256 哈希
    public bool ContentEquals(string filePath) {
        return Hash == ComputeHash(filePath);
    }
}
```

模块加载器在 `Load()` 期间使用 `FileSignature.Hash` 比较来发现更新的模块 (src/UnifierTSL/Module/ModuleAssemblyLoader.cs:204-207)。

#### ModuleLoadContext - 可收集的程序集加载

**ModuleLoadContext** (src/UnifierTSL/Module/ModuleLoadContext.cs:16) 扩展 `AssemblyLoadContext` 为：

```csharp
public class ModuleLoadContext : AssemblyLoadContext
{
    public ModuleLoadContext(FileInfo moduleFile) : base(isCollectible: true) {
        this.moduleFile = moduleFile;
        Resolving += OnResolving;
        Unloading += OnUnloading;
        ResolvingUnmanagedDll += OnResolvingUnmanagedDll;
    }
}
```

**这里重要的是**：
- **`isCollectible: true`** — 这可以让你在运行时卸载模块（一旦没有引用剩余，GC 就会收集 ALC）
- **处置操作** — 插件通过 `AddDisposeAction(Func<Task>)` 注册清理，该操作在 `Unloading` 事件期间运行
- **解析链** — 用于解析托管和本机程序集的多层回退

**程序集解析策略** (src/UnifierTSL/Module/ModuleLoadContext.cs:83-128)：

```
OnResolving(AssemblyName assemblyName)
    ↓
1. Framework assemblies? -> Load from default ALC (BCL, System.*, etc.)
    ↓
2. Host assembly (UnifierTSL.dll)? -> Return singleton
    ↓
3. UTSL core libraries? -> Resolve via AssemblyDependencyResolver
    ↓
4. Preferred shared assembly resolution (EXACT version match):
   - Search loaded modules for matching name + version
   - Register dependency via LoadedModule.Reference()
   - Return assembly from other module's ALC
    ↓
5. Module-local dependency:
   - Check {moduleDir}/lib/{assemblyName}.dll
   - Load from this context if exists
    ↓
6. Fallback shared assembly resolution (NAME-ONLY match):
   - Search loaded modules for any version
   - Try proxy loading (if requester references provider's core assembly)
   - Return assembly from other module's ALC
    ↓
7. Return null (resolution failed)
```

**非托管 DLL 解析** (src/UnifierTSL/Module/ModuleLoadContext.cs:134-155)：
- 从模块目录读取 `dependencies.json`
- 按清单条目 (`DependencyItem.FilePath`) 进行匹配，而不是在加载时探测文件系统 RID 文件夹
- 接受直接名称匹配 (`sqlite3.dll`) 和版本后缀的清单名称 (`sqlite3.1.2.3.dll`)
- 通过 `LoadUnmanagedDllFromPath()` 加载第一个未过时的清单匹配
- 当前 `LoadUnmanagedDll` 的行为是清单驱动的； RID 回退主要在依赖项提取期间较早应用（`NugetPackageFetcher.GetNativeLibsPathsAsync`、`NativeEmbeddedDependency`）

#### 依赖管理

**依赖关系声明** (src/UnifierTSL/Module/ModuleDependencyAttribute.cs)：
```csharp
[assembly: ModuleDependencies<MyDependencyProvider>]

public class MyDependencyProvider : IDependencyProvider
{
    public IReadOnlyList<ModuleDependency> GetDependencies() => [
        new NugetDependency("Newtonsoft.Json", "13.0.3", "net9.0"),
        new ManagedEmbeddedDependency(typeof(MyPlugin).Assembly, "MyPlugin.Libs.Helper.dll"),
        new NativeEmbeddedDependency(typeof(MyPlugin).Assembly, "sqlite3", new("1.0.0"))
    ];
}
```

**依赖类型**：

1. **NuGet 依赖项** (src/UnifierTSL/Module/Dependencies/NugetDependency.cs)：
   - 通过 `NugetPackageCache.ResolveDependenciesAsync()` 解决传递依赖
   - 将缺少的包下载到全局包文件夹 (`~/.nuget/packages`)
   - 提取托管库（匹配目标框架）+本机库（匹配 RID）
   - 使用惰性流返回 `LibraryEntry[]`

2. **托管嵌入式依赖项** (src/UnifierTSL/Module/Dependencies/ManagedEmbeddedDependency.cs)：
   - 通过 PE 标头从嵌入式资源读取程序集标识
   - 将嵌入的 DLL 提取到 `{moduleDir}/lib/{AssemblyName}.dll`

3. **本机嵌入式依赖项** (src/UnifierTSL/Module/Dependencies/NativeEmbeddedDependency.cs)：
   - 探测 RID 后备链以匹配嵌入资源
   - 提取到 `{moduleDir}/runtimes/{rid}/native/{libraryName}.{ext}`

**依赖项提取过程** (src/UnifierTSL/Module/ModuleAssemblyLoader.cs:368-560)：

```csharp
private bool UpdateDependencies(string dll, ModuleInfo info)
{
    // 1. 验证模块结构（必须位于具名目录）
    // 2. 加载旧的 dependencies.json
    DependenciesConfiguration prevConfig = DependenciesConfiguration.LoadDependenciesConfig(moduleDir);

    // 3. 提取新增/更新的依赖
    foreach (ModuleDependency dependency in dependencies) {
        if (dependency.Version != prevConfig.Version) {
            ImmutableArray<LibraryEntry> items = dependency.LibraryExtractor.Extract(Logger);
            // ... 按文件路径跟踪最高版本
        }
    }

    // 4. 复制文件并处理文件锁
    foreach (var (dependency, item) in highestVersion) {
        try {
            using Stream source = item.Stream.Value;
            destination = Utilities.IO.SafeFileCreate(targetPath, out Exception? ex);

            if (destination != null) {
                source.CopyTo(destination);  // 正常路径
            }
            else if (ex is IOException && FileSystemHelper.FileIsInUse(ex)) {
                // 文件被已加载程序集锁定，改为创建带版本后缀的新文件
                string versionedPath = Path.ChangeExtension(item.FilePath,
                    $"{item.Version}.{Path.GetExtension(item.FilePath)}");
                destination = Utilities.IO.SafeFileCreate(versionedPath, ...);
                source.CopyTo(destination);

                // 同时跟踪旧文件（待淘汰）与新文件（生效中）
                currentSetting.Dependencies[dependency.Name].Manifests.Add(
                    new DependencyItem(item.FilePath, item.Version) { Obsolete = true });
                currentSetting.Dependencies[dependency.Name].Manifests.Add(
                    new DependencyItem(versionedPath, item.Version));
            }
        }
        finally { destination?.Dispose(); }
    }

    // 5. 清理过时文件并保存 dependencies.json
    currentConfig.SpecificDependencyClean(moduleDir, prevConfig.Setting);
    currentConfig.Save(moduleDir);
}
```

**文件被锁定时会发生什么**：
- 加载器通过 `IOException` HResult 代码检测锁定的文件
- 它会创建一个版本控制副本：`Newtonsoft.Json.13.0.3.dll` 和 `Newtonsoft.Json.dll` 一起
- 旧文件在清单中被标记为 `Obsolete`
- 一旦文件不再锁定，下次重新启动时就会进行清理

**本机依赖项的 RID 图表** (src/UnifierTSL/Module/Dependencies/RidGraph.cs)：
- 加载嵌入的 `RuntimeIdentifierGraph.json` （NuGet 的官方 RID 图）
- RID扩展的BFS遍历：`win-x64` → [`win-x64`, `win`, `any`]
- 由提取时本机选择使用（`NugetPackageFetcher`、`NativeEmbeddedDependency`）
- `ModuleLoadContext.LoadUnmanagedDll()` 当前是清单驱动的，本身不执行 RID 回退探测

#### 模块卸载和依赖关系图

**LoadedModule** (src/UnifierTSL/Module/LoadedModule.cs) 跟踪谁依赖于谁：

```csharp
public record LoadedModule(
    ModuleLoadContext Context,
    Assembly Assembly,
    ImmutableArray<ModuleDependency> Dependencies,
    FileSignature Signature,
    LoadedModule? CoreModule)  // 核心/独立模块时为 null
{
    // 依赖此模块的模块
    public ImmutableArray<LoadedModule> DependentModules => dependentModules;

    // 此模块依赖的模块
    public ImmutableArray<LoadedModule> DependencyModules => dependencyModules;

    // 线程安全的引用跟踪
    public static void Reference(LoadedModule dependency, LoadedModule dependent) {
        ImmutableInterlocked.Update(ref dependent.dependencyModules, x => x.Add(dependency));
        ImmutableInterlocked.Update(ref dependency.dependentModules, x => x.Add(dependent));
    }
}
```

**卸载级联** (src/UnifierTSL/Module/LoadedModule.cs:50-68)：
```csharp
public void Unload()
{
    if (CoreModule is not null) return;  // 卫星模块共享 ALC，不能单独卸载
    if (Unloaded) return;

    // 递归卸载所有依赖方
    foreach (LoadedModule dependent in DependentModules) {
        if (dependent.CoreModule == this) {
            dependent.Unreference();  // 仅断开引用（会随核心模块一起卸载）
        } else {
            dependent.Unload();  // 递归级联卸载
        }
    }

    Unreference();     // 清理所有引用
    unloaded = true;
    Context.Unload();  // 触发 OnUnloading 事件 -> 执行释放动作
}
```

**有序卸载的拓扑排序** (src/UnifierTSL/Module/LoadedModule.cs:77-109)：
```csharp
// 按执行顺序获取依赖方（前序=叶到根，后序=根到叶）
public ImmutableArray<LoadedModule> GetDependentOrder(bool includeSelf, bool preorder)
{
    HashSet<LoadedModule> visited = [];  // 环检测
    Queue<LoadedModule> result = [];

    void Visit(LoadedModule module) {
        if (!visited.Add(module)) return;  // 已访问过（检测到环）

        if (preorder) result.Enqueue(module);
        foreach (var dep in module.DependentModules) Visit(dep);
        if (!preorder) result.Enqueue(module);
    }

    Visit(this);
    return result.ToImmutableArray();
}
```

**强制卸载** (src/UnifierTSL/Module/ModuleAssemblyLoader.cs:179-189)：
```csharp
public void ForceUnload(LoadedModule module)
{
    // 若是卫星模块则改为卸载核心模块（共享 ALC）
    if (module.CoreModule is not null) {
        ForceUnload(module.CoreModule);
        return;
    }

    // 按后序卸载（先依赖方，后被依赖方）
    foreach (LoadedModule m in module.GetDependentOrder(includeSelf: true, preorder: false)) {
        Logger.Debug($"Unloading module {m.Signature.FilePath}");
        m.Unload();
        moduleCache.Remove(m.Signature.FilePath, out _);
    }
}
```

#### 模块与插件的关系

**这是两个不同的事情**：

|概念|职责|关键类型 |
|---------|---------------|----------|
| **模块** |程序集加载、依赖管理、ALC 生命周期 | `LoadedModule` |
| **插件** |业务逻辑、事件处理程序、游戏集成| `IPlugin` / `PluginContainer` |

**流动**：
1. `ModuleAssemblyLoader` 暂存并加载程序集 → `LoadedModule`
2. `PluginDiscoverer` 扫描加载的模块以查找 `IPlugin` 实现 → `IPluginInfo`
3. `PluginLoader`实例化插件类→`IPlugin`实例
4. `PluginContainer` 包裹 `LoadedModule` + `IPlugin` + `PluginMetadata`
5. `PluginOrchestrator` 管理插件生命周期（初始化、关闭、卸载）

**插件发现** (src/UnifierTSL/PluginHost/Hosts/Dotnet/PluginDiscoverer.cs)：
```csharp
public IReadOnlyList<IPluginInfo> DiscoverPlugins(string pluginsDirectory)
{
    // 1. 使用模块加载器发现/暂存模块
    ModuleAssemblyLoader moduleLoader = new(pluginsDirectory);
    List<ModulePreloadInfo> modules = moduleLoader.PreloadModules(ModuleSearchMode.Any).ToList();

    // 2. 提取插件元数据（查找 IPlugin 实现）
    foreach (ModulePreloadInfo module in modules) {
        pluginInfos.AddRange(ExtractPluginInfos(module));
    }

    return pluginInfos;
}
```

**插件加载** (src/UnifierTSL/PluginHost/Hosts/Dotnet/PluginLoader.cs)：
```csharp
public IPluginContainer? LoadPlugin(IPluginInfo pluginInfo)
{
    // 1. 加载模块
    ModuleAssemblyLoader loader = new("plugins");
    if (!loader.TryLoadSpecific(info.Module, out LoadedModule? loaded, ...)) {
        return null;
    }

    // 2. 实例化插件
    Type? type = loaded.Assembly.GetType(info.EntryPoint.EntryPointString);
    IPlugin instance = (IPlugin)Activator.CreateInstance(type)!;

    // 3. 在 ALC 中注册释放动作
    loaded.Context.AddDisposeAction(async () => await instance.DisposeAsync());

    // 4. 封装为容器
    return new PluginContainer(info.Metadata, loaded, instance);
}
```

</details>

#### 最佳实践

1. **将相关功能分组到核心模块** - 卫星共享依赖项并一起卸载，从而保持干净
2. **声明你的依赖项** — 使用 `ModuleDependenciesAttribute` 而不是手动复制 DLL
3. **测试热重载** — 使用 `FileSignature.Hash` 检查来确保更新检测有效
4. **快速释放文件句柄** - 长期存在的流可能会阻止干净卸载
5. **不要在重新加载时缓存 `LoadedModule` 引用** - 它们会过时
6. **让加载程序处理 NuGet** — 传递解析是自动的
7. **嵌入多个 RID** 如果你提供本机库，或者依赖 NuGet 包 RID 探测

#### 性能说明

**加载**：成本取决于你拥有的程序集数量、依赖关系图的深度、磁盘速度以及 NuGet 缓存是否处于热状态。元数据扫描比完整负载轻，但首次 NuGet 解析或本机载荷提取可以占主导地位。

**内存**：占用空间随加载的程序集和活动 `ModuleLoadContext` 实例而变化。一旦引用被释放并且 GC 运行，卸载的模块就变得可回收。

<a id="23-plugin-host-orchestration"></a>
### 2.3 插件宿主编排
- `PluginOrchestrator` (`src/UnifierTSL/PluginHost/PluginOrchestrator.cs`) 会注册内置 `DotnetPluginHost`，并从已加载模块中发现额外的 `[PluginHost(...)]` 宿主。这一扩展点可用于引入非默认插件/脚本运行时支持。
- 如果要自定义插件宿主，请实现 `IPluginHost`，提供无参构造函数，并使用 `[PluginHost(majorApiVersion, minorApiVersion)]` 标注类型。
- 宿主准入由 `PluginOrchestrator.ApiVersion`（当前 `1.0.0`）控制：`major` 必须完全一致，宿主 `minor` 必须 ≤ 运行时 `minor`。不满足条件的宿主会被跳过并记录警告。
- `.InitializeAllAsync` 预加载模块，通过 `PluginDiscoverer` 发现插件入口点，并通过 `PluginLoader` 加载它们。
- 插件容器按 `InitializationOrder` 排序，并使用 `PluginInitInfo` 列表调用 `IPlugin.InitializeAsync`，以便你可以等待特定的依赖项。
- `ShutdownAllAsync` 和 `UnloadAllAsync` 由编排器提供；但内置 `DotnetPluginHost` 在 `ShutdownAsync`/`UnloadPluginsAsync` 上仍是 TODO 占位实现。目前释放流程主要由模块卸载路径驱动。

<a id="24-networking--coordinator"></a>
### 2.4 网络和协调器

网络层在统一监听端口下承载所有服务器 - 它将数据包路由到正确的服务器，让你以中间件方式拦截/修改它们，并使用池缓冲区来保持较低的分配。

<details>
<summary><strong>展开网络与协调器实现细节深挖</strong></summary>

#### 统一服务器协调器架构

**UnifiedServerCoordinator** (src/UnifierTSL/UnifiedServerCoordinator.cs) 集中所有服务器之间的共享网络状态：

**全局共享状态**：
```csharp
// 索引 = 客户端槽位（0-255）
Player[] players                              // 玩家实体状态
RemoteClient[] globalClients                  // 传输层套接字封装（TCP）
LocalClientSender[] clientSenders             // 每客户端数据包发送器
MessageBuffer[] globalMsgBuffers              // 接收缓冲区（256 × 服务器数）
ServerContext?[] clientCurrentlyServers       // 客户端 → 服务器映射（通过 Volatile 助手读写）
```

**连接管道**：

```
TcpClient connects to unified port (7777)
    ↓
OnConnectionAccepted(): Find empty slot (0-255)
    ↓
Create PendingConnection (pre-auth phase)
    ↓
Async receive loop: ClientHello, SendPassword, SyncPlayer, ClientUUID
    ↓
SwitchJoinServerEvent fires → plugin selects destination server
    ↓
Activate client in chosen ServerContext
    ↓
Byte processing routed via ProcessBytes hook
```

**待连接** (src/UnifierTSL/UnifiedServerCoordinator.cs:529-696)：
- 在服务器分配之前处理预身份验证数据包
- 根据 `Terraria{Main.curRelease}` 验证客户端版本（通过 `Coordinator.CheckVersion` 覆盖）
- `Coordinator.CheckVersion` 当前在 `ClientHello` 处理期间被调用两次；处理程序应该是幂等的，并避免假设单次调用的副作用
- 密码验证（如果设置了 `UnifierApi.ServerPassword`）
- 收集客户端元数据：`ClientUUID`、玩家姓名和外观
- 以 `NetworkText` 原因踢掉不兼容的客户端

**服务器传输协议** (src/UnifierTSL/UnifiedServerCoordinator.cs:290-353)：
```csharp
public static void TransferPlayerToServer(byte plr, ServerContext to, bool ignoreChecks = false)
{
    ServerContext? from = GetClientCurrentlyServer(plr);
    if (from is null || from == to) return;
    if (!to.IsRunning && !ignoreChecks) return;

    UnifierApi.EventHub.Coordinator.PreServerTransfer.Invoke(new(from, to, plr), out bool handled);
    if (handled) return;

    // 同步离开 → 切换映射 → 同步加入
    from.SyncPlayerLeaveToOthers(plr);
    from.SyncServerOfflineToPlayer(plr);
    SetClientCurrentlyServer(plr, to);
    to.SyncServerOnlineToPlayer(plr);
    to.SyncPlayerJoinToOthers(plr);

    UnifierApi.EventHub.Coordinator.PostServerTransfer.Invoke(new(from, to, plr));
}
```

**数据包路由挂钩** (src/UnifierTSL/UnifiedServerCoordinator.cs:356-416)：
```csharp
On.Terraria.NetMessageSystemContext.CheckBytes += ProcessBytes;

private static void ProcessBytes(
    On.Terraria.NetMessageSystemContext.orig_CheckBytes orig,
    NetMessageSystemContext netMsg,
    int clientIndex)
{
    ServerContext server = netMsg.root.ToServer();
    MessageBuffer buffer = globalMsgBuffers[clientIndex];
    lock (buffer) {
        // 解码包长度，然后把载荷路由到 NetPacketHandler.ProcessBytes(...)
        NetPacketHandler.ProcessBytes(server, buffer, contentStart, contentLength);
    }
}
```

#### 数据包拦截 - NetPacketHandler

**NetPacketHandler** (src/UnifierTSL/Events/Handlers/NetPacketHandler.cs) 提供具有取消和重写功能的**中间件式数据包处理**。

**处理程序注册**：
```csharp
NetPacketHandler.Register<TrProtocol.NetPackets.TileChange>(
    (ref ReceivePacketEvent<TileChange> args) =>
    {
        if (IsProtectedRegion(args.Packet.X, args.Packet.Y)) {
            args.HandleMode = PacketHandleMode.Cancel;  // 阻止数据包
        }
    },
    HandlerPriority.Highest);
```

**处理程序存储**：
- 静态数组：`Array?[] handlers = new Array[INetPacket.GlobalIDCount]`
- 每种数据包类型一个插槽（由 `TPacket.GlobalID` 索引）
- 每个槽存储 `PriorityItem<TPacket>[]` （按优先级排序）

**数据包处理流程**：
```
ProcessBytes(server, messageBuffer, contentStart, contentLength)
    ↓
1. Parse MessageID from buffer
    ↓
2. Dispatch to type-specific handler via switch(messageID):
    - ProcessPacket_F<TPacket>()    // 固定长度，非边侧特定
    - ProcessPacket_FS<TPacket>()   // 固定长度，边侧特定
    - ProcessPacket_D<TPacket>()    // 动态长度（托管）
    - ProcessPacket_DS<TPacket>()   // 动态长度，边侧特定
    ↓
3. Deserialize packet from buffer (unsafe pointers)
    ↓
4. Execute handler chain (priority-ordered)
    ↓
5. Evaluate PacketHandleMode:
    - None: Forward to MessageBuffer.GetData() (original logic)
    - Cancel: Suppress packet entirely
    - Overwrite: Re-inject via ClientPacketReceiver.AsReceiveFromSender_*()
    ↓
6. Invoke PacketProcessed callbacks
```

**PacketHandleMode** (src/UnifierTSL/Events/Handlers/NetPacketHandler.cs)：
```csharp
public enum PacketHandleMode : byte
{
    None = 0,       // 透传到 Terraria 原始逻辑
    Cancel = 1,     // 阻止数据包（不再处理）
    Overwrite = 2   // 使用修改后的数据包（通过 ClientPacketReceiver 重新注入）
}
```

**示例 - 数据包修改**：
```csharp
NetPacketHandler.Register<TrProtocol.NetPackets.TileChange>(
    (ref ReceivePacketEvent<TileChange> args) =>
    {
        // 禁止在受保护区域放置图格
        if (IsProtectedRegion(args.Packet.X, args.Packet.Y)) {
            args.HandleMode = PacketHandleMode.Cancel;
        }
    });
```

#### 数据包发送 - PacketSender 和 LocalClientSender

**PacketSender** (src/UnifierTSL/Network/PacketSender.cs) - 用于泛型特化数据包发送的抽象基类（让结构体数据包走无装箱的高性能路径）：

**API方法**：
```csharp
// 固定长度数据包（非托管结构体）
public void SendFixedPacket<TPacket>(scoped in TPacket packet)
    where TPacket : unmanaged, INonSideSpecific, INetPacket

// 动态长度数据包（托管类型）
public void SendDynamicPacket<TPacket>(scoped in TPacket packet)
    where TPacket : struct, IManagedPacket, INonSideSpecific, INetPacket

// 服务端变体（设置 IsServerSide 标记）
public void SendFixedPacket_S<TPacket>(scoped in TPacket packet) where TPacket : unmanaged, ISideSpecific, INetPacket
public void SendDynamicPacket_S<TPacket>(scoped in TPacket packet) where TPacket : struct, IManagedPacket, ISideSpecific, INetPacket

// 运行时分发（通过覆盖多数据包类型的 switch）
public void SendUnknownPacket<TPacket>(scoped in TPacket packet) where TPacket : struct, INetPacket
```

**缓冲区管理** (src/UnifierTSL/Network/SocketSender.cs:79-117)：
```csharp
private unsafe byte[] AllocateBuffer<TPacket>(in TPacket packet, out byte* ptr_start) where TPacket : INetPacket
{
    int capacity = packet is TrProtocol.NetPackets.TileSection ? 16384 : 1024;
    byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);

    fixed (byte* buf = buffer) {
        ptr_start = buf + 2;  // 预留 2 字节长度头
    }
    return buffer;
}

private void SendDataAndFreeBuffer(byte[] buffer, int totalLength, SocketSendCallback? callback)
{
    try {
        if (callback is not null) {
            Socket.AsyncSendNoCopy(buffer, 0, totalLength, callback);
        } else {
            Socket.AsyncSend(buffer, 0, totalLength);
        }
    }
    finally {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
```

**LocalClientSender** (src/UnifierTSL/Network/LocalClientSender.cs) - 每个客户端数据包发送者：
```csharp
public class LocalClientSender : SocketSender
{
    public readonly int ID;

    public RemoteClient Client => UnifiedServerCoordinator.globalClients[ID];
    public sealed override ISocket Socket => Client.Socket;

    public sealed override void Kick(NetworkText reason, bool bg = false) {
        Client.PendingTermination = true;
        Client.PendingTerminationApproved = true;
        base.Kick(reason, bg);
    }
}
```

**数据包接收模拟 - ClientPacketReceiver** (src/UnifierTSL/Network/ClientPacketReceiver.cs)：

当处理程序设置 `HandleMode = Overwrite` 时使用：
```csharp
public void AsReceiveFromSender_FixedPkt<TPacket>(LocalClientSender sender, scoped in TPacket packet)
    where TPacket : unmanaged, INetPacket, INonSideSpecific
{
    byte[] buffer = ArrayPool<byte>.Shared.Rent(sizeof(TPacket) + 4);
    try {
        unsafe {
            fixed (byte* buf = buffer) {
                void* ptr = buf + 2;
                packet.WriteContent(ref ptr);  // 序列化修改后的数据包
                short len = (short)((byte*)ptr - buf);
                *(short*)buf = len;
            }
        }

        // 以“来自发送者接收”的方式注入
        Server.NetMessage.buffer[sender.ID].GetData(Server, 0, len, out _, buffer, ...);
    }
    finally {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
```

#### TrProtocol 集成

UnifierTSL 直接使用 USP 通过 IL Merge 打包进运行时的 **TrProtocol** 数据包模型：

**数据包特征**：
- **许多数据包类型**定义为 `TrProtocol.NetPackets` 和 `TrProtocol.NetPackets.Modules` 下的结构
- **接口**：`INetPacket`、`IManagedPacket`、`ISideSpecific`、`INonSideSpecific`
- **分发策略**：优先使用“泛型 + 接口约束”生成特化路径，而不是运行时通过 `is` 把数据包装箱到接口后再分发；像 `ISideSpecific` / `INonSideSpecific` 这类成对接口就是为了在编译期表达互斥路径，避免逻辑误路由
- **序列化契约**：`ReadContent(ref void* ptr, void* ptrEnd)` 与 `WriteContent(ref void* ptr)`；读取时传入结尾指针可做边界检查，越界会抛出托管异常

对于具体的数据包模型和字段，请检查运行时附带的实际 TrProtocol 数据包结构。

#### USP 网络补丁

**UnifiedNetworkPatcher** (src/UnifierTSL/Network/UnifiedNetworkPatcher.cs:10-31) 挂钩 USP 初始化以重定向到共享数组：

```csharp
On.Terraria.NetplaySystemContext.StartServer += (orig, self) =>
{
    ServerContext server = self.root.ToServer();
    self.Connection.ResetSpecialFlags();
    self.ResetNetDiag();

    // 将每服务器数组替换为全局共享数组
    self.Clients = UnifiedServerCoordinator.globalClients;
    server.NetMessage.buffer = UnifiedServerCoordinator.globalMsgBuffers;

    // 关闭每服务器广播（由协调器处理）
    On.Terraria.NetplaySystemContext.StartBroadCasting = (_, _) => { };
On.Terraria.NetplaySystemContext.StopBroadCasting = (_, _) => { };
};
```

</details>

#### 最佳实践

1. **数据包处理程序**：
   - 尽早注册（`InitializeAsync` 或 `BeforeGlobalInitialize`）
   - 按顺序需求选择优先级；`Highest` 只留给必须最先执行的处理器（例如强制安全闸）
   - 始终在你的处置挂钩中取消注册（如果你使用的是 `BasePlugin`，则为 `DisposeAsync(bool isDisposing)`）

2. **数据包修改**：
   - 按意图选择 `Cancel` 或 `Overwrite`：`Cancel` 丢弃数据包，`Overwrite` 重新注入你修改后的数据包
   - 设置 `HandleMode = Overwrite` 时，通常也应设置 `StopPropagation = true`，除非你明确希望后续处理器继续处理改写后的包
   - 除非明确要改写，否则对数据包保持只读，避免在透传路径里做隐式修改

3. **后处理回调（`PacketProcessed`）**：
   - 适用于处理完成后的逻辑，例如指标、追踪，或依赖最终处理结果的业务逻辑
   - 根据回调中的 `PacketHandleMode`（`None`、`Cancel`、`Overwrite`）分支处理，而不是假设只有单一路径

4. **服务器传输**：
   - 把 `PreServerTransfer` 视为状态切换前的否决点
   - 把 `PostServerTransfer` 用于映射切换 + 入服同步完成后的后续逻辑
   - 通过协调器助手（`GetClientCurrentlyServer`）查询当前映射，不要长期缓存快照

5. **内存与发送器生命周期**：
   - `PacketSender` 和 `ClientPacketReceiver` 会从 `ArrayPool<byte>.Shared` 租用临时缓冲区；不要在回调/作用域之外持有这些数组
   - `UnifiedServerCoordinator` 会在 `clientSenders` 中为每个客户端槽位预分配一个 `LocalClientSender`

#### 性能说明

**数据包处理**：通过数据包 GlobalID 查找处理程序的时间复杂度为 O(1)。总成本与你为该数据包类型注册的处理程序数量及其功能有关。序列化开销取决于数据包的形状和大小。

**缓冲池**：使用 `ArrayPool<byte>.Shared` 和较大的初始缓冲区来处理大量数据包。其效果如何取决于你的流量模式和并发性。

**多服务器路由**：通过易失性支持的映射，客户端→服务器查找的时间复杂度为 O(1)。传输成本主要与同步步骤和事件处理程序有关。

<a id="25-logging-infrastructure"></a>
### 2.5 日志基础设施

日志系统是为了性能而构建的 - `LogEntry` 存在于堆栈中，元数据被池化，所有内容都通过具有服务器范围输出的可插拔写入器进行路由。`Logger` 实现还维护了一个有界的内存历史环形缓冲区，因此 sink 可以先回放最近日志再接入实时输出；启动器可附加异步持久化写入器，在后台消费并落到 `txt` 或 `sqlite` sink。

<details>
<summary><strong>展开日志基础设施实现细节深挖</strong></summary>

#### Logger 架构

**Logger** (src/UnifierTSL/Logging/Logger.cs) - 核心日志引擎：

```csharp
public class Logger
{
    public ILogFilter Filter { get; set; } = EmptyLogFilter.Instance;
    public ILogWriter Writer { get; set; } = ConsoleLogWriter.Instance;
    private ImmutableArray<ILogMetadataInjector> MetadataInjectors = [];

    public void Log(ref LogEntry entry)
    {
        // 1. 应用元数据注入器
        foreach (var injector in MetadataInjectors) {
            injector.InjectMetadata(ref entry);
        }

        // 2. 过滤检查
        if (!Filter.ShouldLog(in entry)) return;

        // 3. 写入
        Writer.Write(in entry);
    }
}
```

**LogEntry** (src/UnifierTSL/Logging/LogEntry.cs) - 结构化日志事件：
```csharp
public ref struct LogEntry
{
    public string Role { get; init; }           // 日志作用域（例如 "TShockAPI"、"Log"）
    public string? Category { get; init; }      // 子类别（例如 "ConnectionAccept"）
    public string Message { get; init; }
    public LogLevel Level { get; init; }
    public DateTime Timestamp { get; init; }
    public Exception? Exception { get; init; }
    public LogEventId? EventId { get; init; }
    public ref readonly TraceContext TraceContext { get; }

    private MetadataCollection metadata;  // 基于 ArrayPool 的有序集合

    public void SetMetadata(string key, string value) {
        metadata.Set(key, value);  // 二分插入
    }
}
```

#### RoleLogger - 作用域日志

**RoleLogger** (src/UnifierTSL/Logging/RoleLogger.cs) 将 `Logger` 与 Host 上下文封装在一起：

```csharp
public class RoleLogger
{
    private readonly Logger logger;
    private readonly ILoggerHost host;
    private ImmutableArray<ILogMetadataInjector> injectors = [];

    public void Log(LogLevel level, string message, ReadOnlySpan<KeyValueMetadata> metadata = default)
    {
        // 1. 创建日志条目
        MetadataAllocHandle allocHandle = logger.CreateMetadataAllocHandle();
        LogEntry entry = new(host.Name, message, level, ref allocHandle);

        // 2. 应用手动元数据
        foreach (var kv in metadata) {
            entry.SetMetadata(kv.Key, kv.Value);
        }

        // 3. 应用 RoleLogger 注入器
        foreach (var injector in injectors) {
            injector.InjectMetadata(ref entry);
        }

        // 4. 委托给 Logger
        logger.Log(ref entry);

        // 5. 清理
        allocHandle.Free();
    }
}
```

**扩展方法** (src/UnifierTSL/Logging/LoggerExt.cs)：
```csharp
public static void Info(this RoleLogger logger, string message, string? category = null)
    => logger.Log(LogLevel.Info, message, overwriteCategory: category);

public static void Warning(this RoleLogger logger, string message, string? category = null)
    => logger.Log(LogLevel.Warning, message, overwriteCategory: category);

public static void Error(this RoleLogger logger, string message, Exception? ex = null, string? category = null)
    => logger.Log(LogLevel.Error, message, overwriteCategory: category, exception: ex);

public static void LogHandledException(this RoleLogger logger, string message, Exception ex, string? category = null)
    => logger.Log(LogLevel.Error, message, overwriteCategory: category, exception: ex);
```

#### 元数据管理

**MetadataCollection** (src/UnifierTSL/Logging/Metadata/MetadataCollection.cs) - 排序键值存储：

```csharp
public ref struct MetadataCollection
{
    private Span<KeyValueMetadata> _entries;  // 缓冲池（ArrayPool）缓冲区
    private int _count;

    public void Set(string key, string value)
    {
        // 二分查找插入点
        int index = BinarySearch(key);
        if (index >= 0) {
            // 键已存在 - 更新值
            _entries[index] = new(key, value);
        } else {
            // 键不存在 - 在 ~index 处插入
            index = ~index;
            EnsureCapacity(_count + 1);
            _entries.Slice(index, _count - index).CopyTo(_entries.Slice(index + 1));
            _entries[index] = new(key, value);
            _count++;
        }
    }

    private void EnsureCapacity(int requiredCapacity)
    {
        if (_entries.Length >= requiredCapacity) return;

        // 通过 ArrayPool 扩容
        int newCapacity = _entries.Length == 0 ? 4 : _entries.Length * 2;
        Span<KeyValueMetadata> newBuffer = ArrayPool<KeyValueMetadata>.Shared.Rent(newCapacity);
        _entries.CopyTo(newBuffer);
        ArrayPool<KeyValueMetadata>.Shared.Return(_entries.ToArray());
        _entries = newBuffer;
    }
}
```

**MetadataAllocHandle** (src/UnifierTSL/Logging/Metadata/MetadataAllocHandle.cs) - 分配管理器：
```csharp
public unsafe struct MetadataAllocHandle
{
    private delegate*<int, Span<KeyValueMetadata>> _allocate;
    private delegate*<Span<KeyValueMetadata>, void> _free;

    public Span<KeyValueMetadata> Allocate(int capacity) => _allocate(capacity);
    public void Free(Span<KeyValueMetadata> buffer) => _free(buffer);
}
```

#### ConsoleLogWriter - 颜色编码输出

**ConsoleLogWriter** (src/UnifierTSL/Logging/LogWriters/ConsoleLogWriter.cs) - 服务器路由的控制台输出：

```csharp
public class ConsoleLogWriter : ILogWriter
{
    public void Write(in LogEntry raw)
    {
        // 1. 检查是否命中服务器专属路由
        if (raw.TryGetMetadata("ServerContext", out string? serverName)) {
            ServerContext? server = UnifiedServerCoordinator.Servers
                .FirstOrDefault(s => s.Name == serverName);

            if (server is not null) {
                WriteToConsole(server.Console, raw);
                return;
            }
        }

        // 2. 写入全局控制台
        WriteToConsole(Console, raw);
    }

    private static void WriteToConsole(IConsole console, in LogEntry raw)
    {
        lock (SynchronizedGuard.ConsoleLock) {
            // 使用颜色码格式化分段
            foreach (ColoredSegment segment in DefConsoleFormatter.Format(raw)) {
                console.ForegroundColor = segment.ForegroundColor;
                console.BackgroundColor = segment.BackgroundColor;
                console.Write(segment.Text.Span);
            }
            console.WriteLine();
        }
    }
}
```

**颜色映射**：
|日志级别 |级别文本|前景色|
|----------|-----------|------------------|
|追踪 | `[Trace]` |灰色|
|调试| `[Debug]` |蓝色|
|信息 | `[+Info]` |白色|
|成功| `[Succe]` |绿色|
|警告| `[+Warn]` |黄色|
|错误 | `[Error]` |红色|
|关键| `[Criti]` |深红色|
| （未知）| `[+-·-+]` |白色|

**输出格式** (src/UnifierTSL/Logging/Formatters/ConsoleLog/DefConsoleFormatter.cs:13-71)：

单行消息：
```
[Level][Role|Category] Message
```

多行消息：
```
[Level][Role|Category] First line
 │   Second line
 └── Last line
```

异常场景（已处理 - 级别 ≤ 警告）：
```
[Level][Role|Category] Message
 │ Handled Exception:
 │   Exception line 1
 │   Exception line 2
 └── Exception line N
```

异常场景（未处理 - 级别 > 警告）：
```
[Level][Role|Category] Message
 │ Unexpected Exception:
 │   Exception line 1
 │   Exception line 2
 └── Exception line N
```

**细分结构**：
- **段 0**：级别文本（按级别着色）
- **段 1**：角色/类别文本（青色前景，黑色背景）
- **第 2 段**：带有多行方框图字符的主要信息（按级别着色）
- **第 3 段**（可选）：带有方框图字符的异常详细信息（红色前景，白色背景）

#### TraceContext - 请求关联

**TraceContext** (src/UnifierTSL/Logging/LogTrace/TraceContext.cs) - 分布式跟踪上下文：

```csharp
[StructLayout(LayoutKind.Explicit, Size = 40)]
public readonly struct TraceContext(Guid correlationId, TraceId traceId, SpanId spanId)
{
    [FieldOffset(00)] public readonly Guid CorrelationId = correlationId;  // 16 字节
    [FieldOffset(16)] public readonly TraceId TraceId = traceId;           // 8 字节（ulong）
    [FieldOffset(32)] public readonly SpanId SpanId = spanId;              // 8 字节（ulong）
}
```

**用法**：
```csharp
TraceContext trace = new(
    Guid.NewGuid(),                                 // 唯一请求 ID
    new TraceId((ulong)DateTime.UtcNow.Ticks),     // 逻辑追踪链
    new SpanId((ulong)Thread.CurrentThread.ManagedThreadId)  // 操作跨度
);

logger.Log(LogLevel.Info, "Player authenticated", in trace,
    metadata: stackalloc[] { new("PlayerId", playerId.ToString()) });
```

#### 服务器上下文集成

**ServerContext** (src/UnifierTSL/Servers/ServerContext.cs) 实现 `ILoggerHost` 和 `ILogMetadataInjector`：

```csharp
public partial class ServerContext : RootContext, ILoggerHost, ILogMetadataInjector
{
    public readonly RoleLogger Log;
    public string? CurrentLogCategory { get; set; }
    string ILoggerHost.Name => "Log";

    public ServerContext(...) {
        Log = UnifierApi.CreateLogger(this, overrideLogCore);
        Log.AddMetadataInjector(injector: this);  // 注册自身
    }

    void ILogMetadataInjector.InjectMetadata(scoped ref LogEntry entry) {
        entry.SetMetadata("ServerContext", this.Name);  // 自动注入服务器名
    }
}
```

**每服务器路由**：
```
Server1.Log.Info("Player joined")
    ↓
LogEntry with metadata["ServerContext"] = "Server1"
    ↓
ConsoleLogWriter detects metadata, routes to Server1.Console
    ↓
Output in Server1's console window with server-specific colors
```

</details>

#### 性能说明

**开销**：日志记录是轻分配的 — `LogEntry` 基于堆栈，元数据由池支持。实际开销取决于你附加的元数据数量、格式化程序以及你要写入的接收器。

**内存**：元数据缓冲区按需增长并得到重用。保持元数据集较小以避免频繁扩容抖动。

**吞吐量**：实际上，吞吐量受到接收器的限制。控制台输出比缓冲文件写入慢得多。如果你有生产延迟目标，请使用实际接收器和日志量进行基准测试。

#### 最佳实践

1. **使用元数据，而不是字符串插值**：
   ```csharp
   // 不佳：会产生字符串分配
   logger.Info($"Player {playerId} joined");

   // 更佳：使用结构化元数据
   logger.Info("Player joined", metadata: stackalloc[] {
       new("PlayerId", playerId.ToString())
   });
   ```

2. **确定日志类别的范围**：
   ```csharp
   // 为相关日志块设置类别
   serverContext.CurrentLogCategory = "WorldGen";
   GenerateWorld();
   serverContext.CurrentLogCategory = null;
   ```

3. **添加自定义元数据注入器**以进行关联：
   ```csharp
   public class RequestIdInjector : ILogMetadataInjector
   {
       private readonly AsyncLocal<Guid> requestId = new();

       public void InjectMetadata(scoped ref LogEntry entry) {
           if (requestId.Value != Guid.Empty) {
               entry.SetMetadata("RequestId", requestId.Value.ToString());
           }
       }
   }

   logger.AddMetadataInjector(new RequestIdInjector());
   ```

4. **正确记录异常**：
   ```csharp
   try {
       DangerousOperation();
   }
   catch (Exception ex) {
       logger.LogHandledException("Operation failed", ex, category: "DangerousOperation");
       // 异常细节会在控制台输出中自动格式化
   }
   ```

有关更多日志记录示例，请参阅 `src/Plugins/TShockAPI/TShock.cs` 和 `src/UnifierTSL/Servers/ServerContext.cs`。

<a id="26-configuration-service"></a>
### 2.6 配置服务
- `ConfigRegistrar` 实现 `IPluginConfigRegistrar`。在内置 .NET 宿主中，插件配置根目录固定为 `Path.Combine("config", Path.GetFileNameWithoutExtension(container.Location.FilePath))`（例如 `config/TShockAPI`）。
- `CreateConfigRegistration<T>` 为你提供 `ConfigRegistrationBuilder`，你可以在其中设置默认值、序列化选项、错误策略 (`DeserializationFailureHandling`) 和外部更改触发器。
- 你得到一个 `ConfigHandle<T>` ，它可以让你 `RequestAsync` 、 `Overwrite` 、 `ModifyInMemory` ，并通过 `OnChangedAsync` 订阅热重载。文件访问由 `FileLockManager` 保护以防止损坏。
- 启动器根配置与插件配置分离：`LauncherConfigManager` 专门管理 `config/config.json`，文件缺失时会自动创建，并且会明确忽略旧的根目录 `config.json`。
- 启动器设置的启动优先级是 `config/config.json` -> CLI 覆盖 -> 缺失端口/密码时的交互式补全，并会把启动阶段生效快照回写到 `config/config.json`。启动完成后，对 `config/config.json` 的修改会应用到支持热重载的启动器设置。
- 根配置热重载会应用 `launcher.serverPassword`、`launcher.joinServer`、追加式 `launcher.autoStartServers`，以及通过重绑监听器实现的 `launcher.listenPort`。

<a id="3-usp-integration-points"></a>
## 3. USP 集成点

- `ServerContext` 继承了 USP 的 `RootContext`，将 Unifier 服务插入上下文（自定义控制台、数据包接收器、记录元数据）。涉及泰拉瑞亚世界/游戏状态的所有内容都会经过此上下文。
- 网络修补程序 (`UnifiedNetworkPatcher`) 对 `NetplaySystemContext` 函数做 detour，以共享缓冲区并协调跨服务器发送/接收路径。
- MonoMod `On.` detour 适合冷门/低复用 hook。对于常见或竞争激烈的热点 hook，优先做成/使用 `EventHub` 提供程序，让插件共享处理器级别的顺序与过滤能力，而不是叠加各自的 detour。
- Unifier 直接使用 USP 运行时中的 TrProtocol 数据包结构与接口（`INetPacket`、`IManagedPacket`、`ISideSpecific` 等），`PacketSender`/`NetPacketHandler` 按 TrProtocol 读写契约工作。

<a id="4-public-api-surface"></a>
## 4. 公共 API 接口

<a id="41-facade-unifierapi"></a>
### 4.1 门面 (`UnifierApi`)
- 运行时在启动器入口 (`Program.cs`) 中通过 `HandleCommandLinePreRun`、`PrepareRuntime`、`InitializeCore` 和 `CompleteLauncherInitialization` 完成启动，并在协调器就绪后再调用 `StartRootConfigMonitoring` — 这些都是内部启动 API，不是给插件直接调用的。
- `EventHub` 使你可以在初始化完成后访问所有分组的事件提供程序。
- `EventHub.Launcher.InitializedEvent` 会在启动器参数最终确定（包括交互式补全）后触发，就在协调器启动之前；根配置文件监视则要等 `UnifiedServerCoordinator.Launch(...)` 成功之后才开启。
- `PluginHosts` 延迟设置 `PluginOrchestrator` 以进行宿主级交互。
- `CreateLogger(ILoggerHost, Logger? overrideLogCore = null)` 返回作用于插件作用域的 `RoleLogger`，并复用共享 `Logger`。
- `UpdateTitle(bool empty = false)` 根据协调器状态控制窗口标题。
- `VersionHelper` 和 `LogCore`/`Logger` 提供共享实用程序（版本信息、日志记录核心）。

<a id="42-event-payloads--helpers"></a>
### 4.2 事件载荷与辅助类型
- 事件载荷结构位于 `src/UnifierTSL/Events/*` 下并实现 `IEventContent`。专用接口（`IPlayerEventContent`、`IServerEventContent`）添加服务器或玩家信息等上下文。
- `HandlerPriority` 和 `FilterEventOption` 控制调用顺序和过滤。
- 注册/注销助手是线程安全且分配轻量的。

<a id="43-module--plugin-types"></a>
### 4.3 模块和插件类型
- `ModulePreloadInfo`、`ModuleLoadResult`、`LoadedModule` 描述模块元数据和生命周期。
- `IPlugin`、`BasePlugin`、`PluginContainer`、`PluginInitInfo` 和 `IPluginHost` 定义插件合约和容器。
- 配置接口面包括 `IPluginConfigRegistrar`、`ConfigHandle<T>` 和 `ConfigFormat` 枚举。

<a id="44-networking-apis"></a>
### 4.4 网络 API
- `PacketSender` 公开固定/动态数据包发送帮助程序以及服务器端变体。
- `NetPacketHandler` 提供 `Register<TPacket>`、`UnRegister<TPacket>`、`ProcessBytes` 以及 `ReceivePacketEvent<T>` 上的数据包回调。
- `LocalClientSender` 包装 `RemoteClient`，公开 `Kick`、`SendData` 和 `Client` 元数据。 `ClientPacketReceiver` 重放或重写入站数据包。
- 协调器助手提供 `TransferPlayerToServer`、`SwitchJoinServerEvent` 和状态查询（`GetClientCurrentlyServer`、`Servers` 列表）。

<a id="45-logging--diagnostics"></a>
### 4.5 日志与诊断
- `RoleLogger` 扩展方法（参见 `src/UnifierTSL/Logging/LoggerExt.cs`）为你提供严重性帮助：`Debug`、`Info`、`Warning`、`Error`、`Success`、`LogHandledException`。
- `LogEventIds` 列出用于对日志输出进行分类的标准事件标识符。
- `Logger.ReplayHistory(...)` 和 `LogCore.AttachHistoryWriter(...)` 允许新的 sink 先从内存历史环回放，再开始接收实时日志。
- 事件提供程序公开 `HandlerCount`，你可以为诊断仪表板枚举 `EventProvider.AllEvents`。

<a id="5-runtime-lifecycle--operations"></a>
## 5. 运行时生命周期和操作

<a id="51-startup-sequence"></a>
### 5.1 启动顺序
1. `HandleCommandLinePreRun` 应用启动前语言覆盖，并确定当前使用的 Terraria 文化。
2. `PrepareRuntime` 解析启动器 CLI 覆盖、加载 `config/config.json`、合并启动设置，并配置持久日志后端。
3. `ModuleAssemblyLoader.Load` 扫描 `plugins/`、暂存程序集并处理依赖项提取。
4. 插件宿主找到符合条件的入口点并实例化 `IPlugin` 实现。
5. `BeforeGlobalInitialize` 在每个插件上同步运行 - 使用它进行跨插件服务连接。
6. `InitializeAsync` 为每个插件运行；你将获得先前的插件初始化任务，以便你可以等待你的依赖项。
7. `InitializeCore` 接线 `EventHub`、完成插件宿主初始化，并应用已解析的启动器默认值（入服策略 + 待启动世界列表）。
8. `CompleteLauncherInitialization` 提示输入仍缺失的端口/密码，同步最终运行时快照，并触发 `EventHub.Launcher.InitializedEvent`。
9. `UnifiedServerCoordinator.Launch(...)` 绑定共享监听器、启动已配置世界，并注册协调器运行循环。
10. `StartRootConfigMonitoring()` 开始监视 `config/config.json`；随后 `Program.Run()` 会更新标题、记录启动成功日志，并触发 `EventHub.Coordinator.Started`。

**关于启动器参数的一些注意事项**：
- 语言优先级：`UTSL_LANGUAGE` env var 在 CLI 解析之前应用，并阻止稍后的 `-lang` / `-culture` / `-language` 覆盖。
- `-server` / `-addserver` / `-autostart` 会在 `PrepareRuntime` 阶段解析服务器描述符；合并行为由 `-servermerge` 控制（默认 `replace`，可选 `overwrite`、`append`），并会持久化本次生效列表。
- `-joinserver` 会在一个常驻解析器里设置启动器的低优先级默认入服模式（`random|rnd|r` 或 `first|f`）；后续根配置热重载可以直接替换该模式。
- `-logmode` / `--log-mode` 选择持久日志后端（`txt`、`none` 或 `sqlite`）。
- `UnifierApi.CompleteLauncherInitialization()` 提示输入任何丢失的端口/密码，然后触发 `EventHub.Launcher.InitializedEvent`。
- `Program.Run()` 启动协调器、启用根配置监视、记录成功，然后触发 `EventHub.Coordinator.Started`。

<a id="52-runtime-operations"></a>
### 5.2 运行时操作
- 事件处理程序处理横切问题——聊天审核、传输控制、数据包过滤等。
- 配置处理对文件更改的反应，因此你可以调整设置而无需重新启动。
- 启动器根配置监视会热应用密码变更、入服策略变更、追加式自动启动世界（仅热追加），以及 `launcher.listenPort` 的监听器重绑。
- 协调器保持窗口标题更新，维护服务器列表，重放加入/离开序列，并且可以在不退出进程的前提下切换当前监听器。
- 日志元数据和有界历史环使你可以把任何日志条目追溯到其服务器、插件或子系统，并在不丢失最近上下文的前提下挂接新 sink。
- 持久化后端（`txt` / `sqlite`）现在通过后台消费队列写入；`none` 会完全旁路持久化历史提交以保持热路径最小开销。

<a id="53-shutdown--reload"></a>
### 5.3 关机和重新加载
- `PluginOrchestrator` 提供 `ShutdownAllAsync` 和 `UnloadAllAsync`，但内置 `DotnetPluginHost` 在 `ShutdownAsync`/`UnloadPluginsAsync` 上仍是 TODO 占位实现。
- 通过加载器 API (`ModuleAssemblyLoader.TryLoadSpecific`、`ForceUnload`) 和插件加载器操作 (`TryUnloadPlugin`/`ForceUnloadPlugin`) 进行模块重新加载和有针对性的卸载工作。
- 通过注册的处置操作将 `DisposeAsync` 挂钩插入 `ModuleLoadContext` 卸载。

<a id="54-diagnostics"></a>
### 5.4 诊断
- 事件提供程序公开处理程序计数以实现可观察性 - 枚举 `EventProvider.AllEvents` 来构建仪表板。
- `PacketSender.SentPacket`、`NetPacketHandler.ProcessPacketEvent` 和每数据包 `PacketProcessed` 回调非常适合流量指标和跟踪。
- 记录元数据注入器为你提供每个服务器/每个插件的标签，用于在外部接收器中进行过滤。

<a id="6-extensibility-guidelines--best-practices"></a>
## 6. 可扩展性指南和最佳实践

- **优先使用事件提供程序** - 在插件中添加 MonoMod detour 之前，先检查 `EventHub` 是否已有对应提供程序（如果没有，优先考虑向核心补一个，供其他插件复用）。
- **保持在上下文边界内** — 始终通过 `ServerContext` 和 USP 上下文 API 以避免跨服务器错误。
- **声明你的依赖项** - 如果你要发布带有本机/托管依赖的模块，请使用 `ModuleDependenciesAttribute` 以便加载程序可以正确跟踪和清理。
- **事件处理函数中不要写异步** - `ref struct` 参数无法在闭包或异步方法中捕获。获取你需要的内容，然后单独安排异步工作。
- **显式等待你的依赖项** — 使用 `PluginInitInfo` 等待先决条件插件，而不是仅仅希望顺序能够解决。
- **使用内置日志记录** — 通过 `UnifierApi.CreateLogger` 创建记录器，以便自动获得元数据注入和控制台格式化能力。添加 `ILogMetadataInjector` 作为相关数据。
- **围绕事件和协调器流编写测试** - 模拟数据包序列、玩家加入等。USP 上下文可以独立运行，这使得这非常简单。
- **启动时批量注册** — 事件注册是线程安全的，但不是免费的。使用来自数据包发送方的池化缓冲区，并将分配保留在热路径之外。
- **使用挂钩构建监控插件** — `PacketSender.SentPacket` 和事件过滤器可让你在不触及核心运行时的情况下观察流量。
