# 插件开发与迁移指南

本指南将解释如何为 UnifierTSL 构建插件、如何利用其运行时服务，以及如何迁移现有的 TShock 或 OTAPI 插件。

## 重要的最佳实践

- **始终从 `ServerContext` 派生上下文。** UnifierTSL 假设每个活跃的根上下文都是 `ServerContext`（或其子类），这样 USP detour 就能安全地调用 `ToServer(this RootContext)`。创建不继承自 `ServerContext` 的自定义 `RootContext` 在运行时会抛出异常；保持 detour 和辅助函数使用 `ToServer()`，以便它们与框架的生命周期管理保持一致。
- **使用 `SampleServer` 处理临时上下文。** 当你需要一个静态的示例上下文用于只关心类型的 API 调用时（例如 `Item.SetDefaults(RootContext ctx, int itemType)`），使用缓存的 `SampleServer` 或其子类。它重写了控制台连接，因此不会弹出额外的控制台窗口，同时仍然满足 `ServerContext` 继承要求。
- **通过调用点传递上下文而不是缓存它们。** 推荐将相关上下文作为方法参数传递。只有在所有者是通过 `ServerContext.RegisterExtension()` 创建的情况下才长期持有上下文，这样其生命周期与服务器匹配。在其他地方缓存上下文可能会阻止垃圾回收并泄露服务器。
- **依赖平台服务而不是自己重新造轮子。** 通过 `IPluginConfigRegistrar` 构建配置，使用 `UnifierApi.CreateLogger` 创建基于角色的日志记录器，并通过 `EventHub` 提供程序公开可重用的钩子。这使插件与协调器（coordinator）生命周期规则、结构化日志记录和共享事件界面保持一致。

## 1. 入门指南

大多数插件作者可以在 UnifierTSL 仓库外工作，通过引用每个版本都附带的已发布的 `UnifierTSL` NuGet 包。只有在需要调试运行时或贡献运行时更改时，才需要直接使用源代码树。

### 1.1 NuGet 快速开始（推荐）

**场景：** 构建 `WelcomePlugin`，一个响应聊天中 `!hello` 命令并回复绿色欢迎消息的模块。

1. 创建一个 .NET 9 类库并启用可空性/隐式 using 以匹配运行时风格。在脚手架之后删除生成的 `Class1.cs` 文件。

   ```
   dotnet new classlib -n WelcomePlugin -f net9.0
   ```

   更新 `WelcomePlugin.csproj`，使 `<PropertyGroup>` 包含：

   ```xml
   <ImplicitUsings>enable</ImplicitUsings>
   <Nullable>enable</Nullable>
   ```

2. 引用运行时包。版本要匹配你目标的 UnifierTSL 版本（启动器在启动时打印版本横幅，`UnifierTSL.runtimeconfig.json` 列出相同的值）：

   ```
   dotnet add package UnifierTSL --version <runtime-version>
   ```

