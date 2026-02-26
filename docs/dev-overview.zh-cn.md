# UnifierTSL 开发者概览

本文档解释 UnifierTSL 运行时如何在 OTAPI Unified Server Process (USP) 之上构建，各关键子系统的职责，以及向集成者公开的公共 API。本文假设你已阅读过 [README](./README.zh-cn.md) 和 USP 的 `Developer-Guide.md`。

## 1. 运行时架构

### 1.1 分层结构
- **USP (OTAPI.UnifiedServerProcess)** — 提供上下文绑定的 Terraria 运行时（`RootContext`、TrProtocol 数据包模型、可 detour 的 hook）。Unifier 在接触 Terraria 静态成员时从不绕过这一层。
- **UnifierTSL Core** — 启动器、编排服务、多服务器协调器(Coordinator)、日志记录、配置管理、模块加载器和插件宿主。
- **Modules & Plugins（模块与插件）** — 存放在 `plugins/` 下的程序集；可以是核心宿主或功能卫星模块。模块可以嵌入依赖负载（托管库、原生库、NuGet 包）供加载器提取。
- **Console Client / Publisher（控制台客户端/发布器）** — 与运行时并列的工具项目，但复用相同的子系统。

### 1.2 启动流程
1. `Program.cs` 解析启动器参数并转发给 `UnifierApi.Initialize`。
2. `UnifierApi` 准备全局服务（日志、事件中心、模块加载器）并组合一个 `PluginOrchestrator`（插件编排器）。
3. 模块通过 `ModuleAssemblyLoader` 被发现和预加载，暂存程序集和依赖 blob。
4. 插件宿主（内置的 `.NET` 宿主加上任何标记了 `[CoreModule]` 的宿主）发现、加载并初始化插件。
5. `UnifiedServerCoordinator` 启动监听套接字，并为每个配置的世界启动 `ServerContext` 实例。
6. 事件桥接器（聊天、游戏、协调器、网络播放、服务器）将 detour 注册到 USP/Terraria，将事件引入 `EventHub`。

### 1.3 主要组件
- `UnifierApi` — 静态外观，用于获取 logger、事件、插件宿主和窗口标题辅助工具。
- `UnifiedServerCoordinator` — 多服务器路由器，管理共享的 Terraria 状态和连接生命周期。
- `ServerContext` — 每个世界一个 USP `RootContext` 子类；集成日志记录、数据包接收器和扩展槽位。
- `PluginOrchestrator` + 宿主 — 管理插件发现、加载、排序、关闭和卸载。
- `ModuleAssemblyLoader` — 处理模块暂存、依赖提取、可收集的加载上下文以及卸载顺序。
- `EventHub` — 集中式事件提供者注册表，将 MonoMod detour 桥接到优先级感知的事件管道。
- `Logging` 子系统 — 分配感知的日志记录，具备元数据注入和可插拔的写入器。

## 2. 核心服务与子系统

### 2.1 事件中心 (Event Hub)

事件系统是 UnifierTSL 的中央发布/订阅基础设施，提供零分配、优先级排序的事件管道，具有编译时类型安全和运行时灵活性。

#### 架构概览

**`EventHub`** (src/UnifierTSL/EventHub.cs) 作为所有事件提供者的聚合点，按域组织：
```csharp
public class EventHub
{
    public readonly LanucherEventHandler Lanucher = new();
    public readonly ChatHandler Chat = new();
    public readonly CoordinatorEventBridge Coordinator = new();
    public readonly GameEventBridge Game = new();
    public readonly NetplayEventBridge Netplay = new();
    public readonly ServerEventBridge Server = new();
}
```

通过以下方式访问事件：`UnifierApi.EventHub.Game.PreUpdate`、`UnifierApi.EventHub.Chat.MessageEvent` 等。

#### 事件提供者类型

事件系统提供**四种不同的提供者类型**，针对不同的可变性和取消需求进行了优化：

| 提供者类型 | 事件数据 | 取消机制 | 使用场景 |
|-----------|---------|---------|----------|
| `ValueEventProvider<T>` | 可变 (`ref T`) | 是（`Handled` 标志） | 处理器修改数据并可取消操作的事件（如聊天命令、传输） |
| `ReadonlyEventProvider<T>` | 不可变 (`in T`) | 是（`Handled` 标志） | 处理器检查数据并可否决操作的事件（如连接验证） |
| `ValueEventNoCancelProvider<T>` | 可变 (`ref T`) | 否 | 处理器可能需要修改共享状态的通知事件 |
| `ReadonlyEventNoCancelProvider<T>` | 不可变 (`in T`) | 否 | 用于生命周期/遥测的纯通知事件（如 `PreUpdate`、`PostUpdate`） |

所有事件参数都是在栈上分配的 `ref struct` 类型——**零堆分配**、**零 GC 压力**。

#### 优先级系统和处理器注册

处理器按**优先级升序**执行（数值越小 = 优先级越高）：
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

**注册 API**：
```csharp
// 基本注册（Normal 优先级）
UnifierApi.EventHub.Chat.MessageEvent.Register(OnMessage);

// 指定优先级
UnifierApi.EventHub.Netplay.ConnectEvent.Register(OnConnect, HandlerPriority.Higher);

// 使用过滤选项（仅在已处理时运行）
UnifierApi.EventHub.Game.GameHardmodeTileUpdate.Register(OnTileUpdate,
    HandlerPriority.Low, FilterEventOption.Handled);

// 取消注册（传递相同的委托引用）
UnifierApi.EventHub.Chat.MessageEvent.UnRegister(OnMessage);
```

**处理器管理内部机制** (src/UnifierTSL/Events/Core/ValueEventBaseProvider.cs:28-68)：
- 使用**易失性快照数组**在调用期间实现无锁读取
- **二分查找插入**自动维护优先级顺序
- **写时复制**语义：注册创建新数组，旧数组对正在进行的调用保持有效
- 仅对修改操作使用 `Lock _sync` 实现线程安全

#### 过滤和取消机制

**FilterEventOption** 根据事件状态控制处理器执行：
```csharp
public enum FilterEventOption : byte
{
    Normal = 1,      // 仅当未处理时执行
    Handled = 2,     // 仅当已处理时执行（如清理/日志）
    All = 3          // 始终执行（Normal | Handled）
}
```

**取消模型**：
- `Handled = true`：标记事件为"已消费"（概念上取消操作）
- `StopPropagation = true`：停止执行剩余处理器
- 不同提供者对 `Handled` 有不同解释：
  - `ReadonlyEventProvider`：向调用者返回布尔值（`out bool handled`）
  - `ValueEventProvider`：调用者在调用后检查 `args.Handled`
  - 无取消提供者：不暴露 `Handled` 标志

**示例 - 聊天命令拦截**：
```csharp
UnifierApi.EventHub.Chat.MessageEvent.Register(
    (ref ReadonlyEventArgs<MessageEvent> args) =>
    {
        if (args.Content.Text.StartsWith("!help"))
        {
            SendHelpText(args.Content.Sender);
            args.Handled = true;  // 阻止进一步处理
        }
    },
    HandlerPriority.Higher);
```

#### 事件桥接器 - MonoMod 集成

