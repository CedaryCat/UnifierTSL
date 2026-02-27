# 插件开发和迁移指南

这份指南讲的是怎么给 UnifierTSL 写插件、怎么用好运行时提供的能力，以及怎么把现有的 TShock 或 OTAPI 插件迁过来。

## 快速导航

- [重要实践建议](#important-best-practices)
- [1. 入门](#1-getting-started)
  - [1.1 NuGet 快速入门（推荐）](#11-nuget-quickstart-recommended)
  - [1.2 源码方式开发](#12-working-from-source)
- [发布器输出行为](#publisher-output-behavior)
- [2. 插件生命周期与宿主管理](#2-plugin-lifecycle--hosting)
  - [2.1 `IPlugin` 约定](#21-iplugin-contract)
  - [2.2 初始化排序](#22-initialization-ordering)
  - [2.3 自定义宿主准入规则](#23-custom-host-admission-rules)
- [3. 配置管理](#3-configuration-management)
  - [3.1 注册配置](#31-registering-configs)
  - [3.2 错误处理和重新加载](#32-error-handling--reloads)
- [4. 事件与 Hook 集成](#4-event--hook-integration)
  - [4.1 使用 `UnifierApi.EventHub`](#41-using-unifierapieventhub)
  - [4.2 桥接到 MonoMod Hooks](#42-bridging-to-monomod-hooks)
  - [4.3 公共事件域](#43-common-event-domains)
- [5. 网络与数据交换](#5-networking--data-exchange)
  - [5.1 发送数据包](#51-sending-packets)
  - [5.2 接收和修改数据包](#52-receiving--modifying-packets)
- [5.3 服间转移与协调器辅助](#53-transfers--coordinator-helpers)
- [6. 日志与诊断](#6-logging--diagnostics)
  - [6.1 角色记录器](#61-role-loggers)
  - [6.2 诊断挂钩](#62-diagnostics-hooks)
- [7. 迁移旧版插件和框架](#7-migrating-legacy-plugins--frameworks)
  - [7.1 围绕 USP 来思考](#71-orienting-around-usp)
  - [7.2 迁移核心功能](#72-translating-core-features)
  - [7.3 案例研究：TShock 模块](#73-case-study-tshock-modules)
- [7.4 需要留意的事项](#74-known-pitfalls)
- [8. 进阶技巧](#8-advanced-techniques)
  - [8.1 自定义 Detour](#81-custom-detours)
  - [8.2 发布附加模块](#82-shipping-additional-modules)
  - [8.3 多服务器协调](#83-multi-server-coordination)
  - [8.4 测试策略](#84-testing-strategies)

<a id="important-best-practices"></a>
## 重要实践建议

- **始终从 `ServerContext` 派生上下文。** UnifierTSL 假定每个活动根上下文都是 `ServerContext`（或其子类），这样 USP 的 detour 才能安全调用 `ToServer(this RootContext)`。如果你创建了不继承 `ServerContext` 的自定义 `RootContext`，运行时会直接抛异常；因此请在 detour 和辅助逻辑里统一通过 `ToServer()` 取服务器上下文，确保生命周期管理一致。
- **使用 `SampleServer` 作为一次性上下文。** 当你只需要一个“类型满足要求”的静态示例上下文（例如 `Item.SetDefaults(RootContext ctx, int itemType)`）时，请实例化 `SampleServer` 或其子类。它会改写控制台接入逻辑，不会额外弹出控制台窗口，同时仍满足 `ServerContext` 继承约束。
- **通过调用点传递上下文，而不是缓存上下文。** 优先将相关上下文作为方法参数传递。只有当持有者本身是通过 `ServerContext.RegisterExtension()` 注册并随服务器上下文托管的扩展实例时，才建议以字段/属性长期保存 `ServerContext` 引用。其他位置长期缓存上下文会阻止正常回收，可能把整台服务器实例常驻内存并导致泄漏。
- **在重复造轮子之前先使用平台服务。** 通过 `IPluginConfigRegistrar` 构建配置，使用 `UnifierApi.CreateLogger` 创建基于角色的记录器，并通过 `EventHub` 提供程序公开可重用的挂钩。这使插件与协调器生命周期规则、结构化日志记录和共享事件界面保持一致。

<a id="1-getting-started"></a>
## 1. 入门

大多数插件作者可以通过引用每个版本附带的已发布的 `UnifierTSL` NuGet 包来保持在 UnifierTSL 存储库之外。仅当你需要调试运行时或贡献运行时更改时，才需要直接针对源代码树进行工作。

<a id="11-nuget-quickstart-recommended"></a>
### 1.1 NuGet 快速入门（推荐）

**场景：** 构建 `WelcomePlugin`，一个在聊天中回复 `!hello` 并显示绿色欢迎消息的模块。

1. 创建 .NET 9 类库并启用可空/隐式使用以匹配运行时风格。删除脚手架后生成的`Class1.cs`文件。

   ```
   dotnet new classlib -n WelcomePlugin -f net9.0
   ```

   更新 `WelcomePlugin.csproj`，使 `<PropertyGroup>` 包括：

   ```xml
   <ImplicitUsings>enable</ImplicitUsings>
   <Nullable>enable</Nullable>
   ```

2. 引用运行时包。将版本与你要定位的 UnifierTSL 版本相匹配（启动器在启动时打印版本横幅，并且 `UnifierTSL.runtimeconfig.json` 列出相同的值）：

   ```
   dotnet add package UnifierTSL --version <runtime-version>
   ```

<details>
<summary><strong>展开实现步骤 3-5</strong></summary>

3. 添加你的插件入口点。从最小可运行骨架开始，再逐步补齐行为。

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

   `[assembly: CoreModule]` 将程序集标记为核心模块（有关运行时概述，请参阅[README 插件系统](../README.md#plugin-system)）。此指定允许其他程序集使用 `[RequiresCoreModule("CoreModuleName")]` 将自己声明为“卫星模块”，它将加载到相同的 `AssemblyLoadContext` 中并共享依赖项。 `[PluginMetadata]` 用于声明插件标识，该标识会出现在加载顺序、日志条目和发布器（Publisher）输出中。加载器根据这些属性而不是它们的初始化行为来发现和组织模块。

   _在初始化阶段注册事件并记录就绪状态_

   将以下成员添加到 `Plugin` 类（保留骨架中的特性）并确保文件导入 `UnifierTSL`、`UnifierTSL.Events.Handlers` 和 `UnifierTSL.Logging`：

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

   _添加一个卸载钩子以便清理注册_

   将此覆盖放在同一个类中。它使用 `BasePlugin` 处置钩子 (`DisposeAsync(bool isDisposing)`) 来撤消订阅：

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

   将此方法添加到类中并导入 `System`、`Microsoft.Xna.Framework` 和 `UnifierTSL.Events.Core`：

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

4. 构建插件并将其复制到你的 UnifierTSL 安装中：

   ```
   dotnet build WelcomePlugin/WelcomePlugin.csproj -c Debug
   mkdir -p <unifier-install>/plugins/WelcomePlugin
   cp WelcomePlugin/bin/Debug/net9.0/WelcomePlugin.dll <unifier-install>/plugins/WelcomePlugin/
   ```

   将任何附加程序集（附属程序或依赖项）与主 DLL 一起保留在插件文件夹内。

5. 启动 UnifierTSL 运行时（从版本下载或现有部署）。启动期间控制台日志将报告 `WelcomePlugin`。现在，加入服务器并发送 `!hello` 会将绿色欢迎消息打印回玩家，演示属性连接、事件注册和日志记录。

</details>

<a id="12-working-from-source"></a>
### 1.2 源码方式开发

如果你需要完整的调试链路，或希望自行打包运行时：克隆存储库并遵循 [`Run from Source`](../README.md#quick-start) 检查表，然后在 `src/Plugins/` 下创建或复制插件项目。

- 通过复制 `src/Plugins/ExamplePlugin` 作为模板来搭建插件支架，或者创建一个新的类库：
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

- 将项目添加到解决方案中，以便它与其他所有内容一起构建：

  ```
  dotnet sln src/UnifierTSL.slnx add src/Plugins/WelcomePlugin/WelcomePlugin.csproj
  ```

- 使用相同的事件注册、日志记录和生命周期模式，完全按照上面的 NuGet 快速入门指南（示例的第 3-5 节）中所示实现插件逻辑。

- 构建和发布：使用 **发布器** 生成集成了所有插件的适当可分发包。将 `win-x64` 替换为你需要的运行时标识符（例如 `linux-x64`、`osx-arm64`）：
  ```
  dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64
  ```
  这是测试插件的**唯一可靠的方法**，因为它确保正确的文件布局（`plugins/`、`lib/`、`config/` 目录）和与生产部署相匹配的依赖项解析。直接 `dotnet build` 或 `dotnet run` 命令不会触发插件编译或保证 UnifierTSL 以有效的运行时结构启动。

- 要生成包含你的插件的可分发包，适用相同的发布器命令。输出可直接部署到生产系统。


<a id="publisher-output-behavior"></a>
### 发布器输出行为

<details>
<summary><strong>展开发布器输出模式细节</strong></summary>

发布器有两种不同的输出模式，由 `--output-path` 参数控制：

**默认行为（未指定 `--output-path`）：**
- 输出目录：`src/UnifierTSL.Publisher/bin/<Configuration>/<TFM>/utsl-<rid>/`
- `<Configuration>` 遵循发布器本身的构建方式（`dotnet run` 默认为 `Debug`，除非传递 `-c Release`）。
- 此默认值使用 `UnifierTSL.Publisher` 项目自己的构建文件夹，该文件夹保持与存储库结构的兼容性。
- 发布器通过搜索最多 5 个目录来查找 `.sln` 或 `.slnx` 文件，自动定位解决方案根目录，因此无论你是通过 `dotnet run` 还是从编译的二进制文件调用它，它都能正常工作。

**自定义输出位置（指定 `--output-path`）：**
- 使用 `--output-path` 指定任何其他目录：
  ```
  dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64 --output-path ./bin
  ```
- `--output-path` 参数接受绝对路径和相对路径：
  - **绝对路径**按原样使用。
  - **相对路径** 相对于你调用发布器的当前工作目录（而不是解决方案根目录）进行解析。

在这两种情况下，发布器都会写入 `<output-path>/utsl-<rid>/`，将每个插件从 `src/Plugins/` 复制到已发布应用程序的 `plugins/` 文件夹中。

**重新运行时保留现有输出：**
默认情况下，发布器在写入之前清理输出目录。如果你重新运行发布器以更新现有部署并希望保留其他文件（例如生成的配置、保存的世界数据），请附加 `--clean-output-dir false`：
```
dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64 --output-path ./bin --clean-output-dir false
```
如果没有此标志，输出文件夹将被删除并重新创建，这对于干净构建很有用，但在更新实时部署时具有破坏性。

**排除特定插件：**
可以选择附加 `--excluded-plugins` 以从捆绑包中省略特定插件：
```
dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64 --excluded-plugins ExamplePlugin
```

**跳过 RID 子文件夹：**
为了方便开发，你可以附加 `--use-rid-folder false` 直接写入输出文件夹，而无需 `utsl-<rid>/` 子文件夹，这对于在单个目标平台上进行迭代非常有用：

```
dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64 --output-path ./bin --use-rid-folder false
```
这将写入 `./bin/plugins/` 而不是 `./bin/utsl-win-x64/plugins/`。

- 推荐：添加构建后步骤，将插件直接复制到发布器输出中，以便增量插件改动无需重新运行发布器。这可以加快开发迭代：

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

  如果你已使用 `--use-rid-folder false` 配置发布器，请相应调整 `PublishFolder` 路径：

  ```xml
  <PublishFolder>$(PublishOutputFolder)/plugins</PublishFolder>
  ```

此工作流程可让你在快速 NuGet 驱动的迭代和全源调试之间切换，而无需重写插件。

</details>

<a id="2-plugin-lifecycle--hosting"></a>
## 2. 插件生命周期与宿主管理

<a id="21-iplugin-contract"></a>
### 2.1 `IPlugin` 约定
实现 `IPlugin` 接口 (`src/UnifierTSL/Plugins/IPlugin.cs`) 或继承 `BasePlugin` 提供默认值：

```csharp
[PluginMetadata("MyPlugin", "1.0.0", "Me", "Sample")]
public sealed class MyPlugin : BasePlugin
{
    public override int InitializationOrder => 10;

    public override void BeforeGlobalInitialize(ImmutableArray<IPluginContainer> plugins)
    {
        // 连接依赖，或注册那些要求其他插件实例已存在的事件。
    }

    public override async Task InitializeAsync(
        IPluginConfigRegistrar registrar,
        ImmutableArray<PluginInitInfo> prior,
        CancellationToken token)
    {
        // 注册配置、初始化服务、启动后台任务。
    }

    public override Task ShutdownAsync(CancellationToken token)
        => Task.CompletedTask;

    public override ValueTask DisposeAsync(bool isDisposing)
        => ValueTask.CompletedTask;
}
```

<a id="22-initialization-ordering"></a>
### 2.2 初始化排序
- 插件按 `InitializationOrder` 排序，然后按类型名排序。对于基础系统使用较低的数字，对于扩展使用较高的数字。
- `BeforeGlobalInitialize` 在所有插件实例存在之后但在 `InitializeAsync` 之前执行。使用它来获取其他插件的引用或注册共享服务。
- `InitializeAsync` 会收到一个 `ImmutableArray<PluginInitInfo>`，其中包含排在你之前的插件及其初始化 `Task`。应只等待你明确依赖的任务，而不是假定它们会按顺序完成：

```csharp
var tshockInit = prior.FirstOrDefault(p => p.Metadata.Name == "TShock");
if (tshockInit.InitializationTask is { } task)
{
    await task.ConfigureAwait(false);
}
```

<a id="23-custom-host-admission-rules"></a>
### 2.3 自定义宿主准入规则
- 要发布自定义 `Plugin Host（插件宿主）`，请实现 `IPluginHost`，提供 public 无参构造函数，并使用 `PluginHostAttribute` 标注该类。
- 运行时会按 `PluginOrchestrator.ApiVersion`（当前 `1.0.0`）校验准入：宿主主版本必须与运行时主版本一致；宿主次版本必须小于或等于运行时次版本。
- 版本检查失败的宿主会被跳过（记录警告），因此在版本对齐前，面向该宿主的插件不会加载。

<a id="3-configuration-management"></a>
## 3. 配置管理

<a id="31-registering-configs"></a>
### 3.1 注册配置
- 从传递到 `InitializeAsync` 的 `IPluginConfigRegistrar` 获取配置注册：

```csharp
var configHandle = registrar
    .CreateConfigRegistration<MyConfig>("config.json")
    .WithDefault(() => new MyConfig { Enabled = true, CooldownSeconds = 30 })
    .TriggerReloadOnExternalChange(true)
    .Complete();

MyConfig config = await configHandle.RequestAsync(cancellationToken: token);
```

- 配置文件位于插件范围的配置目录下（当前为 `config/<PluginModuleFileName>/`，源自加载的插件模块路径）。每个插件可以注册多个配置。

<a id="32-error-handling--reloads"></a>
### 3.2 错误处理和重新加载
- `OnDeserializationFailure(DeserializationFailureHandling.ReturnNewInstance, autoPersistFallback: false)` 决定是保留默认值还是抛出错误。
- `OnSerializationFailure(SerializationFailureHandling.WriteNewInstance)` 控制如何处理写入错误（例如，重写默认值而不是抛出错误）。
- 你可以在单个注册上调用 `TriggerReloadOnExternalChange(true)`，也可以先在 `configRegistrar.DefaultOption` 上设置该选项，让它作为当前插件后续注册的默认值。
- `OnChangedAsync` 让你对外部编辑做出反应。处理程序返回 `ValueTask<bool>`；返回 `true` 表示你已经处理了更改（跳过自动缓存更新）：

```csharp
configHandle.OnChangedAsync += async (sender, updatedConfig) =>
{
    // 校验并应用新配置
    return true; // true = handled, skip auto cache update
};
```

- 以编程方式写入配置时可使用 `ModifyInMemory` 或 `Overwrite`；配置句柄的文件读写路径由 `FileLockManager` 保护，以避免并发损坏。

<a id="4-event--hook-integration"></a>
## 4. 事件与 Hook 集成

<a id="41-using-unifierapieventhub"></a>
### 4.1 使用 `UnifierApi.EventHub`
- 通过特定于域的属性访问事件提供器：`UnifierApi.EventHub.Chat.ChatEvent`、`UnifierApi.EventHub.Coordinator.SwitchJoinServer`、`UnifierApi.EventHub.Game.PreUpdate` 等。
- 选择与你场景匹配的提供器类型：
  - `ValueEventProvider<T>` 用于具有取消功能的可变载荷（`Handled`、`StopPropagation`）。
  - `ReadonlyEventProvider<T>` 当你只检查数据但仍想要否决语义时。
  - `ValueEventNoCancel` 用于不可取消但允许处理器修改共享事件载荷的场景（例如注入路由/选择结果）。
  - `ReadonlyEventNoCancel` 用于纯通知场景，处理器只观察状态。
- 指定 `HandlerPriority` 在其他处理程序之前/之后运行。仅当先前的处理程序将事件标记为已处理时，才使用 `FilterEventOption.Handled` 做出反应。

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

<a id="42-bridging-to-monomod-hooks"></a>
### 4.2 桥接到 MonoMod Hooks
- 当不存在现成事件提供器时，可直接使用 MonoMod 的 `On.` detour（例如 `On.Terraria.Main.Update`）。如果你只是在原逻辑前后插入行为，应正常转发给 `orig`；仅在你明确要替换原逻辑时才跳过转发。
- 当多个插件需要同一钩子且需要更精细的跨插件执行顺序控制时，可先考虑在你自己的插件中引入事件提供器；仅当该钩子具有广泛复用价值时，再考虑向核心运行时 `src/UnifierTSL/Events/Handlers` 提交补丁。

<a id="43-common-event-domains"></a>
### 4.3 公共事件域
- **协调员** – `CheckVersion`、`SwitchJoinServer`、`ServerCheckPlayerCanJoinIn`、`JoinServer`、`CreateSocket`、`PreServerTransfer`、`PostServerTransfer`、`Started`、`LastPlayerLeftEvent`。
- `CheckVersion` 当前在 `ClientHello` 期间被调用两次；保持处理程序幂等。
- **Netplay** – 检查或取消数据包交换，检测套接字重置。
- **游戏** – `GameInitialize`（在`Main.Initialize`原始逻辑之前）、`GamePostInitialize`（在`NetplaySystemContext.StartServer`之后）、`PreUpdate`、`PostUpdate`和`GameHardmodeTileUpdate`。
- `GameHardmodeTileUpdate` 是从两个硬模式挂钩（`InvokeHardmodeTilePlace` 和 `InvokeHardmodeTileUpdate`）引发的，因此一个处理程序可以覆盖放置和更新路径。
- **聊天** – 在原版处理前修改玩家或控制台聊天内容。
- **服务器** – 对服务器添加/删除、控制台服务创建做出反应。
- **启动器** – `InitializedEvent` 在启动器参数最终确定（包括交互式提示）之后和协调器启动之前触发。

<a id="5-networking--data-exchange"></a>
## 5. 网络与数据交换

<a id="51-sending-packets"></a>
### 5.1 发送数据包
- `PacketSender.SendFixedPacket` 针对具有可预测大小的非托管数据包； `SendDynamicPacket` 处理分配自己缓冲区的托管 `IManagedPacket` 载荷。
- `_S` 变体（例如 `SendFixedPacket_S`）在分派之前切换 `ISideSpecific.IsServerSide` 标志。
- 通过 `UnifiedServerCoordinator.clientSenders[clientId]` 获取 `LocalClientSender`。当它已提供高级辅助方法（如 `Kick`）时，优先使用这些方法，以确保协调器状态记录保持同步。

```csharp
using Terraria.Localization;

var sender = UnifiedServerCoordinator.clientSenders[clientId];

// 示例：通知客户端移除其拥有的弹幕（固定长度数据包）。
var killProjectile = new TrProtocol.NetPackets.KillProjectile(
    (short)projectileIndex,
    (byte)clientId);
sender.SendFixedPacket(in killProjectile);

// 需要踢出客户端？使用辅助方法以正确更新终止标记。
sender.Kick(NetworkText.FromLiteral("Maintenance window"));
```

<a id="52-receiving--modifying-packets"></a>
### 5.2 接收和修改数据包
- 通过 `NetPacketHandler.Register<TPacket>` 注册处理程序以在泰拉瑞亚的 `NetMessage.GetData` 处理数据包之前拦截数据包：

```csharp
NetPacketHandler.Register<TrProtocol.NetPackets.TileChange>(
    (ref ReceivePacketEvent<TrProtocol.NetPackets.TileChange> args) =>
    {
        if (IsProtectedRegion(args.Packet.X, args.Packet.Y))
        {
            args.HandleMode = PacketHandleMode.Cancel;
        }
    });
```

- 设置`HandleMode = PacketHandleMode.Overwrite`并修改`args.Packet`以重写数据包；处理程序将重新序列化并通过 `ClientPacketReceiver` 分派它。
- 使用 `NetPacketHandler.ProcessPacketEvent` 检查原始数据包字节 (`ProcessPacketEvent.RawData`) 进行诊断。

<a id="53-transfers--coordinator-helpers"></a>
### 5.3 服间转移与协调器辅助
- `UnifiedServerCoordinator.TransferPlayerToServer` 用于跨服务器转移客户端。建议用 try/catch 包裹调用，并通过 `PreServerTransferEvent` 正确处理可取消流程。
- `ServerContext.SyncPlayerJoinToOthers` 等方法可在自定义转移流程后校正可见性，但它们属于底层原语。除非你明确需要定制时序/行为，否则优先使用已封装的 `TransferPlayerToServer`。
- 查询 `UnifiedServerCoordinator.Servers` 以获得活动上下文；每个上下文都直接继承 `RootContext` 并公开 `Name` 以及注册的扩展。
- 启动器端路由选项映射到协调器事件：
  - `-joinserver random|rnd|r` 注册一个低优先级加服策略处理器，用于随机选择目标服务器。
  - `-joinserver first|f` 注册一个低优先级加服策略处理器，用于选择首个可用服务器。
  - 当前进程中第一个有效的 `-joinserver` 策略会优先生效。
- 自动启动选项（`-server`、`-addserver`、`-autostart`）会在启动参数解析阶段（`UnifiedServerCoordinator.Launch(...)` 之前）处理；每个有效条目都会立即创建并加入 `ServerContext`，共同构成初始服务器集合。
- 语言覆盖优先级在启动时固定：`UTSL_LANGUAGE` 在 CLI 解析之前应用，并在接受后抑制后面的 `-lang`/`-culture`/`-language` 覆盖。

<a id="6-logging--diagnostics"></a>
## 6. 日志与诊断

<a id="61-role-loggers"></a>
### 6.1 角色记录器
- 实现 `ILoggerHost`（例如，提供 `Name`，可选 `CurrentLogCategory`）并调用 `UnifierApi.CreateLogger(this)` 来获取 `RoleLogger`。
- 利用 `src/UnifierTSL/Logging/LoggerExt.cs` 中的扩展方法进行特定于严重性的日志记录（`Debug`、`Info`、`Warning`、`Error`、`Success`、`LogHandledException`）。
- 可通过实现 `ILogMetadataInjector` 或传入 span 来注入额外元数据：

```csharp
log.Info("Teleporting player", metadata: stackalloc[]
{
    new KeyValueMetadata("Player", player.Name),
    new KeyValueMetadata("TargetServer", target.Name),
});
```

<a id="62-diagnostics-hooks"></a>
### 6.2 诊断挂钩
- 订阅 `PacketSender.SentPacket` 以进行流量审计。
- 事件提供者公开 `HandlerCount` 用于工具；迭代 `EventProvider.AllEvents` 以显示运行时仪表板。
- 使用结构化元数据（例如，传送目标、配置名称）来简化外部接收器中的日志过滤。

<a id="7-migrating-legacy-plugins--frameworks"></a>
## 7. 迁移旧版插件和框架

<a id="71-orienting-around-usp"></a>
### 7.1 围绕 USP 来思考
- **上下文优先的心态** – USP 用每服务器上下文替换了泰拉瑞亚静态。审核 `Main`、`Netplay`、`NetMessage` 等的每个旧用法，并将它们迁移到 `ServerContext` 对应项（`ctx.Main`、`ctx.Netplay`、`ctx.Router`）。 [USP dev-guide](https://github.com/CedaryCat/OTAPI.UnifiedServerProcess/blob/main/docs/Developer-Guide.md#core-concepts) 中的表列出了最常见的映射。
- **根上下文创建** – 许多传统启动器假设有一个世界。移植初始化逻辑时，构造或解析 UnifierTSL 提供的 `RootContext`/`ServerContext` 而不是实例化你自己的。使用协调器助手 (`UnifiedServerCoordinator.Servers`) 枚举活动上下文。
- **钩子接口** – 将 `TerrariaApi.Server` 或 OTAPI 静态钩子替换为 `EventHub` 提供程序或上下文绑定的 MonoMod 钩子。如果你需要一个尚不存在的挂钩，请在插件中公开一个新的提供程序，以便其他迁移可以共享它。

<a id="72-translating-core-features"></a>
### 7.2 迁移核心功能
- **命令和权限** – 旧版 TShock 命令可以存在于 UnifierTSL 插件中。将它们注册到 `InitializeAsync` 中，并通过构造函数注入或 `PluginHost` 解决依赖关系。现有的 `CommandTeleport` 插件显示了命令元数据与执行逻辑的完整迁移示例。
- **配置** – 将原始文件 IO 迁移到 `IPluginConfigRegistrar`。这会将配置保留在宿主分配的插件配置目录（当前为 `config/<PluginModuleFileName>/`）下，并启用热重载、回退处理和模式验证。
- **网络** – 旧有静态数据包 detour 应改写为 `NetPacketHandler.Register<TPacket>` 处理程序。序列化建议使用 TrProtocol 数据包类型；它们会自动遵守 USP 的上下文规则并执行长度边界检查。
- **世界和图块访问** – 将 `Tile` 或 `ITile` 操作替换为 USP 的 `TileCollection`、`TileData` 和 `RefTileData`。迁移步骤参见[USP dev-guide](https://github.com/CedaryCat/OTAPI.UnifiedServerProcess/blob/main/docs/Developer-Guide.md#world-data-tileprovider)。

<a id="73-case-study-tshock-modules"></a>
### 7.3 案例研究：TShock 模块
- 内置的 `src/Plugins/TShockAPI` 插件演示了成熟生态如何与 UnifierTSL 集成。迁移此前直接扩展 TShock 的插件时，可参考其做法。
- TShock 的权限检查仍由其自身的组/权限体系负责；日志则通过 UnifierTSL 的 `RoleLogger` 输出，从而复用统一的分类过滤与结构化元数据链路。
- 依赖于 TShock 自定义序列化器的数据包级功能依赖于迁移实现中的 TrProtocol 模型。这避免了手动缓冲区算术并保持与 USP 的 IL 合并数据包定义的兼容性。

<a id="74-known-pitfalls"></a>
### 7.4 需要留意的事项
- 在 USP 中，只有那些已被上下文化（contextified）的 detour 才会在当前逻辑管道中拿到上下文参数。该参数可用时应尽量直接使用，而不是重新查询 `UnifiedServerCoordinator.GetClientCurrentlyServer(plr)`；只有在钩子确实没有上下文且无更可靠来源时，才考虑使用该查询。
- `ILengthAware`/`IExtraData` 在当前文脉下主要用于表达数据包形态并作为泛型约束/分发路径标记，而不是“是否必须传结束指针”的判据。出于边界安全考虑，当前 TrProtocol 反序列化应统一传入结束指针（或等效边界信息）。
- 事件处理程序中的阻塞工作会拖慢协调器。并且 EventHub 事件参数是 `ref struct`，不能跨异步边界捕获；如需异步处理，请先提取所需字段，再把长任务分派到后台（`Task.Run`、专用队列等）。
- 面向单服 OTAPI/Terraria 模型编写的旧插件常见进程级静态单例（例如按玩家索引的静态缓存、静态世界图格备份/快照缓冲）。在 USP 多实例架构下，应改写为每个上下文独立服务或由协调器托管的字典，避免服间状态泄漏。

<a id="8-advanced-techniques"></a>
## 8. 进阶技巧

<a id="81-custom-detours"></a>
### 8.1 自定义 Detour
- 使用 `MonoMod.RuntimeDetour` 应用所提供事件之外的挂钩。确保在 `ShutdownAsync` 或 `DisposeAsync` 期间释放 detour，避免插件卸载后残留过期补丁。
- 将 detours 与新的 `EventHub` 提供程序结合起来，为其他插件公开可重用的挂钩。

<a id="82-shipping-additional-modules"></a>
### 8.2 发布附加模块
- 通过使用 `[ModuleDependencies]` 创建核心模块来打包共享服务或本机库。实现 `IDependencyProvider` 将载荷提取到 `plugins/<Module>/lib/` 中。
- 卫星可以携带可选功能、命令或数据迁移，而不会导致核心插件 DLL 膨胀。
- `ModuleAssemblyLoader.PreloadModules(...)` 会按 `dll.Name` 对发现的 DLL 去重。如果多个文件同名，则最后被索引的文件会优先生效（根目录 `plugins/*.dll` 在子目录之后索引）。
- 本机加载行为是清单驱动的：`ModuleLoadContext.LoadUnmanagedDll` 读取 `dependencies.json` 并按清单文件名（包括版本后缀名称，如 `sqlite3.1.0.0.dll`）进行解析。
- RID 回退通常发生在更早的依赖提取阶段（NuGet/嵌入式依赖解析，如 `GetNativeLibsPathsAsync` 与 `NativeEmbeddedDependency`）；`LoadUnmanagedDll` 本身不做按 RID 文件夹的运行时探测循环。

<a id="83-multi-server-coordination"></a>
### 8.3 多服务器协调
- 通过在 `UnifiedServerCoordinator` 上注册的专用服务或通过特定于插件的扩展字典来维护共享状态。
- 通过将协调器事件（`SwitchJoinServer`、`ServerListChanged`）与数据包发送 API 相结合，在服务器之间桥接聊天或游戏事件。
- 按照示例 `CommandTeleport` 插件查看服务器列表和传输如何向用户公开。

<a id="84-testing-strategies"></a>
### 8.4 测试策略
- 在无头测试中实例化 `ServerContext`，以验证针对 USP 的迁移，而无需运行完整的启动器。
- 使用事件提供程序来模拟玩家加入、数据包流或隔离的协调器切换。
- 一旦根据存储库指南创建了 xUnit 项目，请考虑在 `tests/` 下构建集成测试工具。



