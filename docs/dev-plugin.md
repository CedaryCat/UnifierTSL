# Plugin Development & Migration Guide

This guide explains how to build plugins for UnifierTSL, how to take advantage of its runtime services, and how to migrate existing TShock or OTAPI-based plugins.

## Quick Navigation

- [Important Best Practices](#important-best-practices)
- [1. Getting Started](#1-getting-started)
  - [1.1 NuGet Quickstart (Recommended)](#11-nuget-quickstart-recommended)
  - [1.2 Working from Source](#12-working-from-source)
  - [Publisher Output Behavior](#publisher-output-behavior)
- [2. Plugin Lifecycle & Hosting](#2-plugin-lifecycle--hosting)
  - [2.1 `IPlugin` Contract](#21-iplugin-contract)
  - [2.2 Initialization Ordering](#22-initialization-ordering)
  - [2.3 Custom Host Admission Rules](#23-custom-host-admission-rules)
- [3. Configuration Management](#3-configuration-management)
  - [3.1 Registering Configs](#31-registering-configs)
  - [3.2 Error Handling & Reloads](#32-error-handling--reloads)
- [4. Event & Hook Integration](#4-event--hook-integration)
  - [4.1 Using `UnifierApi.EventHub`](#41-using-unifierapieventhub)
  - [4.2 Bridging to MonoMod Hooks](#42-bridging-to-monomod-hooks)
  - [4.3 Common Event Domains](#43-common-event-domains)
- [5. Networking & Data Exchange](#5-networking--data-exchange)
  - [5.1 Sending Packets](#51-sending-packets)
  - [5.2 Receiving & Modifying Packets](#52-receiving--modifying-packets)
  - [5.3 Transfers & Coordinator Helpers](#53-transfers--coordinator-helpers)
- [6. Logging & Diagnostics](#6-logging--diagnostics)
  - [6.1 Role Loggers](#61-role-loggers)
  - [6.2 Diagnostics Hooks](#62-diagnostics-hooks)
- [7. Migrating Legacy Plugins & Frameworks](#7-migrating-legacy-plugins--frameworks)
  - [7.1 Orienting Around USP](#71-orienting-around-usp)
  - [7.2 Translating Core Features](#72-translating-core-features)
  - [7.3 Case Study: TShock Modules](#73-case-study-tshock-modules)
  - [7.4 Migration Watchouts](#74-migration-watchouts)
- [8. Advanced Techniques](#8-advanced-techniques)
  - [8.1 Custom Detours](#81-custom-detours)
  - [8.2 Shipping Additional Modules](#82-shipping-additional-modules)
  - [8.3 Multi-Server Coordination](#83-multi-server-coordination)
  - [8.4 Testing Strategies](#84-testing-strategies)

## Important Best Practices

- **Always derive contexts from `ServerContext`.** UnifierTSL assumes every active root context is a `ServerContext` (or subclass) so that USP detours can safely call `ToServer(this RootContext)`. Creating a custom `RootContext` that does not inherit from `ServerContext` will throw at runtime; keep detours and helpers using `ToServer()` so they align with the framework’s lifetime management.
- **Use `SampleServer` for throwaway contexts.** When you need a static sample context for API calls that only care about the type (e.g., `Item.SetDefaults(RootContext ctx, int itemType)`), instantiate `SampleServer` or a subclass. It overrides console wiring, so it will not pop extra console windows while still satisfying the `ServerContext` inheritance requirement.
- **Pass contexts through call sites instead of caching them.** Prefer passing the relevant context as a method parameter. Keep long-lived references only on owners that are themselves extension instances created through `ServerContext.RegisterExtension()`, so the owner lifetime is tied to the same server context. Caching contexts elsewhere can keep whole server instances alive and cause memory leaks.
- **Lean on platform services before rolling your own.** Build configuration via `IPluginConfigRegistrar`, create role-based loggers with `UnifierApi.CreateLogger`, and expose reusable hooks through `EventHub` providers. This keeps plugins aligned with coordinator lifecycle rules, structured logging, and shared event surfaces.

## 1. Getting Started

Most plugin authors can stay outside the UnifierTSL repository by referencing the published `UnifierTSL` NuGet package that ships with each release. Working directly against the source tree is only necessary when you need to debug the runtime or contribute runtime changes.

### 1.1 NuGet Quickstart (Recommended)

**Scenario:** build `WelcomePlugin`, a module that replies to `!hello` in chat with a green welcome message.

1. Create a .NET 9 class library and enable nullable/implicit usings to match the runtime style. Delete the generated `Class1.cs` file after scaffolding.

   ```
   dotnet new classlib -n WelcomePlugin -f net9.0
   ```

   Update `WelcomePlugin.csproj` so the `<PropertyGroup>` includes:

   ```xml
   <ImplicitUsings>enable</ImplicitUsings>
   <Nullable>enable</Nullable>
   ```

2. Reference the runtime package. Match the version to the UnifierTSL release you are targeting (the launcher prints the version banner on startup, and `UnifierTSL.runtimeconfig.json` lists the same value):

   ```
   dotnet add package UnifierTSL --version <runtime-version>
   ```

<details>
<summary><strong>Expand implementation steps 3-5</strong></summary>

3. Add your plugin entry point. Start with the smallest viable stub, then layer in behavior.

   _Minimal skeleton_

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

   `[assembly: CoreModule]` marks the assembly as a core module (see [README plugin system](../README.md#plugin-system) for a runtime overview). This designation allows other assemblies to declare themselves as "satellite modules" using `[RequiresCoreModule("CoreModuleName")]`, which will load in the same `AssemblyLoadContext` and share dependencies. `[PluginMetadata]` publishes the plugin identity that surfaces in load order, log entries, and publisher output. The loader discovers and organizes modules based on these attributes, not their initialization behavior.

   _Subscribe during initialization and log readiness_

   Add the following members to the `Plugin` class (keeping the attributes from the stub) and ensure the file imports `UnifierTSL`, `UnifierTSL.Events.Handlers`, and `UnifierTSL.Logging`:

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

   _Add an unload hook so you clean up registrations_

   Place this override inside the same class. It uses the `BasePlugin` disposal hook (`DisposeAsync(bool isDisposing)`) to undo the subscription:

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

   _Handle the chat callback you registered_

   Add this method to the class and import `System`, `Microsoft.Xna.Framework`, and `UnifierTSL.Events.Core`:

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
   <summary>Completed example</summary>

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

4. Build and copy the plugin into your UnifierTSL install:

   ```
   dotnet build WelcomePlugin/WelcomePlugin.csproj -c Debug
   mkdir -p <unifier-install>/plugins/WelcomePlugin
   cp WelcomePlugin/bin/Debug/net9.0/WelcomePlugin.dll <unifier-install>/plugins/WelcomePlugin/
   ```

   Keep any additional assemblies (satellites or dependencies) alongside the main DLL inside the plugin folder.

5. Launch the UnifierTSL runtime (from the release download or your existing deployment). The console log will report `WelcomePlugin` during startup. Joining the server and sending `!hello` now prints the green welcome message back to the player, demonstrating attribute wiring, event registration, and logging.

</details>

### 1.2 Working from Source

Need the full debugging story or want to bundle the runtime yourself? Clone the repository and follow the [`Run from Source`](../README.md#quick-start) checklist, then create or duplicate a plugin project under `src/Plugins/`.

- Scaffold the plugin by copying `src/Plugins/ExamplePlugin` as a template, or create a new class library:
  ```
  dotnet new classlib -n WelcomePlugin -f net9.0
  ```
  inside `src/Plugins/`, then update the `.csproj` to enable `<ImplicitUsings>enable</ImplicitUsings>` and `<Nullable>enable</Nullable>`.
- Reference the runtime project instead of the NuGet package:

  ```xml
  <ItemGroup>
    <ProjectReference Include="..\..\UnifierTSL\UnifierTSL.csproj" />
  </ItemGroup>
  ```

- Add the project to the solution so it builds with everything else:

  ```
  dotnet sln src/UnifierTSL.slnx add src/Plugins/WelcomePlugin/WelcomePlugin.csproj
  ```

- Implement the plugin logic exactly as shown in the NuGet quickstart guide above (sections 3–5 of the example), using the same event registration, logging, and lifecycle patterns.

- Build and publish: Use the **Publisher** to generate a proper distributable bundle with all plugins integrated. Replace `win-x64` with the runtime identifier you need (e.g., `linux-x64`, `osx-arm64`):
  ```
  dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64
  ```
  This is the **only reliable way** to test plugins, as it ensures the correct file layout (`plugins/`, `lib/`, `config/` directories) and dependency resolution that matches production deployments. Direct `dotnet build` or `dotnet run` commands do not trigger plugin compilation or guarantee UnifierTSL launches with a valid runtime structure.

- To produce a distributable bundle that includes your plugin, the same Publisher command applies. The output will be ready for deployment to production systems.


### Publisher Output Behavior

<details>
<summary><strong>Expand Publisher output mode deep dive</strong></summary>

The publisher has two distinct output modes controlled by the `--output-path` argument:

**Default behavior (no `--output-path` specified):**
- Output directory: `src/UnifierTSL.Publisher/bin/<Configuration>/<TFM>/utsl-<rid>/`
- `<Configuration>` follows how the publisher itself is built (`dotnet run` defaults to `Debug` unless `-c Release` is passed).
- This default uses the Publisher project's own build folder, which maintains compatibility with the repository structure.
- The publisher automatically locates the solution root by searching up to 5 directories for `.sln` or `.slnx` files, so this works correctly whether you invoke it via `dotnet run` or from a compiled binary.

**Custom output location (with `--output-path` specified):**
- Use `--output-path` to specify any other directory:
  ```
  dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64 --output-path ./bin
  ```
- The `--output-path` argument accepts both absolute and relative paths:
  - **Absolute paths** are used as-is.
  - **Relative paths** are resolved relative to the current working directory from which you invoke the publisher (not the solution root).

In both cases, the publisher writes to `<output-path>/utsl-<rid>/`, copying every plugin from `src/Plugins/` into the published application's `plugins/` folder.

**Preserving existing output on re-runs:**
By default, the publisher cleans the output directory before writing. If you are re-running the publisher to update an existing deployment and want to preserve other files (e.g., generated configurations, saved world data), append `--clean-output-dir false`:
```
dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64 --output-path ./bin --clean-output-dir false
```
Without this flag, the output folder is deleted and recreated, which is useful for clean builds but destructive when updating a live deployment.

**Excluding specific plugins:**
Optionally append `--excluded-plugins` to omit specific plugins from the bundle:
```
dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64 --excluded-plugins ExamplePlugin
```

**Skipping the RID subfolder:**
For development convenience, you can append `--use-rid-folder false` to write directly to your output folder without the `utsl-<rid>/` subfolder, which is useful for iterating on a single target platform:

```
dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- --rid win-x64 --output-path ./bin --use-rid-folder false
```
This writes to `./bin/plugins/` instead of `./bin/utsl-win-x64/plugins/`.

- Recommended: add a post-build step to copy your plugin directly into the publisher output so incremental plugin changes do not require rerunning the publisher. This keeps your development cycle fast:

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

  If you've configured the publisher with `--use-rid-folder false`, adjust the `PublishFolder` path accordingly:

  ```xml
  <PublishFolder>$(PublishOutputFolder)/plugins</PublishFolder>
  ```

This workflow lets you toggle between quick NuGet-driven iterations and full-source debugging without rewriting your plugin.

</details>

## 2. Plugin Lifecycle & Hosting

### 2.1 `IPlugin` Contract
Implement the `IPlugin` interface (`src/UnifierTSL/Plugins/IPlugin.cs`) or inherit from `BasePlugin` which provides defaults:

```csharp
[PluginMetadata("MyPlugin", "1.0.0", "Me", "Sample")]
public sealed class MyPlugin : BasePlugin
{
    public override int InitializationOrder => 10;

    public override void BeforeGlobalInitialize(ImmutableArray<IPluginContainer> plugins)
    {
        // Wire dependencies or register events that require other plugins to exist.
    }

    public override async Task InitializeAsync(
        IPluginConfigRegistrar registrar,
        ImmutableArray<PluginInitInfo> prior,
        CancellationToken token)
    {
        // Register configuration, set up services, start background tasks.
    }

    public override Task ShutdownAsync(CancellationToken token)
        => Task.CompletedTask;

    public override ValueTask DisposeAsync(bool isDisposing)
        => ValueTask.CompletedTask;
}
```

### 2.2 Initialization Ordering
- Plugins are sorted by `InitializationOrder` then type name. Use low numbers for foundational systems, higher numbers for extensions.
- `BeforeGlobalInitialize` executes after all plugin instances exist but before `InitializeAsync`. Use it to grab references from other plugins or register shared services.
- `InitializeAsync` receives an `ImmutableArray<PluginInitInfo>` for plugins scheduled before yours, including each plugin container and its initialization `Task`. Await only the dependencies you actually need instead of assuming serial completion order:

```csharp
var tshockInit = prior.FirstOrDefault(p => p.Metadata.Name == "TShock");
if (tshockInit.InitializationTask is { } task)
{
    await task.ConfigureAwait(false);
}
```

### 2.3 Custom Host Admission Rules
- To ship a custom plugin host, implement `IPluginHost`, provide a public parameterless constructor, and annotate the class with `PluginHostAttribute`.
- Runtime admission is checked against `PluginOrchestrator.ApiVersion` (currently `1.0.0`): host major version must equal runtime major version; host minor version must be less than or equal to runtime minor version.
- Hosts that fail version checks are skipped (warning logged), so plugins targeting that host will not load until versions are aligned.

## 3. Configuration Management

### 3.1 Registering Configs
- Obtain a config registration from the `IPluginConfigRegistrar` passed into `InitializeAsync`:

```csharp
var configHandle = registrar
    .CreateConfigRegistration<MyConfig>("config.json")
    .WithDefault(() => new MyConfig { Enabled = true, CooldownSeconds = 30 })
    .TriggerReloadOnExternalChange(true)
    .Complete();

MyConfig config = await configHandle.RequestAsync(cancellationToken: token);
```

- Config files live under the plugin-scoped config directory (currently `config/<PluginModuleFileName>/`, derived from the loaded plugin module path). Multiple configs can be registered per plugin.

### 3.2 Error Handling & Reloads
- `OnDeserializationFailure(DeserializationFailureHandling.ReturnNewInstance, autoPersistFallback: false)` decides whether to retain defaults or surface errors.
- `OnSerializationFailure(SerializationFailureHandling.WriteNewInstance)` controls how write errors are handled (e.g., rewrite defaults instead of throwing).
- Use `TriggerReloadOnExternalChange(true)` per registration, or set it once on `configRegistrar.DefaultOption` to apply that default to subsequent registrations in the current plugin.
- `OnChangedAsync` lets you react to external edits. Handlers return `ValueTask<bool>`; return `true` to signal that you already handled the change (skip automatic cache update):

```csharp
configHandle.OnChangedAsync += async (sender, updatedConfig) =>
{
    // Validate and apply new settings
    return true; // true = handled, skip auto cache update
};
```

- Use `ModifyInMemory` or `Overwrite` when writing configs programmatically; config handle IO paths are guarded by `FileLockManager` to prevent concurrent file corruption.

## 4. Event & Hook Integration

### 4.1 Using `UnifierApi.EventHub`
- Access providers via domain-specific properties: `UnifierApi.EventHub.Chat.ChatEvent`, `UnifierApi.EventHub.Coordinator.SwitchJoinServer`, `UnifierApi.EventHub.Game.PreUpdate`, etc.
- Choose the provider kind that matches your scenario:
  - `ValueEventProvider<T>` for mutable payloads with cancellation (`Handled`, `StopPropagation`).
  - `ReadonlyEventProvider<T>` when you only inspect data but still want veto semantics.
  - `ValueEventNoCancel` for no-cancel hooks where handlers may still mutate shared event data (for example, injecting routing or selection results).
  - `ReadonlyEventNoCancel` for pure notifications where handlers should only observe state.
- Specify `HandlerPriority` to run before/after other handlers. Use `FilterEventOption.Handled` to react only when prior handlers marked the event as handled.

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

### 4.2 Bridging to MonoMod Hooks
- When no provider exists, you can use `On.` detours provided by MonoMod (e.g., `On.Terraria.Main.Update`). Forward to `orig` when you want to preserve existing behavior and add pre/post logic; skip forwarding only when you intentionally replace the original path.
- If multiple plugins need the same hook and require consistent cross-plugin ordering, consider introducing an event provider in your plugin first. Promote it to the core runtime (`src/UnifierTSL/Events/Handlers`) only when the hook is broadly reusable.

### 4.3 Common Event Domains
- **Coordinator** – `CheckVersion`, `SwitchJoinServer`, `ServerCheckPlayerCanJoinIn`, `JoinServer`, `CreateSocket`, `PreServerTransfer`, `PostServerTransfer`, `Started`, `LastPlayerLeftEvent`.
- `CheckVersion` is currently invoked twice during `ClientHello`; keep handlers idempotent.
- **Netplay** – inspect or cancel packet exchange, detect socket resets.
- **Game** – `GameInitialize` (before `Main.Initialize` original logic), `GamePostInitialize` (after `NetplaySystemContext.StartServer`), `PreUpdate`, `PostUpdate`, and `GameHardmodeTileUpdate`.
- `GameHardmodeTileUpdate` is raised from both hardmode hooks (`InvokeHardmodeTilePlace` and `InvokeHardmodeTileUpdate`), so one handler can cover both placement and update paths.
- **Chat** – manipulate player or console chat before vanilla processing.
- **Server** – react to server add/remove, console service creation.
- **Launcher** – `InitializedEvent` fires after launcher args are finalized (including interactive prompts) and before coordinator launch.

## 5. Networking & Data Exchange

### 5.1 Sending Packets
- `PacketSender.SendFixedPacket` targets unmanaged packets with a predictable size; `SendDynamicPacket` handles managed `IManagedPacket` payloads that allocate their own buffers.
- `_S` variants (e.g., `SendFixedPacket_S`) toggle the `ISideSpecific.IsServerSide` flag before dispatching.
- Retrieve a `LocalClientSender` via `UnifiedServerCoordinator.clientSenders[clientId]`. Prefer the higher-level helpers it exposes (such as `Kick`) when they exist so coordinator bookkeeping stays in sync.

```csharp
using Terraria.Localization;

var sender = UnifiedServerCoordinator.clientSenders[clientId];

// Example: tell the client to remove a projectile it owns (fixed-size packet).
var killProjectile = new TrProtocol.NetPackets.KillProjectile(
    (short)projectileIndex,
    (byte)clientId);
sender.SendFixedPacket(in killProjectile);

// Kicking a client? Use the helper so termination flags are updated correctly.
sender.Kick(NetworkText.FromLiteral("Maintenance window"));
```

### 5.2 Receiving & Modifying Packets
- Register handlers via `NetPacketHandler.Register<TPacket>` to intercept packets before Terraria’s `NetMessage.GetData` processes them:

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

- Set `HandleMode = PacketHandleMode.Overwrite` and modify `args.Packet` to rewrite the packet; the handler will reserialize and dispatch it through `ClientPacketReceiver`.
- Use `NetPacketHandler.ProcessPacketEvent` to inspect raw packet bytes (`ProcessPacketEvent.RawData`) for diagnostics.

### 5.3 Transfers & Coordinator Helpers
- `UnifiedServerCoordinator.TransferPlayerToServer` transfers a client across servers. Wrap calls in try/catch and honour cancellation via `PreServerTransferEvent`.
- `ServerContext.SyncPlayerJoinToOthers` and related methods can realign visibility after custom transfer flows, but they are low-level primitives. Prefer `TransferPlayerToServer` unless you explicitly need custom sequencing.
- Query `UnifiedServerCoordinator.Servers` for active contexts; each context directly inherits `RootContext` and exposes `Name` plus registered extensions.
- Launcher-side routing options map into coordinator events:
  - `-joinserver random|rnd|r` registers a low-priority join-policy handler that picks a random destination.
  - `-joinserver first|f` registers a low-priority join-policy handler that picks the first available server.
  - The first valid `-joinserver` policy wins for the current process.
- Auto-start options (`-server`, `-addserver`, `-autostart`) are parsed during startup argument handling, before `UnifiedServerCoordinator.Launch(...)`. Each accepted entry immediately creates/adds a `ServerContext`, forming the initial server set.
- Language override precedence is fixed at launch time: `UTSL_LANGUAGE` is applied before CLI parsing and suppresses later `-lang`/`-culture`/`-language` overrides once accepted.

## 6. Logging & Diagnostics

### 6.1 Role Loggers
- Implement `ILoggerHost` (e.g., provide `Name`, optional `CurrentLogCategory`) and call `UnifierApi.CreateLogger(this)` to obtain a `RoleLogger`.
- Leverage extension methods from `src/UnifierTSL/Logging/LoggerExt.cs` for severity-specific logging (`Debug`, `Info`, `Warning`, `Error`, `Success`, `LogHandledException`).
- Inject additional metadata by implementing `ILogMetadataInjector` or passing spans:

```csharp
log.Info("Teleporting player", metadata: stackalloc[]
{
    new KeyValueMetadata("Player", player.Name),
    new KeyValueMetadata("TargetServer", target.Name),
});
```

### 6.2 Diagnostics Hooks
- Subscribe to `PacketSender.SentPacket` for traffic auditing.
- Event providers expose `HandlerCount` for tooling; iterate `EventProvider.AllEvents` to surface runtime dashboards.
- Use structured metadata (e.g., teleport targets, config names) to simplify log filtering in external sinks.

## 7. Migrating Legacy Plugins & Frameworks

### 7.1 Orienting Around USP
- **Context-first mindset** – USP replaces Terraria statics with per-server contexts. Audit every legacy usage of `Main`, `Netplay`, `NetMessage`, etc., and migrate them to their `ServerContext` counterparts (`ctx.Main`, `ctx.Netplay`, `ctx.Router`). The table in [USP dev-guide](https://github.com/CedaryCat/OTAPI.UnifiedServerProcess/blob/main/docs/Developer-Guide.md#core-concepts) lists the most common mappings.
- **Root context creation** – Many legacy launchers assumed a single world. When porting initialization logic, construct or resolve the `RootContext`/`ServerContext` supplied by UnifierTSL instead of instantiating your own. Use coordinator helpers (`UnifiedServerCoordinator.Servers`) to enumerate active contexts.
- **Hook surfaces** – Replace `TerrariaApi.Server` or OTAPI static hooks with `EventHub` providers or context-bound MonoMod hooks. If you need a hook that does not exist yet, expose a new provider in your plugin so other migrations can share it.

### 7.2 Translating Core Features
- **Commands & permissions** – Legacy TShock commands can live inside UnifierTSL plugins. Register them in `InitializeAsync` and resolve dependencies through constructor injection or `PluginHost`. The existing `CommandTeleport` plugin shows a clean port of command metadata and execution.
- **Configuration** – Migrate raw file IO to the `IPluginConfigRegistrar`. This keeps configs under the host-assigned plugin config directory (currently `config/<PluginModuleFileName>/`) and enables hot reload, fallback handling, and schema validation.
- **Networking** – Static packet detours become `NetPacketHandler.Register<TPacket>` handlers. Use TrProtocol packet types for serialization; they automatically honor USP’s context rules and enforce length boundaries.
- **World & tile access** – Swap `Tile` or `ITile` operations for USP’s `TileCollection`, `TileData`, and `RefTileData`. For migration steps, see [USP dev-guide](https://github.com/CedaryCat/OTAPI.UnifiedServerProcess/blob/main/docs/Developer-Guide.md#world-data-tileprovider).

### 7.3 Case Study: TShock Modules
- The built-in `src/Plugins/TShockAPI` plugin demonstrates how an established ecosystem integrates with UnifierTSL. Mirror its approach when migrating plugins that previously extended TShock directly.
- TShock permission checks remain in TShock's own group/permission system, while logging is emitted through UnifierTSL's `RoleLogger`. This gives TShock modules access to the shared category/metadata logging pipeline.
- Packet-level features that depended on TShock’s custom serializers now rely on TrProtocol models in the migrated codebase. This avoids manual buffer arithmetic and keeps compatibility with USP’s IL-merged packet definitions.

### 7.4 Migration Watchouts
- Many USP detours that were contextified already receive the current pipeline context. Prefer that parameter when available, instead of re-querying `UnifiedServerCoordinator.GetClientCurrentlyServer(plr)`. Use `GetClientCurrentlyServer` only for hooks that truly have no context parameter and no better source.
- Treat `ILengthAware`/`IExtraData` primarily as packet-shape markers used in generic constraints and dispatch paths. For current TrProtocol reads, always pass the packet end pointer (or equivalent bounds) during deserialisation for bounded parsing, regardless of packet category.
- Blocking work inside event handlers stalls the coordinator. Event args are `ref struct`, so they cannot be captured across async boundaries; copy required data first, then dispatch long-running work to background services (`Task.Run`, dedicated worker queues).
- Legacy plugins from single-server OTAPI/Terraria models often rely on process-wide statics (for example, player-index caches or static world-tile snapshot buffers). Rewrite these as per-context services or coordinator-managed dictionaries to avoid cross-server state leakage.

## 8. Advanced Techniques

### 8.1 Custom Detours
- Use `MonoMod.RuntimeDetour` to apply hooks beyond the provided events. Ensure detours are disposed during `ShutdownAsync` or `DisposeAsync` to avoid stale patching when a plugin unloads.
- Combine detours with new `EventHub` providers to expose reusable hooks for other plugins.

### 8.2 Shipping Additional Modules
- Package shared services or native libs by creating a core module with `[ModuleDependencies]`. Implement `IDependencyProvider` to extract payloads into `plugins/<Module>/lib/`.
- Satellites can carry optional features, commands, or data migrations without bloating the core plugin DLL.
- `ModuleAssemblyLoader.PreloadModules(...)` deduplicates discovered DLLs by `dll.Name`. If multiple files share the same name, the last indexed file wins (root `plugins/*.dll` entries are indexed after subdirectories).
- Native load resolution in `ModuleLoadContext.LoadUnmanagedDll` is manifest-driven: it reads `dependencies.json` and matches manifest file names (including version-suffixed names such as `sqlite3.1.0.0.dll`).
- RID fallback usually happens earlier while extracting dependency assets (NuGet and embedded dependencies). `LoadUnmanagedDll` itself does not run a RID folder probing loop at runtime.

### 8.3 Multi-Server Coordination
- Maintain shared state via dedicated services registered on `UnifiedServerCoordinator` or through plugin-specific extension dictionaries.
- Bridge chat or gameplay events across servers by combining coordinator events (`SwitchJoinServer`, `ServerListChanged`) with packet sending APIs.
- Follow the sample `CommandTeleport` plugin to see how server lists and transfers are exposed to users.

### 8.4 Testing Strategies
- Instantiate `ServerContext` in headless tests to validate migrations against USP without running the full launcher.
- Use event providers to simulate player joins, packet flows, or coordinator switches in isolation.
- Consider building an integration test harness under `tests/` once the xUnit project is created per the repository guidelines.