事件桥接器将 **MonoMod 运行时 detour** 连接到事件系统，将低级 hook 转换为类型化的事件调用：

**GameEventBridge** (src/UnifierTSL/Events/Handlers/GameEventBridge.cs)：
```csharp
public class GameEventBridge
{
    public readonly ReadonlyEventNoCancelProvider<ServerEvent> PreUpdate = new();
    public readonly ReadonlyEventNoCancelProvider<ServerEvent> PostUpdate = new();
    public readonly ReadonlyEventProvider<GameHardmodeTileUpdateEvent> GameHardmodeTileUpdate = new();

    public GameEventBridge() {
        On.Terraria.Main.Update += OnUpdate;
        On.OTAPI.HooksSystemContext.WorldGenSystemContext.InvokeHardmodeTileUpdate += OnHardmodeTileUpdate;
    }

    private void OnUpdate(On.Terraria.Main.orig_Update orig, Main self, RootContext root, GameTime gameTime) {
        ServerEvent data = new(root.ToServer());
        PreUpdate.Invoke(data);      // 原始逻辑之前
        orig(self, root, gameTime);  // 执行原始 Terraria 逻辑
        PostUpdate.Invoke(data);     // 原始逻辑之后
    }

    private bool OnHardmodeTileUpdate(...) {
        GameHardmodeTileUpdateEvent data = new(x, y, type, self.root.ToServer());
        GameHardmodeTileUpdate.Invoke(data, out bool handled);
        return !handled;  // 如果已处理则返回 false 以取消
    }
}
```

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
- `ConnectEvent`（可取消）- 在客户端握手期间触发
- `ReceiveFullClientInfoEvent`（可取消）- 收到客户端元数据后
- `LeaveEvent`（通知性）- 客户端断开连接通知
- `SocketResetEvent`（通知性）- 套接字清理通知

**CoordinatorEventBridge** (src/UnifierTSL/Events/Handlers/CoordinatorEventBridge.cs)：
- `SwitchJoinServerEvent`（可变）- 为加入的玩家选择目标服务器
- `PreServerTransfer`（可取消）- 在服务器间传输玩家之前
- `PostServerTransfer`（通知性）- 成功传输后
- `CreateSocketEvent`（可变）- 自定义套接字创建

**ServerEventBridge** (src/UnifierTSL/Events/Handlers/ServerEventBridge.cs)：
- `CreateConsoleService`（可变）- 提供自定义控制台实现
- `AddServer` / `RemoveServer`（通知性）- 服务器生命周期通知
- `ServerListChanged`（通知性）- 聚合的服务器列表变化

#### 事件内容层次结构

事件负载实现类型化接口以进行上下文传播：
```csharp
public interface IEventContent { }  // 基础标记

public interface IServerEventContent : IEventContent
{
    ServerContext Server { get; }  // 服务器范围事件
}

public interface IPlayerEventContent : IServerEventContent
{
    int Who { get; }  // 玩家范围事件（包含服务器）
}
```

示例：
```csharp
// 基础事件（无上下文）
public readonly struct MessageEvent(...) : IEventContent { ... }

// 服务器范围
public readonly struct ServerEvent(ServerContext server) : IServerEventContent { ... }

// 玩家范围（继承服务器上下文）
public readonly struct LeaveEvent(int plr, ServerContext server) : IPlayerEventContent
{
    public int Who { get; } = plr;
    public ServerContext Server { get; } = server;
}
```

#### 性能特征

**处理器调用**：
- 快照读取：O(1) 易失性访问（无锁）
- 处理器迭代：O(n)，其中 n = 处理器数量
- 过滤检查：每个处理器 O(1) 位运算 AND

**内存**：
- 事件参数：栈分配的 `ref struct`（零堆分配）
- 处理器快照：不可变数组（GC 友好，长期存活的 Gen2）
- 注册：O(log n) 二分查找 + O(n) 数组复制

**典型性能**：
- 具有 5 个处理器的简单事件：约 100-200 ns
- 具有 20 个处理器 + 过滤的事件：约 500-800 ns
- 稳态调用期间零 GC 分配

#### 最佳实践

1. **优先使用事件提供者而非直接 Detour**：为保持一致性使用 `EventHub` 提供者；将新提供者贡献到核心运行时而不是添加插件特定的 detour
2. **遵守 `ref struct` 约束**：事件参数不能在闭包或异步方法中捕获；同步提取必要数据
3. **避免阻塞操作**：事件处理器在游戏线程上运行；通过 `Task.Run()` 调度长时间运行的工作
4. **在关闭期间取消注册**：始终在 `DisposeAsync()` 中调用 `UnRegister()` 以防止内存泄漏
5. **使用适当的提供者类型**：在适用时选择只读/无取消变体以获得更好的性能
6. **注意优先级顺序**：谨慎使用 `Highest`；为关键基础设施（如权限检查）保留
7. **高级：自定义事件提供者**

要添加新事件，遵循此模式：
```csharp
// 1. 定义事件内容结构
public readonly struct MyCustomEvent(ServerContext server, int data) : IServerEventContent
{
    public ServerContext Server { get; } = server;
    public int Data { get; } = data;
}

// 2. 在适当的桥接器中创建提供者
public class MyEventBridge
{
    public readonly ValueEventProvider<MyCustomEvent> CustomEvent = new();

    public MyEventBridge() {
        On.Terraria.Something.Method += OnMethod;
    }

    private void OnMethod(...) {
        MyCustomEvent data = new(server, 42);
        CustomEvent.Invoke(ref data, out bool handled);
        if (handled) return;  // 遵守取消
        // ... 原始逻辑
    }
}

// 3. 添加到 EventHub
public class EventHub
{
    public readonly MyEventBridge MyEvents = new();
}
```

有关完整示例，请参阅 `src/Plugins/TShockAPI/Handlers/MiscHandler.cs` 和 `src/Plugins/CommandTeleport`。

### 2.2 模块系统

模块系统提供**可收集的程序集加载**，具备自动依赖管理、热重载支持和安全卸载语义。它在原始 DLL 和插件宿主层之间架起桥梁。

#### 模块类型与组织

**三种模块类别** (src/UnifierTSL/Module/ModulePreloadInfo.cs)：

1. **核心模块** (`[assembly: CoreModule]`)：
   - 相关程序集的锚点
   - 获得专用子目录：`plugins/<ModuleName>/`
   - 在隔离的 `ModuleLoadContext` 中加载（可收集）
   - 可通过 `[assembly: ModuleDependencies<TProvider>]` 声明依赖
   - 其他模块可通过 `[assembly: RequiresCoreModule("ModuleName")]` 依赖它们

2. **卫星模块** (`[assembly: RequiresCoreModule("CoreModuleName")]`)：
   - 必须引用现有核心模块
   - 暂存在核心模块目录中：`plugins/<CoreModuleName>/SatelliteName.dll`
   - **共享核心模块的 `ModuleLoadContext`**（关键：共享类型，协调卸载）
   - 不能声明自己的依赖（从核心模块继承）
   - 在核心模块初始化后加载

3. **独立模块**（无特殊属性）：
   - 保留在 `plugins/` 根目录
   - 在隔离的 `ModuleLoadContext` 中加载
   - 不能被卫星模块目标化
   - 如需要可声明依赖