3. 添加你的插件入口点。从最小可行的存根开始，然后分层添加行为。

   _最小骨架_

   ```csharp
   using System.Collections.Immutable;
   using System.Threading;
   using System.Threading.Tasks;
   using UnifierTSL.Plugins;

   [assembly: CoreModule]

   namespace WelcomePlugin
   {
       [PluginMetadata("WelcomePlugin", "1.0.0", "Contoso", "Replies to !hello with a welcome message.")]
       public sealed class Plugin : BasePlugin
       {
           public override Task InitializeAsync(
               IPluginConfigRegistrar registrar,
               ImmutableArray<PluginInitInfo> prior,
               CancellationToken cancellationToken = default) =>
               Task.CompletedTask;
       }
   }
   ```

   `[assembly: CoreModule]` 将程序集标记为核心模块（可参考 [README 插件系统章节](../README.md#plugin-system) 了解运行时总览）。这个标识允许其他程序集使用 `[RequiresCoreModule("CoreModuleName")]` 声明自己为"卫星模块"，这些模块将在同一个 `AssemblyLoadContext` 中加载并共享依赖项。`[PluginMetadata]` 发布在加载顺序、日志条目和发布器（Publisher）输出中显示的插件身份。加载器基于这些属性而不是初始化行为来发现和组织模块。

   _在初始化期间订阅并记录就绪状态_

   将以下成员添加到 `Plugin` 类（保留存根中的属性），并确保文件导入 `UnifierTSL`、`UnifierTSL.Events.Handlers` 和 `UnifierTSL.Logging`：

   ```csharp
   public sealed class Plugin : BasePlugin, ILoggerHost
   {
       readonly RoleLogger logger;

       public Plugin()
       {
           logger = UnifierApi.CreateLogger(this);
       }

       public string Name => "WelcomePlugin";
       public string? CurrentLogCategory => null;

       public override Task InitializeAsync(
           IPluginConfigRegistrar registrar,
           ImmutableArray<PluginInitInfo> prior,
           CancellationToken cancellationToken = default)
       {
           UnifierApi.EventHub.Chat.MessageEvent.Register(OnChatMessage, HandlerPriority.Normal);
           logger.Info("WelcomePlugin ready. Type !hello in chat to test.");
           return Task.CompletedTask;
       }
   }
   ```

   _添加卸载钩子以便清理注册_

   将此重写放在同一个类中。它使用 `BasePlugin` 的处置钩子（`DisposeAsync(bool isDisposing)`）来取消订阅：

   ```csharp
   public override ValueTask DisposeAsync(bool isDisposing)
   {
       if (!isDisposing)
       {
           return ValueTask.CompletedTask;
       }

       UnifierApi.EventHub.Chat.MessageEvent.UnRegister(OnChatMessage);
       return ValueTask.CompletedTask;
   }
   ```

   _处理你注册的聊天回调_

   将此方法添加到类中，并导入 `System`、`Microsoft.Xna.Framework` 和 `UnifierTSL.Events.Core`：

   ```csharp
   static void OnChatMessage(ref ReadonlyEventArgs<MessageEvent> args)
   {
       if (!args.Content.Sender.IsClient)
       {
           return;
       }

       var text = args.Content.Text.Trim();
       if (string.Equals(text, "!hello", StringComparison.OrdinalIgnoreCase))
       {
           args.Content.Sender.Chat("Hello from WelcomePlugin!", Color.LightGreen);
           args.Handled = true;
       }
   }
   ```

   <details>
   <summary>完整示例</summary>

   ```csharp
   using System;
   using System.Collections.Immutable;
   using System.Threading;
   using System.Threading.Tasks;
   using Microsoft.Xna.Framework;
   using UnifierTSL;
   using UnifierTSL.Events.Core;
   using UnifierTSL.Events.Handlers;
   using UnifierTSL.Logging;
   using UnifierTSL.Plugins;

   [assembly: CoreModule]

   namespace WelcomePlugin
   {
       [PluginMetadata("WelcomePlugin", "1.0.0", "Contoso", "Replies to !hello with a welcome message.")]
       public sealed class Plugin : BasePlugin, ILoggerHost
       {
           readonly RoleLogger logger;

           public Plugin()
           {
               logger = UnifierApi.CreateLogger(this);
           }

           public string Name => "WelcomePlugin";
           public string? CurrentLogCategory => null;

           public override int InitializationOrder => 0;

           public override Task InitializeAsync(
               IPluginConfigRegistrar registrar,
               ImmutableArray<PluginInitInfo> prior,
               CancellationToken cancellationToken = default)
           {
               UnifierApi.EventHub.Chat.MessageEvent.Register(OnChatMessage, HandlerPriority.Normal);
               logger.Info("WelcomePlugin ready. Type !hello in chat to test.");
               return Task.CompletedTask;
           }

           public override ValueTask DisposeAsync(bool isDisposing)
           {
               if (isDisposing)
               {
                   UnifierApi.EventHub.Chat.MessageEvent.UnRegister(OnChatMessage);
               }

               return ValueTask.CompletedTask;
           }

           static void OnChatMessage(ref ReadonlyEventArgs<MessageEvent> args)
           {
               if (!args.Content.Sender.IsClient)
               {
                   return;
               }

               var text = args.Content.Text.Trim();
               if (string.Equals(text, "!hello", StringComparison.OrdinalIgnoreCase))
               {
                   args.Content.Sender.Chat("Hello from WelcomePlugin!", Color.LightGreen);
                   args.Handled = true;
               }
           }
       }
   }
   ```

   </details>

4. 构建并将插件复制到你的 UnifierTSL 安装目录：

   ```
   dotnet build WelcomePlugin/WelcomePlugin.csproj -c Debug
   mkdir -p <unifier-install>/plugins/WelcomePlugin
   cp WelcomePlugin/bin/Debug/net9.0/WelcomePlugin.dll <unifier-install>/plugins/WelcomePlugin/
   ```

   将任何额外的程序集（卫星模块或依赖项）与主 DLL 一起保存在插件文件夹内。

5. 启动 UnifierTSL 运行时（来自版本下载或你现有的部署）。控制台日志将在启动期间报告 `WelcomePlugin`。加入服务器并发送 `!hello` 现在会向玩家打印绿色欢迎消息，演示了属性连接、事件注册和日志记录。

### 1.2 从源代码工作

需要完整的调试体验或想要自己打包运行时？克隆仓库并遵循 [`从源代码运行`](./README.zh-cn.md#quick-start) 检查清单，然后在 `src/Plugins/` 下创建或复制插件项目。

- 通过复制 `src/Plugins/ExamplePlugin` 作为模板来创建插件，或创建新的类库：
  ```
  dotnet new classlib -n WelcomePlugin -f net9.0
  ```
  在 `src/Plugins/` 内，然后更新 `.csproj` 以启用 `<ImplicitUsings>enable</ImplicitUsings>` 和 `<Nullable>enable</Nullable>`。
- 引用运行时项目而不是 NuGet 包：

  ```xml
  <ItemGroup>
    <ProjectReference Include="..\..\UnifierTSL\UnifierTSL.csproj" />
  </ItemGroup>
  ```

- 将项目添加到解决方案，以便与其他所有内容一起构建：

  ```
  dotnet sln src/UnifierTSL.slnx add src/Plugins/WelcomePlugin/WelcomePlugin.csproj
  ```

- 完全按照上面 NuGet 快速开始指南中显示的方式实现插件逻辑（示例的第 3-5 节），使用相同的事件注册、日志记录和生命周期模式。

- 构建和发布：使用 **Publisher（发布器）** 生成包含所有插件集成的适当可分发包。将 `win-x64` 替换为你需要的运行时标识符（例如 `linux-x64`、`osx-arm64`）：
  ```
  dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64
  ```
  这是测试插件的**唯一可靠方式**，因为它确保了正确的文件布局（`plugins/`、`lib/`、`config/` 目录）和与生产部署匹配的依赖项解析。直接的 `dotnet build` 或 `dotnet run` 命令不会触发插件编译或保证 UnifierTSL 以有效的运行时结构启动。

- 要生成包含你的插件的可分发包，应用相同的 Publisher 命令。输出将准备好部署到生产系统。


### Publisher（发布器）输出行为

发布器有两种不同的输出模式，由 `--output-path` 参数控制：

**默认行为（未指定 `--output-path`）：**
- 输出目录：`src/UnifierTSL.Publisher/bin/Release/net9.0/utsl-<rid>/`
- 此默认值使用 Publisher 项目自己的 Release 构建文件夹，这与仓库结构保持兼容。
- 发布器通过向上搜索最多 5 个目录来查找 `.sln` 或 `.slnx` 文件来自动定位解决方案根目录，因此无论你是通过 `dotnet run` 调用还是从编译的二进制文件调用，这都能正确工作。

**自定义输出位置（指定了 `--output-path`）：**
- 使用 `--output-path` 指定任何其他目录：
  ```
  dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64 --output-path ./bin
  ```
- `--output-path` 参数接受绝对路径和相对路径：
  - **绝对路径** 按原样使用。
  - **相对路径** 相对于你调用发布器的当前工作目录解析（不是解决方案根目录）。

在两种情况下，发布器都会写入 `<output-path>/utsl-<rid>/`，将 `src/Plugins/` 中的每个插件复制到已发布应用程序的 `plugins/` 文件夹中。

**在重新运行时保留现有输出：**
默认情况下，发布器在写入之前会清理输出目录。如果你重新运行发布器来更新现有部署并想要保留其他文件（例如生成的配置、保存的世界数据），请追加 `--clean-output-dir false`：
```
dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64 --output-path ./bin --clean-output-dir false
```
没有此标志，输出文件夹会被删除并重新创建，这对于干净构建很有用，但在更新实时部署时具有破坏性。

**排除特定插件：**
可选地追加 `--excluded-plugins` 以从包中省略特定插件：
```
dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64 --excluded-plugins ExamplePlugin
```

**跳过 RID 子文件夹：**
为了开发便利，你可以追加 `--use-rid-folder false` 来直接写入你的输出文件夹而不使用 `utsl-<rid>/` 子文件夹，这对于在单个目标平台上迭代很有用：

```
dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64 --output-path ./bin --use-rid-folder false
```
这会写入 `./bin/plugins/` 而不是 `./bin/utsl-win-x64/plugins/`。

- 推荐：添加构建后步骤将你的插件直接复制到发布器输出中，这样增量插件更改就不需要重新运行发布器。这保持你的开发周期快速：

  ```xml
  <Target Name="CopyToPublish" AfterTargets="Build">
    <PropertyGroup>
      <PublishRid>win-x64</PublishRid>
      <PublishOutputFolder>$(MSBuildProjectDirectory)/../../bin</PublishOutputFolder>
      <PublishFolder>$(PublishOutputFolder)/utsl-$(PublishRid)/plugins</PublishFolder>
    </PropertyGroup>
    <MakeDir Directories="$(PublishFolder)" />
    <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(PublishFolder)" />
  </Target>
  ```

  如果你已经用 `--use-rid-folder false` 配置了发布器，请相应调整 `PublishFolder` 路径：

  ```xml
  <PublishFolder>$(PublishOutputFolder)/plugins</PublishFolder>
  ```

这个工作流程让你在快速的 NuGet 驱动迭代和完整源代码调试之间切换，而无需重写你的插件。

## 2. 插件生命周期与托管

### 2.1 `IPlugin` 契约
实现 `IPlugin` 接口（`src/UnifierTSL/Plugins/IPlugin.cs`）或继承提供默认值的 `BasePlugin`：

```csharp
public sealed class MyPlugin : BasePlugin
{
    public override PluginMetadata Metadata { get; } =
        new("MyPlugin", new Version(1, 0, 0), "Me", "Sample");

    public override int InitializationOrder => 10;

    public override void BeforeGlobalInitialize(ImmutableArray<IPluginContainer> plugins)
    {
        // 连接依赖项或注册需要其他插件存在的事件。
    }

    public override async Task InitializeAsync(
        IPluginConfigRegistrar registrar,
        ImmutableArray<PluginInitInfo> prior,
        CancellationToken token)
    {
        // 注册配置，设置服务，启动后台任务。
    }

    public override Task ShutdownAsync(CancellationToken token)
        => Task.CompletedTask;

    public override ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
}
```

### 2.2 初始化顺序
- 插件按 `InitializationOrder` 然后按类型名称排序。对基础系统使用低数字，对扩展使用高数字。
- `BeforeGlobalInitialize` 在所有插件实例存在之后但在 `InitializeAsync` 之前执行。用它来获取其他插件的引用或注册共享服务。
- `InitializeAsync` 接收一个 `ImmutableArray<PluginInitInfo>`，描述在它之前加载的插件及其初始化 `Task`。可以通过await该`Task`来等待你依赖的特定任务，而不是假设完成顺序：

```csharp
var tshockInit = prior.FirstOrDefault(p => p.Metadata.Name == "TShock");
if (tshockInit.InitializationTask is { } task)
{
    await task.ConfigureAwait(false);
}
```

## 3. 配置管理

### 3.1 注册配置
- 从传递给 `InitializeAsync` 的 `IPluginConfigRegistrar` 获取配置注册：

```csharp
var configHandle = registrar
    .CreateConfigRegistration<MyConfig>("config.json")
    .WithDefault(() => new MyConfig { Enabled = true, CooldownSeconds = 30 })
    .TriggerReloadOnExternalChange(true)
    .Complete();

MyConfig config = await configHandle.RequestAsync(cancellationToken: token);
```

- 配置文件位于 `config/<PluginName>/` 下。每个插件可以注册多个配置。

### 3.2 错误处理与重载
- `OnDeserializationFailure(DeserializationFailureHandling.ReturnNewInstance, autoPersistFallback: false)` 决定是保留默认值还是显示错误。
- `OnSerializationFailure(SerializationFailureHandling.WriteNewInstance)` 控制如何处理写入错误（例如，重写默认值而不是抛出异常）。
- 使用 `TriggerReloadOnExternalChange(true)` 来按注册选择热重载，或通过 `configRegistrar.DefaultOption` 全局选择热重载。
- `OnChangedAsync` 让你对外部编辑做出反应。处理程序返回 `ValueTask<bool>`；返回 `true` 表示你已经处理了更改（跳过自动缓存更新）：

```csharp
configHandle.OnChangedAsync += async (sender, updatedConfig) =>
{
    // 验证并应用新设置
    return true; // true = 已处理，跳过自动缓存更新
};
```

- 当程序化写入配置时使用 `ModifyInMemory` 或 `Overwrite`；注册器使用 `FileLockManager` 保护文件访问。

## 4. 事件与钩子集成

### 4.1 使用 `UnifierApi.EventHub`
- 通过特定域的属性访问提供程序：`UnifierApi.EventHub.Chat.ChatEvent`、`UnifierApi.EventHub.Coordinator.SwitchJoinServer`、`UnifierApi.EventHub.Game.PreUpdate` 等。
- 选择匹配你场景的提供程序类型：
  - `ValueEventProvider<T>` 用于具有取消功能的可变负载（`Handled`、`StopPropagation`）。
  - `ReadonlyEventProvider<T>` 当你只检查数据但仍想要否决语义时。
  - `ValueEventNoCancel` 或 `ReadonlyEventNoCancel` 用于通知风格的钩子。
- 指定 `HandlerPriority` 以在其他处理程序之前/之后运行。使用 `FilterEventOption.Handled` 只在先前的处理程序将事件标记为已处理时做出反应。

```csharp
UnifierApi.EventHub.Chat.ChatEvent.Register(
    (ref ValueEventArgs<ChatEvent> args) =>
    {
        if (args.Content.Text.StartsWith("!servers", StringComparison.OrdinalIgnoreCase))
        {
            args.Content.Text = string.Join(", ",
                UnifiedServerCoordinator.Servers.Select(s => s.Name));
            args.Handled = true;
        }
    },
    priority: HandlerPriority.Highest);
```

### 4.2 桥接到 MonoMod 钩子
- 当不存在提供程序时，你可以使用 MonoMod 提供的 `On.` detour（例如 `On.Terraria.Main.Update`）。始终转发到原始委托以保持 USP 不变量。
- 当多个插件需要相同的钩子时，首选在核心运行时中添加新的事件提供程序。向 `src/UnifierTSL/Events/Handlers` 提交补丁。

### 4.3 常见事件域
- **Coordinator（协调器）** – 路由待处理连接，拦截传输，管理服务器列表更新。
- **Netplay** – 检查或取消数据包交换，检测套接字重置。
- **Game（游戏）** – 处理生命周期事件，如 `PreUpdate`、`PostUpdate`、困难模式图块更新。
- **Chat（聊天）** – 在原版处理之前操作玩家或控制台聊天。
- **Server（服务器）** – 对服务器添加/移除、控制台服务创建做出反应。

## 5. 网络与数据交换

### 5.1 发送数据包
- `PacketSender.SendFixedPacket` 针对具有可预测大小的非托管数据包；`SendDynamicPacket` 处理分配自己缓冲区的托管 `IManagedPacket` 负载。
- `_S` 变体（例如 `SendFixedPacket_S`）在分派之前切换 `ISideSpecific.IsServerSide` 标志。
- 通过 `UnifiedServerCoordinator.clientSenders[clientId]` 检索 `LocalClientSender`。当它们存在时，首选它公开的更高级别的辅助程序（如 `Kick`），以便协调器记录保持同步。

```csharp
using Terraria.Localization;

var sender = UnifiedServerCoordinator.clientSenders[clientId];

// 示例：告诉客户端移除它拥有的弹幕（固定大小数据包）。
var killProjectile = new TrProtocol.NetPackets.KillProjectile(
    (short)projectileIndex,
    (byte)clientId);
sender.SendFixedPacket(in killProjectile);

// 踢出客户端？使用辅助程序，以便正确更新终止标志。
sender.Kick(NetworkText.FromLiteral("Maintenance window"));
```

### 5.2 接收与修改数据包
- 通过 `NetPacketHandler.Register<TPacket>` 注册处理程序，以在 Terraria 的 `NetMessage.GetData` 处理之前拦截数据包：

```csharp
NetPacketHandler.Register<TrProtocol.NetPackets.ChatMessage>(
    (ref RecievePacketEventArgs<TrProtocol.NetPackets.ChatMessage> args) =>
    {
        if (args.Packet.Message.StartsWith("/secret"))
        {
            args.HandleMode = PacketHandleMode.Cancel;
        }
    });
```

- 设置 `HandleMode = PacketHandleMode.Overwrite` 并修改 `args.Packet` 来重写数据包；处理程序将重新序列化并通过 `ClientPacketReciever` 分派它。
- `RecieveBytesInfo` 事件公开原始缓冲区用于调试异常数据包。

### 5.3 传输与协调器辅助程序
- `UnifiedServerCoordinator.TransferPlayerToServer` 在服务器之间迁移客户端。将调用包装在 try/catch 中，并通过 `PreServerTransferEvent` 尊重取消。
- 使用 `ServerContext.SyncPlayerJoinToOthers` 和相关方法在自定义迁移后对齐玩家可见性。
- 查询 `UnifiedServerCoordinator.Servers` 获取活跃上下文；每个都公开 `ServerName`、`Root` 上下文 API 和注册的扩展。

## 6. 日志记录与诊断

### 6.1 角色日志记录器
- 实现 `ILoggerHost`（例如，提供 `Name`、可选的 `CurrentLogCategory`）并调用 `UnifierApi.CreateLogger(this)` 来获得 `RoleLogger`。
- 利用 `src/UnifierTSL/Logging/LoggerExt.cs` 中的扩展方法进行严重性特定的日志记录（`Debug`、`Info`、`Warning`、`Error`、`Success`、`LogHandledException`）。
- 通过实现 `ILogMetadataInjector` 或传递 span 来注入额外的元数据：

```csharp
log.Info("Teleporting player", metadata: stackalloc[]
{
    new KeyValueMetadata("Player", player.Name),
    new KeyValueMetadata("TargetServer", target.ServerName),
});
```

### 6.2 诊断钩子
- 订阅 `PacketSender.SentPacket` 进行流量审计。
- 事件提供程序为工具公开 `HandlerCount`；迭代 `EventProvider.AllEvents` 来显示运行时仪表板。
- 使用结构化元数据（例如，传送目标、配置名称）来简化外部接收器中的日志过滤。

## 7. 迁移传统插件与框架

### 7.1 围绕 USP 定位
- **上下文优先思维** – USP 将 Terraria 静态变量替换为每服务器上下文。审计传统 `Main`、`Netplay`、`NetMessage` 等的每个用法，并将它们迁移到对应的 `ServerContext`（`ctx.Main`、`ctx.Netplay`、`ctx.Router`）。`tmp/usp-doc/Developer-Guide.md#core-concepts` 中的表格列出了最常见的映射。
- **根上下文创建** – 许多传统启动器假设单个世界。移植初始化逻辑时，构造或解析 UnifierTSL 提供的 `RootContext`/`ServerContext`，而不是实例化你自己的。使用协调器辅助程序（`UnifiedServerCoordinator.Servers`）来枚举活跃上下文。
- **钩子表面** – 用 `EventHub` 提供程序或上下文绑定的 MonoMod 钩子替换 `TerrariaApi.Server` 或 OTAPI 静态钩子。如果你需要一个尚不存在的钩子，在你的插件中公开一个新的提供程序，以便其他迁移可以共享它。

### 7.2 翻译核心功能
- **命令与权限** – 传统 TShock 命令可以存在于 UnifierTSL 插件内。在 `InitializeAsync` 中注册它们，并通过构造函数注入或 `PluginHost` 解析依赖项。现有的 `CommandTeleport` 插件展示了命令元数据和执行的干净移植。
- **配置** – 将原始文件 IO 迁移到 `IPluginConfigRegistrar`。这将配置保存在 `config/<PluginName>/` 下，并启用热重载、回退处理和架构验证。
- **网络** – 静态数据包 detour 成为 `NetPacketHandler.Register<TPacket>` 处理程序。使用 TrProtocol 数据包类型进行序列化；它们自动尊重 USP 的上下文规则并强制执行长度边界。
- **世界与图块访问** – 将 `Tile` 或 `ITile` 操作替换为 USP 的 `TileCollection`、`TileData` 和 `RefTileData`。详细迁移步骤见 [USP的开发指南](https://github.com/CedaryCat/OTAPI.UnifiedServerProcess/Developer-Guide.md#world-data-tileprovider)。

### 7.3 案例研究：TShock 模块
- 捆绑的 `src/Plugins/TShockAPI` 插件演示了一个成熟的生态系统如何与 UnifierTSL 集成。在迁移以前直接扩展 TShock 的模组时，模仿它的方法。
- TShock 的权限树和日志记录现在通过 UnifierTSL 的 `RoleLogger` 流动，确保基于类别的过滤和结构化元数据在整个套件中工作。
- 依赖于 TShock 自定义序列化器的数据包级功能在移植中依赖于 TrProtocol 模型。这避免了手动缓冲区算术，并与 USP 的 IL 合并数据包定义保持兼容性。

### 7.4 已知陷阱
- USP 程序集中的 detour 已经接收当前逻辑管道的上下文；使用该参数而不是重新查询 `UnifiedServerCoordinator.GetClientCurrentlyServer(plr)`。只有当钩子真正在没有上下文的情况下运行并且你没有其他方法解析它时，才使用 `GetClientCurrentlyServer`。
- `ILengthAware`/`IExtraData` 只是表示需要保留尾随数据或提供长度元数据的数据包——在反序列化期间传递提供的结束指针（或等效的长度信息），以便压缩和尾部复制保持完整。它们的显式接口实现隐藏了不支持的 `ReadContent(void*)`，所以只有当你将数据包装箱到接口时才会出现问题；当你想要排除长度感知数据包时，用 `INonLengthAware` 约束泛型。
- 事件处理程序内的阻塞工作会使协调器停滞。将长时间运行的任务分派到后台服务（`Task.Run`、专用工作队列）并保持钩子响应。
- 传统全局单例（例如，由玩家索引键控的静态缓存）应该重写为存在于每上下文服务或协调器管理的字典内，以避免在服务器之间泄露状态。

## 8. 高级技术

### 8.1 自定义 Detour
- 使用 `MonoMod.RuntimeDetour` 应用超出提供事件的钩子。确保在 `ShutdownAsync` 或 `DisposeAsync` 期间处置 detour，以避免在插件卸载时出现陈旧补丁。
- 将 detour 与新的 `EventHub` 提供程序结合，为其他插件公开可重用的钩子。

### 8.2 附带额外模块
- 通过创建带有 `[ModuleDependencies]` 的核心模块来打包共享服务或本机库。实现 `IDependencyProvider` 将负载提取到 `plugins/<Module>/lib/`。
- 卫星模块可以携带可选功能、命令或数据迁移，而不会使核心插件 DLL 膨胀。

### 8.3 多服务器协调
- 通过在 `UnifiedServerCoordinator` 上注册的专用服务或通过插件特定的扩展字典维护共享状态。
- 通过将协调器事件（`SwitchJoinServer`、`ServerListChanged`）与数据包发送 API 结合来桥接跨服务器的聊天或游戏事件。
- 遵循示例 `CommandTeleport` 插件来查看如何向用户公开服务器列表和传输。

### 8.4 测试策略
- 在无头测试中实例化 `ServerContext` 以在不运行完整启动器的情况下验证对 USP 的迁移。
- 使用事件提供程序来隔离模拟玩家加入、数据包流或协调器切换。
- 根据仓库指南创建 xUnit 项目后，考虑在 `tests/` 下构建集成测试工具。