**模块发现与暂存** (src/UnifierTSL/Module/ModuleAssemblyLoader.cs:43-175)：

加载器**在发现期间不加载程序集**——它通过 `MetadataLoadContext` 直接读取 PE 头：
```csharp
public ModulePreloadInfo PreloadModule(string dll)
{
    // 1. 读取 PE 头而不加载程序集
    using PEReader peReader = MetadataBlobHelpers.GetPEReader(dll);
    MetadataReader metadataReader = peReader.GetMetadataReader();

    // 2. 提取程序集名称
    AssemblyDefinition asmDef = metadataReader.GetAssemblyDefinition();
    string moduleName = metadataReader.GetString(asmDef.Name);

    // 3. 通过 PE 元数据检查属性
    bool isCoreModule = MetadataBlobHelpers.HasCustomAttribute(metadataReader, "CoreModuleAttribute");
    bool hasDependencies = MetadataBlobHelpers.HasCustomAttribute(metadataReader, "ModuleDependenciesAttribute");
    string? requiresCoreModule = TryReadAssemblyAttributeData(metadataReader, "RequiresCoreModuleAttribute");

    // 4. 确定暂存位置
    string newLocation;
    if (!hasDependencies && !isCoreModule && requiresCoreModule is null) {
        newLocation = Path.Combine(loadDirectory, fileName);  // 独立：保留在根目录
    } else {
        string moduleDir = Path.Combine(loadDirectory,
            (hasDependencies || isCoreModule) ? moduleName : requiresCoreModule!);
        Directory.CreateDirectory(moduleDir);
        newLocation = Path.Combine(moduleDir, Path.GetFileName(dll));
    }

    // 5. 移动文件（保留时间戳）并生成签名
    CopyFileWithTimestamps(dll, newLocation);
    CopyFileWithTimestamps(dll.Replace(".dll", ".pdb"), newLocation.Replace(".dll", ".pdb"));
    File.Delete(dll);  // 删除原始文件

    return new ModulePreloadInfo(FileSignature.Generate(newLocation), ...);
}
```

**验证规则**：
- 不能同时是 `CoreModule` 和 `RequiresCoreModule`
- `RequiresCoreModule` 模块不能声明依赖
- `RequiresCoreModule` 必须指定核心模块名称

#### FileSignature 变更检测

**FileSignature** (src/UnifierTSL/FileSystem/FileSignature.cs) 通过三级检测跟踪模块变化：

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

    // 级别 3：最慢/最彻底 - 仅检查 SHA256 哈希
    public bool ContentEquals(string filePath) {
        return Hash == ComputeHash(filePath);
    }
}
```

**用法**：模块加载器在 `Load()` 期间使用 `FileSignature.Hash` 比较来检测更新的模块 (src/UnifierTSL/Module/ModuleAssemblyLoader.cs:204-207)。

#### ModuleLoadContext - 可收集的程序集加载

**ModuleLoadContext** (src/UnifierTSL/Module/ModuleLoadContext.cs:16) 扩展 `AssemblyLoadContext`，具有：

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

**关键属性**：
- **`isCollectible: true`** - 启用运行时卸载（无引用时 GC 收集 ALC）
- **清理动作** - 插件通过 `AddDisposeAction(Func<Task>)` 注册清理，在 `Unloading` 事件期间执行
- **解析链** - 托管和原生程序集的多级回退

**程序集解析策略** (src/UnifierTSL/Module/ModuleLoadContext.cs:83-128)：

```
OnResolving(AssemblyName assemblyName)
    ↓
1. 框架程序集？-> 从默认 ALC 加载（BCL、System.* 等）
    ↓
2. 宿主程序集（UnifierTSL.dll）？-> 返回单例
    ↓
3. UTSL 核心库？-> 通过 AssemblyDependencyResolver 解析
    ↓
4. 首选共享程序集解析（精确版本匹配）：
   - 在已加载模块中搜索匹配名称 + 版本
   - 通过 LoadedModule.Reference() 注册依赖
   - 从其他模块的 ALC 返回程序集
    ↓
5. 模块本地依赖：
   - 检查 {moduleDir}/lib/{assemblyName}.dll
   - 如果存在则从此上下文加载
    ↓
6. 回退共享程序集解析（仅名称匹配）：
   - 在已加载模块中搜索任何版本
   - 尝试代理加载（如果请求者引用提供者的核心程序集）
   - 从其他模块的 ALC 返回程序集
    ↓
7. 返回 null（解析失败）
```

**非托管 DLL 解析** (src/UnifierTSL/Module/ModuleLoadContext.cs:134-155)：
- 从模块目录读取 `dependencies.json`
- 通过 `RidGraph.Instance.ExpandRuntimeIdentifier()` 扩展当前 RID（例如，`win-x64` → `win` → `any`）
- 使用 RID 回退链搜索匹配的原生库
- 通过 `LoadUnmanagedDllFromPath()` 加载

#### 依赖管理

**依赖声明** (src/UnifierTSL/Module/ModuleDependenciesAttribute.cs)：
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

1. **NuGet 依赖** (src/UnifierTSL/Module/Dependencies/NugetDependency.cs)：
   - 通过 `NugetPackageCache.ResolveDependenciesAsync()` 解析传递依赖
   - 将缺失的包下载到全局包文件夹（`~/.nuget/packages`）
   - 提取托管库（匹配目标框架）+ 原生库（匹配 RID）
   - 返回带有惰性流的 `LibraryEntry[]`

2. **托管嵌入式依赖** (src/UnifierTSL/Module/Dependencies/ManagedEmbeddedDependency.cs)：
   - 通过 PE 头从嵌入资源读取程序集标识
   - 将嵌入的 DLL 提取到 `{moduleDir}/lib/{AssemblyName}.dll`

3. **原生嵌入式依赖** (src/UnifierTSL/Module/Dependencies/NativeEmbeddedDependency.cs)：
   - 探测 RID 回退链以查找匹配的嵌入资源
   - 提取到 `{moduleDir}/lib/runtimes/{rid}/native/{libraryName}.{ext}`

**依赖提取过程** (src/UnifierTSL/Module/ModuleAssemblyLoader.cs:368-560)：

```csharp
private bool UpdateDependencies(string dll, ModuleInfo info)
{
    // 1. 验证模块结构（必须在命名目录中）
    // 2. 加载先前的 dependencies.json
    DependenciesConfiguration prevConfig = DependenciesConfiguration.LoadDependenicesConfig(moduleDir);

    // 3. 提取新的/更新的依赖
    foreach (ModuleDependency dependency in dependencies) {
        if (dependency.Version != prevConfig.Version) {
            ImmutableArray<LibraryEntry> items = dependency.LibraryExtractor.Extract(Logger);
            // ... 跟踪每个文件路径的最高版本
        }
    }

    // 4. 处理锁处理的文件复制
    foreach (var (dependency, item) in highestVersion) {
        try {
            using Stream source = item.Stream.Value;
            destination = Utilities.IO.SafeFileCreate(targetPath, out Exception? ex);

            if (destination != null) {
                source.CopyTo(destination);  // 成功路径
            }
            else if (ex is IOException && FileSystemHelper.FileIsInUse(ex)) {
                // 文件被已加载的程序集锁定！创建版本化文件
                string versionedPath = Path.ChangeExtension(item.FilePath,
                    $"{item.Version}.{Path.GetExtension(item.FilePath)}");
                destination = Utilities.IO.SafeFileCreate(versionedPath, ...);
                source.CopyTo(destination);

                // 跟踪旧（过时）和新（活动）文件
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

**锁处理策略**：
- 通过 `IOException` HResult 代码检测锁定文件
- 创建版本化副本：`Newtonsoft.Json.13.0.3.dll` 而不是 `Newtonsoft.Json.dll`
- 在清单中将旧文件标记为 `Obsolete`
- 下次重启时文件不再被锁定时运行清理

**原生依赖的 RID 图** (src/UnifierTSL/Module/Dependencies/RidGraph.cs)：
- 加载嵌入的 `RuntimeIdentifierGraph.json`（NuGet 的官方 RID 图）
- RID 扩展的 BFS 遍历：`win-x64` → [`win-x64`, `win`, `any`]
- 使用者：`NugetPackageFetcher`、`ModuleLoadContext.LoadUnmanagedDll()`、`NativeEmbeddedDependency`

#### 模块卸载与依赖图

**LoadedModule** (src/UnifierTSL/Module/LoadedModule.cs) 跟踪双向依赖：

```csharp
public record LoadedModule(
    ModuleLoadContext Context,
    Assembly Assembly,
    ImmutableArray<ModuleDependency> Dependencies,
    FileSignature Signature,
    LoadedModule? CoreModule)  // 核心/独立模块为 null
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
    if (CoreModule is not null) return;  // 不能卸载卫星（共享 ALC）
    if (Unloaded) return;

    // 递归卸载所有依赖者
    foreach (LoadedModule dependent in DependentModules) {
        if (dependent.CoreModule == this) {
            dependent.Unreference();  // 只断开链接（将与核心一起卸载）
        } else {
            dependent.Unload();  // 递归级联
        }
    }

    Unreference();     // 清除所有引用
    unloaded = true;
    Context.Unload();  // 触发 OnUnloading 事件 -> 清理动作
}
```

**有序卸载的拓扑排序** (src/UnifierTSL/Module/LoadedModule.cs:77-109)：
```csharp
// 按执行顺序获取依赖者（preorder = 叶到根，postorder = 根到叶）
public ImmutableArray<LoadedModule> GetDependentOrder(bool includeSelf, bool preorder)
{
    HashSet<LoadedModule> visited = [];  // 循环检测
    Queue<LoadedModule> result = [];

    void Visit(LoadedModule module) {
        if (!visited.Add(module)) return;  // 已访问（检测到循环）

        if (preorder) result.Enqueue(module);
        foreach (var dep in module.DependentModules) Visit(dep);
        if (!preorder) result.Enqueue(module);
    }

    Visit(this);
    return result.ToImmutableArray();
}
```

**ForceUnload** (src/UnifierTSL/Module/ModuleAssemblyLoader.cs:179-189)：
```csharp
public void ForceUnload(LoadedModule module)
{
    // 如果是卫星，改为卸载核心（卫星共享 ALC）
    if (module.CoreModule is not null) {
        ForceUnload(module.CoreModule);
        return;
    }

    // 按后序卸载（依赖者在依赖之前）
    foreach (LoadedModule m in module.GetDependentOrder(includeSelf: true, preorder: false)) {
        Logger.Debug($"Unloading module {m.Signature.FilePath}");
        m.Unload();
        moduleCache.Remove(m.Signature.FilePath, out _);
    }
}
```

#### 模块与插件的关系

**明确的关注点分离**：

| 概念 | 职责 | 关键类型 |
|------|------|---------|
| **模块** | 程序集加载、依赖管理、ALC 生命周期 | `LoadedModule` |
| **插件** | 业务逻辑、事件处理器、游戏集成 | `IPlugin` / `PluginContainer` |

**流程**：
1. `ModuleAssemblyLoader` 暂存并加载程序集 → `LoadedModule`
2. `PluginDiscoverer` 扫描已加载模块的 `IPlugin` 实现 → `IPluginInfo`
3. `PluginLoader` 实例化插件类 → `IPlugin` 实例
4. `PluginContainer` 包装 `LoadedModule` + `IPlugin` + `PluginMetadata`
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

    // 3. 在 ALC 中注册清理动作
    loaded.Context.AddDisposeAction(async () => await instance.DisposeAsync());

    // 4. 包装在容器中
    return new PluginContainer(info.Metadata, loaded, instance);
}
```

#### 最佳实践

1. **为相关功能使用核心模块**：在核心模块下分组卫星以共享依赖并简化卸载
2. **声明所有依赖**：使用 `ModuleDependenciesAttribute` 而不是手动复制 DLL
3. **测试热重载**：使用 `FileSignature.Hash` 检查验证更新检测是否正常工作
4. **处理锁定文件**：设计插件以快速释放文件句柄（避免长期存在的流）
5. **遵守卸载顺序**：不要跨重载缓存 `LoadedModule` 引用
6. **优先选择 NuGet 依赖**：让加载器自动处理传递解析
7. **使用 RID 感知的原生库**：为多个 RID 嵌入或依赖 NuGet 包 RID 探测

#### 性能特征

**模块加载**：
- PE 头解析：每个程序集约 1-2 ms
- 暂存（文件复制）：每个程序集约 10-50 ms（磁盘受限）
- 程序集加载：每个程序集约 5-20 ms
- 依赖提取：约 100-500 ms（NuGet 解析）或约 1-10 ms（嵌入式）

**内存**：
- 每个 `ModuleLoadContext`：约 50-200 KB 开销
- 共享程序集：加载一次，从多个 ALC 引用（无重复）
- 卸载的模块：GC 后可收集（Gen2 收集）

**典型场景**：10 个插件，50 个总程序集（包括依赖），约 1-3 秒冷启动。

### 2.3 插件宿主编排
- `PluginOrchestrator` (`src/UnifierTSL/PluginHost/PluginOrchestrator.cs`) 注册内置宿主（`DotnetPluginHost`）和从核心模块发现的任何其他宿主。
- `.InitializeAllAsync` 预加载模块，发现插件入口点（`PluginDiscoverer`），并通过 `PluginLoader` 加载它们。
- 插件容器按 `InitializationOrder` 排序，构造描述先前插件的 `PluginInitInfo` 列表，并在允许等待依赖的同时并发调用 `IPlugin.InitializeAsync`。
- 关闭/卸载镜像此流程：`ShutdownAllAsync` 确保插件运行清理，如果需要则进行模块卸载。

### 2.4 网络与协调器

UnifierTSL 的网络层在单个端口上提供**统一的多服务器数据包路由**，具有中间件风格的数据包拦截、优先级排序的处理器和内存池序列化。

#### UnifiedServerCoordinator 架构

**UnifiedServerCoordinator** (src/UnifierTSL/UnifiedServerCoordinator.cs) 集中管理所有服务器的共享网络状态：

**全局共享状态**：
```csharp
// 索引 = 客户端槽位（0-255）
Player[] players                              // 玩家实体状态
RemoteClient[] globalClients                  // TCP 套接字包装器
LocalClientSender[] clientSenders             // 每客户端数据包发送器
MessageBuffer[] globalMsgBuffers              // 接收缓冲区（256 × 服务器数量）
volatile ServerContext?[] clientCurrentlyServers  // 客户端 → 服务器映射
```

**连接管道**：

```
TcpClient 连接到统一端口（7777）
    ↓
OnConnectionAccepted()：查找空槽位（0-255）
    ↓
创建 PendingConnection（预认证阶段）
    ↓
异步接收循环：ClientHello、SendPassword、SyncPlayer、ClientUUID
    ↓
触发 SwitchJoinServerEvent → 插件选择目标服务器
    ↓
在选定的 ServerContext 中激活客户端
    ↓
字节处理通过 ProcessBytes hook 路由
```

**PendingConnection** (src/UnifierTSL/UnifiedServerCoordinator.cs:529-696)：
- 在服务器分配之前处理预认证数据包
- 验证 Terraria 版本（Terraria279）
- 密码认证（如果设置了 `Config.ServerPassword`）
- 收集客户端元数据：`ClientUUID`、玩家名称和外观
- 使用 `NetworkText` 原因踢出不兼容的客户端

**服务器传输协议** (src/UnifierTSL/UnifiedServerCoordinator.cs:290-353)：
```csharp
public static void TransferPlayerToServer(byte clientIndex, ServerContext targetServer)
{
    RemoteClient client = globalClients[clientIndex];
    ServerContext currentServer = clientCurrentlyServers[clientIndex]!;

    // 1. 触发可取消的传输前事件
    PreServerTransferEvent preEvent = new(clientIndex, currentServer, targetServer);
    UnifierApi.EventHub.Coordinator.PreServerTransfer.Invoke(preEvent, out bool cancelled);
    if (cancelled) return;

    // 2. 原子服务器切换
    SetClientCurrentlyServer(clientIndex, targetServer);

    // 3. 同步玩家状态到新服务器
    Player player = players[clientIndex];
    targetServer.NetMessage.SendData(MessageID.PlayerInfo, -1, -1, player: clientIndex);
    targetServer.NetMessage.SendData(MessageID.PlayerActive, -1, -1, player: clientIndex);

    // 4. 通知目标服务器中的其他玩家
    targetServer.SyncPlayerJoinToOthers(client, player);

    // 5. 触发传输后事件
    PostServerTransferEvent postEvent = new(clientIndex, currentServer, targetServer);
    UnifierApi.EventHub.Coordinator.PostServerTransfer.Invoke(postEvent);
}
```

**数据包路由 Hook** (src/UnifierTSL/UnifiedServerCoordinator.cs:356-416)：
```csharp
On.Terraria.NetMessageSystemContext.CheckBytes += (orig, self, clientIndex, buffer, length, out int messageType) =>
{
    ServerContext? server = clientCurrentlyServers[clientIndex];
    if (server is null) {
        // 待处理连接 - 通过 PendingConnection 处理
        return pendingConnections[clientIndex].ProcessBytes(buffer, length, out messageType);
    }

    // 路由到服务器的数据包处理器
    lock (globalMsgBuffers[clientIndex]) {
        return NetPacketHandler.ProcessBytes(server, clientIndex, buffer, length, out messageType);
    }
};
```

#### 数据包拦截 - NetPacketHandler

**NetPacketHandler** (src/UnifierTSL/Events/Handlers/NetPacketHandler.cs) 提供**中间件风格的数据包处理**，具有取消和重写能力。

**处理器注册**：
```csharp
NetPacketHandler.Register<TrProtocol.NetPackets.ChatMessage>(
    (ref RecievePacketEvent<ChatMessage> args) =>
    {
        if (args.Packet.Message.StartsWith("/admin")) {
            if (!HasPermission(args.Who, "admin")) {
                args.HandleMode = PacketHandleMode.Cancel;  // 阻止数据包
            }
        }
    },
    HandlerPriority.Highest);
```

**处理器存储**：
- 静态数组：`Array?[] handlers = new Array[INetPacket.GlobalIDCount]`
- 每个数据包类型一个槽位（按 `TPacket.GlobalID` 索引）
- 每个槽位存储 `PriorityItem<TPacket>[]`（按优先级排序）

**数据包处理流程**：
```
ProcessBytes(server, clientIndex, buffer, length)
    ↓
1. 从缓冲区解析 MessageID
    ↓
2. 通过 switch(messageID) 分发到类型特定处理器：
    - ProcessPacket_F<TPacket>()    // 固定，非长度感知
    - ProcessPacket_FL<TPacket>()   // 固定，长度感知
    - ProcessPacket_D<TPacket>()    // 动态（托管）
    - ProcessPacket_DS<TPacket>()   // 动态，特定端
    - ProcessPacket_DLS<TPacket>()  // 动态，长度感知，特定端
    ↓
3. 从缓冲区反序列化数据包（不安全指针）
    ↓
4. 执行处理器链（优先级排序）
    ↓
5. 评估 PacketHandleMode：
    - None：转发到 MessageBuffer.GetData()（原始逻辑）
    - Cancel：完全抑制数据包
    - Overwrite：通过 ClientPacketReceiver.AsRecieveFromSender_*() 重新注入
    ↓
6. 调用 PacketProcessed 回调
```

**PacketHandleMode** (src/UnifierTSL/Events/Handlers/NetPacketHandler.cs)：
```csharp
public enum PacketHandleMode : byte
{
    None = 0,       // 传递到原始 Terraria 逻辑
    Cancel = 1,     // 阻止数据包（阻止处理）
    Overwrite = 2   // 使用修改的数据包（通过 ClientPacketReceiver 重新注入）
}
```

**示例 - 数据包修改**：
```csharp
NetPacketHandler.Register<TrProtocol.NetPackets.TileChange>(
    (ref RecievePacketEvent<TileChange> args) =>
    {
        // 拒绝在受保护区域放置方块
        if (IsProtectedRegion(args.Packet.X, args.Packet.Y)) {
            args.HandleMode = PacketHandleMode.Cancel;
        }
    });
```

#### 数据包发送 - PacketSender 与 LocalClientSender

**PacketSender** (src/UnifierTSL/Network/PacketSender.cs) - 类型安全数据包传输的抽象基类：

**API 方法**：
```csharp
// 固定大小数据包（非托管结构）
public void SendFixedPacket<TPacket>(in TPacket packet)
    where TPacket : struct, INetPacket, INonSideSpecific, INonLengthAware

// 动态大小数据包（托管类型）
public void SendDynamicPacket<TPacket>(in TPacket packet)
    where TPacket : struct, IManagedPacket, INonSideSpecific

// 服务器端变体（设置 IsServerSide 标志）
public void SendFixedPacket_S<TPacket>(in TPacket packet) where TPacket : ISideSpecific, ...
public void SendDynamicPacket_S<TPacket>(in TPacket packet) where TPacket : ISideSpecific, ...

// 运行时调度（对所有 246 种数据包类型使用 switch）
public void SendUnknownPacket<TPacket>(in TPacket packet) where TPacket : INetPacket
```

**缓冲区管理** (src/UnifierTSL/Network/SocketSender.cs:79-117)：
```csharp
private unsafe byte[] AllocateBuffer<TPacket>(in TPacket packet, out byte* ptr_start) where TPacket : INetPacket
{
    int capacity = packet is TrProtocol.NetPackets.TileSection ? 16384 : 1024;
    byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);

    fixed (byte* buf = buffer) {
        ptr_start = buf + 2;  // 为长度头保留 2 字节
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

**LocalClientSender** (src/UnifierTSL/Network/LocalClientSender.cs) - 每客户端数据包发送器：
```csharp
public class LocalClientSender : SocketSender
{
    public byte ID { get; init; }

    protected override ISocket Socket =>
        UnifiedServerCoordinator.globalClients[ID].Socket;

    // 重写 Kick 以原子方式设置终止标志
    public override void Kick(NetworkText text, bool writeToConsole = true) {
        RemoteClient client = UnifiedServerCoordinator.globalClients[ID];
        if (!Volatile.Read(ref client.PendingTermination)) {
            client.PendingTerminationApproved = true;
            Volatile.Write(ref client.PendingTermination, true);
        }
        base.Kick(text, writeToConsole);
    }
}
```

**数据包接收模拟 - ClientPacketReceiver** (src/UnifierTSL/Network/ClientPacketReciever.cs)：

当处理器设置 `HandleMode = Overwrite` 时使用：
```csharp
public static void AsRecieveFromSender_FixedPkt<TPacket>(ServerContext server, byte who, in TPacket packet)
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

        // 作为从发送者接收注入
        server.NetMessage.buffer[who].GetData(buffer, 2, len - 2);
    }
    finally {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
```

#### TrProtocol 集成

UnifierTSL 利用 **TrProtocol**（从 USP IL 合并）作为数据包模型：

**数据包特征**：
- **140+ 种数据包类型**定义为结构
- **接口**：`INetPacket`、`IManagedPacket`、`ILengthAware`、`ISideSpecific`
- **序列化**：基于不安全指针（`void ReadContent(ref void* ptr)`、`void WriteContent(ref void* ptr)`）

**示例数据包**：
```csharp
// 固定大小，非特定端
public struct SpawnPlayer : INetPacket, INonSideSpecific
{
    public short _PlayerSlot;
    public short _SpawnX;
    public short _SpawnY;
    // ...
}

// 动态大小，特定端
public struct ChatMessage : IManagedPacket, ISideSpecific
{
    public bool IsServerSide { get; set; }
    public NetworkText Message;
    public Color Color;
    public byte PlayerId;
}

// 长度感知（需要结束指针进行反序列化）
public struct TileSection : INetPacket, ILengthAware
{
    public void ReadContent(ref void* ptr, void* end_ptr);  // 必须读取到 end_ptr
}
```

#### USP 的网络补丁

**UnifiedNetworkPatcher** (src/UnifierTSL/Network/UnifiedNetworkPatcher.cs:10-31) hook USP 初始化以重定向到共享数组：

```csharp
On.Terraria.NetplaySystemContext.StartServer += (orig, self) =>
{
    ServerContext server = self.root.ToServer();
    self.Connection.ResetSpecialFlags();
    self.ResetNetDiag();

    // 用全局共享数组替换每服务器数组
    self.Clients = UnifiedServerCoordinator.globalClients;
    server.NetMessage.buffer = UnifiedServerCoordinator.globalMsgBuffers;

    // 禁用每服务器广播（协调器处理）
    On.Terraria.NetplaySystemContext.StartBroadCasting = (_, _) => { };
    On.Terraria.NetplaySystemContext.StopBroadCasting = (_, _) => { };
};
```

#### 最佳实践

1. **数据包处理器**：
   - 早期注册（在 `InitializeAsync` 或 `BeforeGlobalInitialize` 中）
   - 谨慎使用 `Highest` 优先级（仅用于安全检查）
   - 始终在 `DisposeAsync()` 中取消注册

2. **数据包修改**：
   - 尽可能优先使用 `Cancel` 而非 `Overwrite`（开销更小）
   - 在设置 `Overwrite` 之前验证修改的数据包
   - 使用 `PacketProcessed` 回调进行遥测，而非逻辑

3. **服务器传输**：
   - 优雅地处理 `PreServerTransfer` 取消
   - 在 `PostServerTransfer` 中更新插件特定状态
   - 不要缓存 `clientCurrentlyServers` 引用

4. **内存管理**：
   - 数据包序列化使用 `ArrayPool<byte>.Shared` - 永远不要缓存缓冲区
   - `LocalClientSender` 实例在 `UnifiedServerCoordinator.clientSenders` 中池化

#### 性能特征

**数据包处理**：
- 处理器查找：按数据包 GlobalID 的 O(1) 数组索引
- 处理器链：O(n × log m)，其中 n = 处理器数量，m = 过滤检查
- 序列化：每个数据包约 100-500 ns（不安全指针，无分配）

**缓冲区池化**：
- 标准数据包：1 KB 缓冲区（租用时间约 50 ns）
- TileSection 数据包：16 KB 缓冲区
- 约 95% 缓冲区重用率（在 TShock 生产中测量）

**多服务器路由**：
- 客户端服务器查找：O(1) 易失性读取
- 服务器切换：约 10-20 μs（原子指针交换 + 事件调用）

### 2.5 日志基础设施

UnifierTSL 提供**高性能结构化日志系统**，具有元数据注入、服务器范围路由和基于 ArrayPool 的分配，实现零 GC 日志记录。

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
    public string Role { get; init; }           // Logger 范围（如 "TShockAPI"、"Log"）
    public string? Category { get; init; }      // 子类别（如 "ConnectionAccept"）
    public string Message { get; init; }
    public LogLevel Level { get; init; }
    public DateTime Timestamp { get; init; }
    public Exception? Exception { get; init; }
    public LogEventId? EventId { get; init; }
    public ref readonly TraceContext TraceContext { get; }

    private MetadataCollection metadata;  // ArrayPool 支持的排序集合

    public void SetMetadata(string key, string value) {
        metadata.Set(key, value);  // 二分查找插入
    }
}
```

#### RoleLogger - 范围日志

**RoleLogger** (src/UnifierTSL/Logging/RoleLogger.cs) 用宿主上下文包装 `Logger`：

```csharp
public class RoleLogger
{
    private readonly Logger logger;
    private readonly ILoggerHost host;
    private ImmutableArray<ILogMetadataInjector> injectors = [];

    public void Log(LogLevel level, string message, ReadOnlySpan<KeyValueMetadata> metadata = default)
    {
        // 1. 创建条目
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

**MetadataCollection** (src/UnifierTSL/Logging/Metadata/MetadataCollection.cs) - 排序的键值存储：

```csharp
public ref struct MetadataCollection
{
    private Span<KeyValueMetadata> _entries;  // ArrayPool 缓冲区
    private int _count;

    public void Set(string key, string value)
    {
        // 二分查找插入点
        int index = BinarySearch(key);
        if (index >= 0) {
            // 键存在 - 更新值
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

        // 通过 ArrayPool 增长
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

#### ConsoleLogWriter - 带颜色编码的输出

**ConsoleLogWriter** (src/UnifierTSL/Logging/LogWriters/ConsoleLogWriter.cs) - 服务器路由的控制台输出：

```csharp
public class ConsoleLogWriter : ILogWriter
{
    public void Write(in LogEntry raw)
    {
        // 1. 检查服务器特定路由
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
            // 使用颜色代码格式化段
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
| LogLevel | 级别文本 | 前景色 |
|----------|---------|--------|
| Trace | `[Trace]` | Gray |
| Debug | `[Debug]` | Blue |
| Info | `[+Info]` | White |
| Success | `[Succe]` | Green |
| Warning | `[+Warn]` | Yellow |
| Error | `[Error]` | Red |
| Critical | `[Criti]` | DarkRed |
| (未知) | `[+-·-+]` | White |

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

带异常（已处理 - Level ≤ Warning）：
```
[Level][Role|Category] Message
 │ Handled Exception:
 │   Exception line 1
 │   Exception line 2
 └── Exception line N
```

带异常（意外 - Level > Warning）：
```
[Level][Role|Category] Message
 │ Unexpected Exception:
 │   Exception line 1
 │   Exception line 2
 └── Exception line N
```

**段结构**：
- **段 0**：级别文本（按级别着色）
- **段 1**：Role/Category 文本（青色前景，黑色背景）
- **段 2**：主消息，多行使用框绘制字符（按级别着色）
- **段 3**（可选）：异常详情，带框绘制字符（红色前景，白色背景）

#### TraceContext - 请求关联

**TraceContext** (src/UnifierTSL/Logging/LogTrace/TraceContext.cs) - 分布式跟踪上下文：

```csharp
[StructLayout(LayoutKind.Explicit, Size = 40)]
public readonly struct TraceContext(Guid correlationId, TraceId traceId, SpanId spanId)
{
    [FieldOffset(00)] public readonly Guid CorrelationId = correlationId;  // 16 字节
    [FieldOffset(16)] public readonly TraceId TraceId = traceId;           // 8 字节 (ulong)
    [FieldOffset(32)] public readonly SpanId SpanId = spanId;              // 8 字节 (ulong)
}
```

**用法**：
```csharp
TraceContext trace = new(
    Guid.NewGuid(),                                 // 唯一请求 ID
    new TraceId((ulong)DateTime.UtcNow.Ticks),     // 逻辑跟踪链
    new SpanId((ulong)Thread.CurrentThread.ManagedThreadId)  // 操作跨度
);

logger.Log(LogLevel.Info, "Player authenticated", in trace,
    metadata: stackalloc[] { new("PlayerId", playerId.ToString()) });
```

#### ServerContext 集成

**ServerContext** (src/UnifierTSL/Servers/ServerContext.cs) 同时实现 `ILoggerHost` 和 `ILogMetadataInjector`：

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
        entry.SetMetadata("ServerContext", this.Name);  // 自动注入服务器名称
    }
}
```

**每服务器路由**：
```
Server1.Log.Info("Player joined")
    ↓
LogEntry with metadata["ServerContext"] = "Server1"
    ↓
ConsoleLogWriter 检测元数据，路由到 Server1.Console
    ↓
在 Server1 的控制台窗口输出，带有服务器特定颜色
```

#### 性能特征

**日志开销**：
- 简单日志（无元数据）：约 200-400 ns
- 结构化日志（5 个元数据键）：约 800-1200 ns
- 零分配：仅栈的 `LogEntry`，池化的 `MetadataCollection`

**内存**：
- 元数据缓冲区起始：4 个条目（池化）
- 自动调整大小：4 → 8 → 16 个条目
- 约 90% 的日志使用 ≤4 个元数据键（无重新分配）

**吞吐量**：
- 约 100-200 万日志/秒（单线程，无过滤）
- 控制台输出：约 5 万日志/秒（I/O 受限）
- 文件输出：约 50 万日志/秒（缓冲写入）

#### 最佳实践

1. **元数据优于字符串插值**：
   ```csharp
   // 不好：创建字符串分配
   logger.Info($"Player {playerId} joined");

   // 好：使用结构化元数据
   logger.Info("Player joined", metadata: stackalloc[] {
       new("PlayerId", playerId.ToString())
   });
   ```

2. **类别范围**：
   ```csharp
   // 为相关日志块设置类别
   serverContext.CurrentLogCategory = "WorldGen";
   GenerateWorld();
   serverContext.CurrentLogCategory = null;
   ```

3. **自定义元数据注入器**：
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

4. **异常日志**：
   ```csharp
   try {
       DangerousOperation();
   }
   catch (Exception ex) {
       logger.LogHandledException("Operation failed", ex, category: "DangerousOperation");
       // 异常详情在控制台输出中自动格式化
   }
   ```

有关扩展日志示例，请参阅 `src/Plugins/TShockAPI/TShock.cs` 和 `src/UnifierTSL/Servers/ServerContext.cs`。

### 2.6 配置服务
- `ConfigRegistrar` 实现 `IPluginConfigRegistrar`，用于 `config/<PluginName>/` 下的插件配置文件。
- `CreateConfigRegistration<T>` 生成 `ConfigRegistrationBuilder`，启用默认值、序列化选项、错误策略（`DeserializationFailureHandling`）和外部变更触发器。
- 完成的注册返回 `ConfigHandle<T>`，公开 `RequestAsync`、`Overwrite`、`ModifyInMemory` 和 `OnChangedAsync` 以支持热重载。文件访问使用 `FileLockManager` 以避免多线程环境中的损坏。

## 3. USP 集成点

- `ServerContext` 继承 USP `RootContext`，将 Unifier 服务连接到上下文中（自定义控制台、数据包接收器、日志元数据）。与 Terraria 世界/游戏状态的每次交互都通过此上下文流动。
- 网络补丁器（`UnifiedNetworkPatcher`）detour `NetplaySystemContext` 函数以重用共享缓冲区并在多服务器环境中强制执行协调的发送/接收路径。
- MonoMod `On.` detour 被谨慎使用，将 USP hook 桥接到 Unifier 管理的事件中。添加新 detour 时，优先将它们公开为 `EventHub` 提供者，以便下游插件可以依赖一致的 API。
- USP 的 TrProtocol 数据包结构被重新导出，供 `PacketSender` 和 `NetPacketHandler` 直接使用，保持与 USP 序列化语义的兼容性（长度感知数据包、`IExtraData` 等）。

## 4. 公共 API 界面

### 4.1 外观 (`UnifierApi`)
- `Initialize(string[] launcherArgs)` 启动运行时，连接事件，加载插件宿主，并解析启动器参数。
- `EventHub` 在初始化完成后公开分组的事件提供者。
- `PluginHosts` 惰性实例化 `PluginOrchestrator` 以进行宿主级交互。
- `CreateLogger(ILoggerHost, Logger? overrideLogCore = null)` 返回范围限定于调用者的 `RoleLogger` 并重用共享的 `Logger`。
- `UpdateTitle(bool empty = false)` 根据协调器状态控制窗口标题更新。
- `VersionHelper`、`FileMonitor` 和 `LogCore`/`Logger` 提供对共享实用程序的访问（版本信息、文件监视器、日志核心）。

### 4.2 事件负载与辅助工具
- 事件负载结构位于 `src/UnifierTSL/Events/*` 下并实现 `IEventContent`。专用接口（`IPlayerEventContent`、`IServerEventContent`）添加上下文特定的元数据。
- `HandlerPriority` 和 `FilterEventOption` 枚举定义调用顺序和过滤语义。
- 注册/取消注册处理器的辅助方法确保线程安全、低分配的操作。

### 4.3 模块与插件类型
- `ModulePreloadInfo`、`ModuleLoadResult`、`LoadedModule` 描述模块元数据和生命周期。
- `IPlugin`、`BasePlugin`、`PluginContainer`、`PluginInitInfo` 和 `IPluginHost` 定义插件契约和容器。
- 配置界面包括 `IPluginConfigRegistrar`、`ConfigHandle<T>` 和 `ConfigFormat` 枚举。

### 4.4 网络 API
- `PacketSender` 公开固定/动态数据包发送辅助工具加上服务器端变体。
- `NetPacketHandler` 提供 `Register<TPacket>`、`ProcessBytes` 和 `RecievePacketEvent<T>`。
- `LocalClientSender` 包装 `RemoteClient`，公开 `Kick`、`SendData` 和 `Client` 元数据。`ClientPacketReciever` 重放或重写入站数据包。
- 协调器辅助工具提供 `TransferPlayerToServer`、`SwitchJoinServerEvent` 和状态查询（`GetClientCurrentlyServer`、`Servers` 列表）。

### 4.5 日志与诊断
- `RoleLogger` 扩展方法（参见 `src/UnifierTSL/Logging/LoggerExt.cs`）提供严重性辅助工具（`Debug`、`Info`、`Warning`、`Error`、`Success`、`LogHandledException`）。
- `LogEventIds` 枚举标准事件标识符以对日志输出进行分类。
- 事件提供者公开 `HandlerCount`，`EventProvider.AllEvents` 可用于诊断仪表板。

## 5. 运行时生命周期与操作

### 5.1 启动序列
1. 启动器解析 CLI 并设置日志记录。
2. `ModuleAssemblyLoader.Load` 扫描 `plugins/`，暂存程序集并准备依赖提取。
3. 插件宿主发现合格的入口点并实例化 `IPlugin` 实现。
4. `BeforeGlobalInitialize` 在每个插件上同步运行，启用跨插件服务连接。
5. `InitializeAsync` 为每个插件运行；编排器传递先前的初始化任务，以便可以等待依赖。
6. `UnifiedServerCoordinator` 配置服务器上下文，调用 USP 启动世界，并注册事件桥接器。

### 5.2 运行时操作
- 事件处理器捕获横切逻辑（聊天审核、传输控制、数据包过滤）。
- 配置句柄响应文件更改，允许无需重启的运行时调整。
- 协调器更新窗口标题，维护服务器列表，并管理加入/离开重放。
- 日志元数据确保每个日志都可以关联到服务器、插件或子系统。

### 5.3 关闭与重载
- 关闭路径：编排器请求每个插件 `ShutdownAsync`，然后卸载模块（遵守依赖图），并释放加载上下文。
- 模块重载（用于更新的 DLL）触发 `ModuleAssemblyLoader.TryLoadSpecific`，如果哈希不同，则重新初始化相关插件。
- `ForceUnload` 和 `TryUnloadPlugin` 允许在插件发出释放信号时进行针对性的卸载操作。

### 5.4 诊断
- 事件提供者发布处理器计数以供可观察性使用；通过枚举 `EventProvider.AllEvents` 与工具集成。
- `PacketSender.SentPacket` 和 `NetPacketHandler.RecievePacketEvent` 可以为网络跟踪发出指标或日志。
- 日志元数据注入器提供每服务器/每插件标签，以便在外部接收器中进行过滤。

## 6. 扩展性指南与最佳实践

- **优先使用事件提供者** — 在插件中添加新的 MonoMod detour 之前，检查是否存在提供者，或通过核心项目扩展 `EventHub` 以保持行为一致。
- **遵守上下文边界** — 始终通过 `ServerContext` 和 USP 上下文 API 操作，以避免跨服务器错误。
- **管理加载上下文** — 在发布带有原生/托管依赖的模块时，实现 `ModuleDependenciesAttribute` 提供者，以便加载器跟踪负载并干净地卸载。
- **避免事件中的异步间隙** — `ref struct` 事件参数需要同步处理器。如果需要异步工作，捕获必要的状态并调度任务，而不持有参数引用。
- **协调初始化** — 使用 `PluginInitInfo` 等待先决条件插件，而不是仅依赖顺序假设。
- **日志纪律** — 通过 `UnifierApi.CreateLogger` 创建 logger 以继承元数据注入和控制台格式化；为关联数据添加自定义 `ILogMetadataInjector` 实例。
- **测试策略** — 围绕事件处理器和协调器流程锚定集成测试（例如，模拟数据包序列）。USP 上下文可以独立实例化以进行验证。
- **性能考虑** — 事件的注册/取消注册是线程安全的，但并非微不足道；在启动期间批量注册。使用池化缓冲区（数据包发送器）并避免在热路径内分配。
- **诊断 Hook** — 利用 `PacketSender.SentPacket` 和事件过滤器构建监控插件，而无需修改核心运行时。

---

## 附录：关键术语对照

为便于理解，以下是文档中出现的关键技术术语及其中英文对照：

**核心概念**：
- **Runtime（运行时）** - 程序执行环境
- **Context（上下文）** - 执行环境的状态容器
- **Detour** - MonoMod 运行时方法拦截机制
- **Hook** - 在特定执行点插入自定义代码
- **Assembly（程序集）** - .NET 编译单元（DLL/EXE）
- **Collectible（可收集的）** - 可被 GC 回收的对象

**模块系统**：
- **Core Module（核心模块）** - 独立可加载的功能单元
- **Satellite Module（卫星模块）** - 依赖核心模块的扩展
- **Load Context（加载上下文）** - 程序集隔离边界
- **Dependency（依赖）** - 模块所需的外部库
- **RID (Runtime Identifier)** - 运行时平台标识符

**事件系统**：
- **Event Provider（事件提供者）** - 事件发布源
- **Handler（处理器）** - 事件响应函数
- **Priority（优先级）** - 处理器执行顺序
- **Cancellation（取消）** - 阻止后续处理
- **Filter（过滤器）** - 处理器执行条件

**网络系统**：
- **Packet（数据包）** - 网络传输的数据单元
- **Coordinator（协调器）** - 多服务器管理中心
- **Transfer（传输）** - 玩家在服务器间移动
- **Buffer（缓冲区）** - 临时数据存储区域
- **Pool（池）** - 可重用对象集合

**日志系统**：
- **Logger（日志器）** - 日志记录器
- **Metadata（元数据）** - 日志附加信息
- **Injector（注入器）** - 自动添加元数据的组件
- **Writer（写入器）** - 日志输出目标
- **Trace（跟踪）** - 请求链路追踪

**插件系统**：
- **Plugin（插件）** - 功能扩展模块
- **Host（宿主）** - 插件运行环境
- **Orchestrator（编排器）** - 插件生命周期管理器
- **Container（容器）** - 插件实例包装器
- **Discovery（发现）** - 插件自动识别过程

这些术语在整个 UnifierTSL 架构中频繁使用，理解它们有助于更好地掌握系统的设计理念和实现细节。
