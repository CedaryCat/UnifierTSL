# UnifierTSL Developer Overview

Welcome! This doc walks you through how the UnifierTSL runtime is put together on top of OTAPI Unified Server Process (USP) — the key subsystems, how they fit together, and the public APIs you can use. If you haven't checked out the [README](../README.md) or USP's [Developer-Guide.md](https://github.com/CedaryCat/OTAPI.UnifiedServerProcess/blob/main/docs/Developer-Guide.md) yet, those are good starting points.

## Quick Navigation

- [1. Runtime Architecture](#1-runtime-architecture)
  - [1.1 Layering](#11-layering)
  - [1.2 Boot Flow](#12-boot-flow)
  - [1.3 Major Components](#13-major-components)
- [2. Core Services & Subsystems](#2-core-services--subsystems)
  - [2.1 Event Hub](#21-event-hub)
  - [2.2 Module System](#22-module-system)
  - [2.3 Plugin Host Orchestration](#23-plugin-host-orchestration)
  - [2.4 Networking & Coordinator](#24-networking--coordinator)
  - [2.5 Logging Infrastructure](#25-logging-infrastructure)
  - [2.6 Configuration Service](#26-configuration-service)
- [3. USP Integration Points](#3-usp-integration-points)
- [4. Public API Surface](#4-public-api-surface)
  - [4.1 Facade (`UnifierApi`)](#41-facade-unifierapi)
  - [4.2 Event Payloads & Helpers](#42-event-payloads--helpers)
  - [4.3 Module & Plugin Types](#43-module--plugin-types)
  - [4.4 Networking APIs](#44-networking-apis)
  - [4.5 Logging & Diagnostics](#45-logging--diagnostics)
- [5. Runtime Lifecycle & Operations](#5-runtime-lifecycle--operations)
  - [5.1 Startup Sequence](#51-startup-sequence)
  - [5.2 Runtime Operations](#52-runtime-operations)
  - [5.3 Shutdown & Reload](#53-shutdown--reload)
  - [5.4 Diagnostics](#54-diagnostics)
- [6. Extensibility Guidelines & Best Practices](#6-extensibility-guidelines--best-practices)

## 1. Runtime Architecture

### 1.1 Layering
- **USP (OTAPI.UnifiedServerProcess)** – the patched server runtime layer (evolved from OTAPI) that provides per-server context isolation (`RootContext`) and runtime contracts such as TrProtocol packet models and detourable hook surfaces. Unifier runtime code reaches Terraria state through this layer.
- **UnifierTSL Core** – the launcher itself, plus orchestration, multi-server coordination, logging, config, module loading, and the plugin host.
- **Modules & Plugins** – your assemblies, staged under `plugins/`. They can be core hosts or feature satellites, and they can embed dependencies (managed, native, NuGet) for the loader to pull out automatically.
- **Console Client / Publisher** – tooling projects that sit alongside the runtime and share the same subsystems.

### 1.2 Boot Flow
1. `Program.cs` calls `UnifierApi.HandleCommandLinePreRun(args)`, then `UnifierApi.PrepareRuntime(args)` to parse launcher overrides, load `config/config.json`, merge startup settings, and configure durable logging.
2. `Initializer.Initialize()` and `UnifierApi.InitializeCore()` set up global services (logging, event hub, module loader) and initialize a `PluginOrchestrator`.
3. Modules get discovered and preloaded through `ModuleAssemblyLoader` — assemblies are staged and dependency blobs extracted.
4. Plugin hosts (the built-in .NET host plus custom `[PluginHost(...)]` hosts discovered from loaded modules) discover, load, and initialize plugins.
5. `UnifierApi.CompleteLauncherInitialization()` resolves any missing interactive port/password inputs, syncs the effective runtime snapshot, and raises `EventHub.Launcher.InitializedEvent`.
6. `UnifiedServerCoordinator` opens the listening socket and spins up a `ServerContext` for each configured world.
7. After the coordinator is live, `UnifierApi.StartRootConfigMonitoring()` enables hot reload for the launcher root config.
8. Event bridges (chat, game, coordinator, netplay, server) hook into USP/Terraria via detours and pipe everything into `EventHub`.

### 1.3 Major Components
- `UnifierApi` – your main entry point for grabbing loggers, events, plugin hosts, and window title helpers.
- `UnifiedServerCoordinator` – the multi-server router that manages shared Terraria state and connection lifecycles.
- `ServerContext` – a USP `RootContext` subclass per world, wiring in logging, packet receivers, and extension slots.
- `PluginOrchestrator` + hosts – handle plugin discovery, loading, init ordering, and shutdown/unload.
- `ModuleAssemblyLoader` – takes care of module staging, dependency extraction, collectible load contexts, and unload order.
- `EventHub` – the central event registry that bridges MonoMod detours into priority-sorted event pipelines.
- `Logging` subsystem – lightweight, allocation-friendly logging with metadata injection and pluggable writers.

## 2. Core Services & Subsystems

### 2.1 Event Hub

The event system is the heart of UnifierTSL's pub/sub — a fast, priority-sorted pipeline with no heap allocations and full type safety.

<details>
<summary><strong>Expand Event Hub implementation deep dive</strong></summary>

#### Architecture Overview

**`EventHub` (src/UnifierTSL/EventHub.cs)** collects all event providers in one place, grouped by domain:
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

You access events like this: `UnifierApi.EventHub.Game.PreUpdate`, `UnifierApi.EventHub.Chat.MessageEvent`, etc.

`UnifierApi.EventHub.Launcher.InitializedEvent` fires from `UnifierApi.CompleteLauncherInitialization()` once launcher arguments are finalized (including interactive prompts) — right before `UnifiedServerCoordinator.Launch(...)` starts accepting clients.

#### Event Provider Types

There are **four provider types**, each tuned for different needs around mutability and cancellation:

| Provider Type | Event Data | Cancellation | Use Case |
|--------------|------------|--------------|----------|
| `ValueEventProvider<T>` | Mutable (`ref T`) | Yes (`Handled` flag) | Events where handlers modify data and can cancel actions (e.g., chat commands, transfers) |
| `ReadonlyEventProvider<T>` | Immutable (`in T`) | Yes (`Handled` flag) | Events where handlers inspect data and can veto actions (e.g., connection validation) |
| `ValueEventNoCancelProvider<T>` | Mutable (`ref T`) | No | Informational events where handlers may need to modify shared state |
| `ReadonlyEventNoCancelProvider<T>` | Immutable (`in T`) | No | Pure notification events for lifecycle/telemetry (e.g., `PreUpdate`, `PostUpdate`) |

All event args are `ref struct` types living on the stack — no heap allocations, no GC pressure.

#### Priority System and Handler Registration

Handlers run in **ascending priority order** (lower number = higher priority):
```csharp
public enum HandlerPriority : byte
{
    Highest = 0,
    VeryHigh = 10,
    Higher = 20,
    High = 30,
    AboveNormal = 40,
    Normal = 50,      // Default
    BelowNormal = 60,
    Low = 70,
    Lower = 80,
    VeryLow = 90,
    Lowest = 100
}
```

**Registration API**:
```csharp
// Basic registration (Normal priority on ValueEventProvider)
UnifierApi.EventHub.Chat.ChatEvent.Register(OnChat);

// With explicit priority
UnifierApi.EventHub.Netplay.ConnectEvent.Register(OnConnect, HandlerPriority.Higher);

// With filter option (run only if already handled)
UnifierApi.EventHub.Game.GameHardmodeTileUpdate.Register(OnTileUpdate,
    HandlerPriority.Low, FilterEventOption.Handled);

// Unregistration (pass same delegate reference)
UnifierApi.EventHub.Chat.ChatEvent.UnRegister(OnChat);
```

**Under the hood** (src/UnifierTSL/Events/Core/ValueEventBaseProvider.cs:28-68):
- Uses a **volatile snapshot array** so reads during invocation are lock-free
- **Binary search insertion** keeps handlers sorted by priority automatically
- **Copy-on-write**: registration creates a new array, so ongoing invocations against the old snapshot aren't disrupted
- Only modifications are guarded by `Lock _sync`

#### Filtering and Cancellation Mechanics

**FilterEventOption** controls handler execution based on event state:
```csharp
public enum FilterEventOption : byte
{
    Normal = 1,      // Only execute if NOT handled
    Handled = 2,     // Only execute if already handled (e.g., cleanup/logging)
    All = 3          // Always execute (Normal | Handled)
}
```

**Cancellation Model**:
- `Handled = true`: Marks event as "consumed" (conceptually cancels the action)
- `StopPropagation = true`: Stops executing remaining handlers
- Different providers interpret `Handled` differently:
  - `ReadonlyEventProvider`: Returns boolean to caller (`out bool handled`)
  - `ValueEventProvider`: Caller receives the cancellation result from `Invoke(ref data, out bool handled)` (handlers set `args.Handled`)
  - No-cancel providers: No `Handled` flag exposed

**Example - Chat Command Interception**:
```csharp
UnifierApi.EventHub.Chat.MessageEvent.Register(
    (ref ReadonlyEventArgs<MessageEvent> args) =>
    {
        if (args.Content.Text.StartsWith("!help"))
        {
            SendHelpText(args.Content.Sender);
            args.Handled = true;  // Prevent further processing
        }
    },
    HandlerPriority.Higher);
```

#### Event Bridges — MonoMod Integration

Event bridges are the glue between **MonoMod runtime detours** and the event system — they turn low-level hooks into typed event invocations:

**GameEventBridge** (src/UnifierTSL/Events/Handlers/GameEventBridge.cs):
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
        GameInitialize.Invoke(new(root.ToServer())); // Before Terraria.Main.Initialize original logic
        orig(self, root);
    }

    private void OnStartServer(...) {
        orig(self);
        GamePostInitialize.Invoke(new(self.root.ToServer())); // After NetplaySystemContext.StartServer
    }

    private void OnUpdate(On.Terraria.Main.orig_Update orig, Main self, RootContext root, GameTime gameTime) {
        ServerEvent data = new(root.ToServer());
        PreUpdate.Invoke(data);      // Before original
        orig(self, root, gameTime);  // Execute original Terraria logic
        PostUpdate.Invoke(data);     // After original
    }

    private bool OnHardmodeTilePlace(...) {
        GameHardmodeTileUpdateEvent data = new(x, y, type, self.root.ToServer());
        GameHardmodeTileUpdate.Invoke(data, out bool handled);
        return !handled;
    }

    private bool OnHardmodeTileUpdate(...) {
        GameHardmodeTileUpdateEvent data = new(x, y, type, self.root.ToServer());
        GameHardmodeTileUpdate.Invoke(data, out bool handled);
        return !handled;  // Return false to cancel if handled
    }
}
```
`GameHardmodeTileUpdate` intentionally aggregates the two original OTAPI hardmode events (`HardmodeTilePlace` and `HardmodeTileUpdate`) behind one event provider.
Those hooks sit on Terraria hardmode tile infection/growth paths (for example evil/hallow spread and crystal shard growth), so one handler can enforce one policy.
After USP contextification, those event entry points move from static access to members on context instances, which is good for per-instance subscriptions but awkward for global registration. By detouring the two instance entry functions (`InvokeHardmodeTilePlace` and `InvokeHardmodeTileUpdate`) via MonoMod, Unifier exposes one global event stream; `self` carries the server root for the current call, and the bridge forwards `self.root.ToServer()` as `ServerContext`.

**ChatHandler** (src/UnifierTSL/Events/Handlers/ChatHandler.cs):
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

**NetplayEventBridge** (src/UnifierTSL/Events/Handlers/NetplayEventBridge.cs):
- `ConnectEvent` (cancellable) - Fires during client handshake
- `ReceiveFullClientInfoEvent` (cancellable) - After client metadata received
- `LeaveEvent` (informational) - Client disconnect notification
- `SocketResetEvent` (informational) - Socket cleanup notification

**CoordinatorEventBridge** (src/UnifierTSL/Events/Handlers/CoordinatorEventBridge.cs):
- `CheckVersion` (mutable) - Gate `ClientHello` version checks during pending connection authentication
- `SwitchJoinServerEvent` (mutable) - Select destination server for joining player
- `ServerCheckPlayerCanJoinIn` (mutable) - Per-server admission hook used before candidate server selection
- `JoinServer` (informational) - Raised once a player is bound to a destination server
- `PreServerTransfer` (cancellable) - Before transferring player between servers
- `PostServerTransfer` (informational) - After successful transfer
- `CreateSocketEvent` (mutable) - Customize socket creation
- `Started` (informational) - Fired after coordinator launch and startup logging completes
- `LastPlayerLeftEvent` (informational) - Fired on transition from `ActiveConnections > 0` to `0`

**ServerEventBridge** (src/UnifierTSL/Events/Handlers/ServerEventBridge.cs):
- `CreateConsoleService` (mutable) - Provide custom console implementation
- `AddServer` / `RemoveServer` (informational) - Server lifecycle notifications
- `ServerListChanged` (informational) - Aggregated server list changes

#### Event Content Hierarchy

Event payloads use typed interfaces to carry context around:
```csharp
public interface IEventContent { }  // Base marker

public interface IServerEventContent : IEventContent
{
    ServerContext Server { get; }  // Server-scoped events
}

public interface IPlayerEventContent : IServerEventContent
{
    int Who { get; }  // Player-scoped events (includes server)
}
```

Examples:
```csharp
// Base event (no context)
public readonly struct MessageEvent(...) : IEventContent { ... }

// Server-scoped
public readonly struct ServerEvent(ServerContext server) : IServerEventContent { ... }

// Player-scoped (inherits server context)
public readonly struct LeaveEvent(int plr, ServerContext server) : IPlayerEventContent
{
    public int Who { get; } = plr;
    public ServerContext Server { get; } = server;
}
```

</details>

#### Performance Characteristics

**Handler Invocation**:
- Snapshot read: O(1) volatile access (lock-free)
- Handler iteration: O(n) where n = handler count
- Filter check: O(1) bitwise AND per handler

**Memory**:
- Event args: stack-allocated `ref struct` — no heap allocation
- Handler snapshots: immutable arrays, GC-friendly (long-lived Gen2)
- Registration: O(log n) binary search + O(n) array copy

**In practice**, the actual cost depends on how many handlers you have, what filters are active, and how heavy your handler logic is. The pipeline avoids per-call heap allocations as long as your handlers do the same.

#### Best Practices

1. **Use `EventHub` for shared/high-traffic hook points** — it gives handler-level priority/filter control and predictable ordering. Raw MonoMod detours compose by detour registration order (typically last-registered-first), which is too coarse when multiple plugins need different ordering per event. If a hook is broadly useful, contribute an `EventHub` provider instead of adding plugin-specific detours
2. **Remember `ref struct` rules** — event args can't be captured in closures or async methods, so grab what you need from them synchronously
3. **Don't block in handlers** — they run on the game thread. Offload heavy work with `Task.Run()`
4. **Unregister on shutdown** — always call `UnRegister()` in your plugin's dispose hook (`DisposeAsync`/`DisposeAsync(bool isDisposing)`) to avoid leaks
5. **Pick the right provider type** — use readonly/no-cancel variants when you can; they're lighter
6. **Go easy on `Highest` priority** — save it for critical infrastructure like permission checks

<details>
<summary><strong>Expand custom event provider extension example</strong></summary>

#### Advanced: Custom Event Providers

To add new events, follow this pattern:
```csharp
// 1. Define event content struct
public readonly struct MyCustomEvent(ServerContext server, int data) : IServerEventContent
{
    public ServerContext Server { get; } = server;
    public int Data { get; } = data;
}

// 2. Create provider in appropriate bridge
public class MyEventBridge
{
    public readonly ValueEventProvider<MyCustomEvent> CustomEvent = new();

    public MyEventBridge() {
        On.Terraria.Something.Method += OnMethod;
    }

    private void OnMethod(...) {
        MyCustomEvent data = new(server, 42);
        CustomEvent.Invoke(ref data, out bool handled);
        if (handled) return;  // Honor cancellation
        // ... original logic
    }
}

// 3. Add to EventHub
public class EventHub
{
    public readonly MyEventBridge MyEvents = new();
}
```

For real-world examples, check out `src/Plugins/TShockAPI/Handlers/MiscHandler.cs` and `src/Plugins/CommandTeleport`.

</details>

### 2.2 Module System

The module system handles loading your plugin DLLs, pulling in dependencies, hot-reloading, and cleaning up when you unload. It sits between raw DLLs and the plugin host, using collectible `AssemblyLoadContext`s so modules can be swapped at runtime.

<details>
<summary><strong>Expand Module System implementation deep dive</strong></summary>

#### Module Types and Organization

**Three Module Categories** (src/UnifierTSL/Module/ModulePreloadInfo.cs):

1. **Core Modules** (`[assembly: CoreModule]`):
   - Anchor for related assemblies
   - Get dedicated subdirectory: `plugins/<ModuleName>/`
   - Loaded in isolated `ModuleLoadContext` (collectible)
   - Can declare dependencies via `[assembly: ModuleDependencies<TProvider>]`
   - Other modules can depend on them via `[assembly: RequiresCoreModule("ModuleName")]`

2. **Satellite Modules** (`[assembly: RequiresCoreModule("CoreModuleName")]`):
   - Must reference an existing core module
   - Staged in core module's directory: `plugins/<CoreModuleName>/SatelliteName.dll`
   - **Share core module's `ModuleLoadContext`** (critical: share types, coordinate unload)
   - Cannot declare own dependencies (inherit from core module)
   - Loaded after core module initializes

3. **Independent Modules** (no special attributes):
   - Stay in `plugins/` root directory
   - Loaded in isolated `ModuleLoadContext`
   - Cannot be targeted by satellites
   - May declare dependencies if needed

**Module Discovery and Staging** (src/UnifierTSL/Module/ModuleAssemblyLoader.cs:43-175):

The loader **doesn't actually load assemblies during discovery** — it just reads PE headers via `MetadataLoadContext`:
```csharp
public ModulePreloadInfo PreloadModule(string dll)
{
    // 1. Read PE headers WITHOUT loading assembly
    using PEReader peReader = MetadataBlobHelpers.GetPEReader(dll);
    MetadataReader metadataReader = peReader.GetMetadataReader();

    // 2. Extract assembly name
    AssemblyDefinition asmDef = metadataReader.GetAssemblyDefinition();
    string moduleName = metadataReader.GetString(asmDef.Name);

    // 3. Check attributes via PE metadata
    bool isCoreModule = MetadataBlobHelpers.HasCustomAttribute(metadataReader, "CoreModuleAttribute");
    bool hasDependencies = MetadataBlobHelpers.HasCustomAttribute(metadataReader, "ModuleDependenciesAttribute");
    string? requiresCoreModule = TryReadAssemblyAttributeData(metadataReader, "RequiresCoreModuleAttribute");

    // 4. Determine staging location
    string newLocation;
    if (!hasDependencies && !isCoreModule && requiresCoreModule is null) {
        newLocation = Path.Combine(loadDirectory, fileName);  // Independent: stay in root
    } else {
        string moduleDir = Path.Combine(loadDirectory,
            (hasDependencies || isCoreModule) ? moduleName : requiresCoreModule!);
        Directory.CreateDirectory(moduleDir);
        newLocation = Path.Combine(moduleDir, Path.GetFileName(dll));
    }

    // 5. Move files (preserving timestamps) and generate signature
    CopyFileWithTimestamps(dll, newLocation);
    CopyFileWithTimestamps(dll.Replace(".dll", ".pdb"), newLocation.Replace(".dll", ".pdb"));
    File.Delete(dll);  // Remove original

    return new ModulePreloadInfo(FileSignature.Generate(newLocation), ...);
}
```

`PreloadModules()` indexes discovered DLLs by `dll.Name` in a dictionary. If you have duplicate names across `plugins/` subdirectories (or between a subdirectory and root), the last one indexed wins. Since root-level files are indexed after subdirectories, a `plugins/<Name>.dll` at the root will override same-name files found earlier in child folders.

**Validation Rules**:
- Cannot be both `CoreModule` and `RequiresCoreModule`
- `RequiresCoreModule` modules cannot declare dependencies
- `RequiresCoreModule` must specify core module name

#### FileSignature Change Detection

**FileSignature** (src/UnifierTSL/FileSystem/FileSignature.cs) tracks module changes with three levels of checking:

```csharp
public record FileSignature(string FilePath, string Hash, DateTime LastWriteTimeUtc)
{
    // Level 1: Fastest - check path + timestamp
    public bool QuickEquals(string filePath) {
        return FilePath == filePath && LastWriteTimeUtc == File.GetLastWriteTimeUtc(filePath);
    }

    // Level 2: Medium - check timestamp + SHA256 hash
    public bool ExactEquals(string filePath) {
        if (LastWriteTimeUtc != File.GetLastWriteTimeUtc(filePath)) return false;
        return Hash == ComputeHash(filePath);
    }

    // Level 3: Slowest/Most thorough - check SHA256 hash only
    public bool ContentEquals(string filePath) {
        return Hash == ComputeHash(filePath);
    }
}
```

The module loader uses `FileSignature.Hash` comparison during `Load()` to spot updated modules (src/UnifierTSL/Module/ModuleAssemblyLoader.cs:204-207).

#### ModuleLoadContext - Collectible Assembly Loading

**ModuleLoadContext** (src/UnifierTSL/Module/ModuleLoadContext.cs:16) extends `AssemblyLoadContext` with:

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

**What's important here**:
- **`isCollectible: true`** — this is what lets you unload modules at runtime (GC collects the ALC once no references remain)
- **Disposal actions** — plugins register cleanup via `AddDisposeAction(Func<Task>)`, which runs during the `Unloading` event
- **Resolution chain** — multi-tier fallback for resolving both managed and native assemblies

**Assembly Resolution Strategy** (src/UnifierTSL/Module/ModuleLoadContext.cs:83-128):

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

**Unmanaged DLL Resolution** (src/UnifierTSL/Module/ModuleLoadContext.cs:134-155):
- Reads `dependencies.json` from module directory
- Matches by manifest entry (`DependencyItem.FilePath`) rather than probing filesystem RID folders at load time
- Accepts direct name matches (`sqlite3.dll`) and version-suffixed manifest names (`sqlite3.1.2.3.dll`)
- Loads the first non-obsolete manifest match via `LoadUnmanagedDllFromPath()`
- Current `LoadUnmanagedDll` behavior is manifest-driven; RID fallback is primarily applied earlier during dependency extraction (`NugetPackageFetcher.GetNativeLibsPathsAsync`, `NativeEmbeddedDependency`)

#### Dependency Management

**Dependency Declaration** (src/UnifierTSL/Module/ModuleDependenciesAttribute.cs):
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

**Dependency Types**:

1. **NuGet Dependencies** (src/UnifierTSL/Module/Dependencies/NugetDependency.cs):
   - Resolves transitive dependencies via `NugetPackageCache.ResolveDependenciesAsync()`
   - Downloads missing packages to global packages folder (`~/.nuget/packages`)
   - Extracts managed libs (matching target framework) + native libs (matching RID)
   - Returns `LibraryEntry[]` with lazy streams

2. **Managed Embedded Dependencies** (src/UnifierTSL/Module/Dependencies/ManagedEmbeddedDependency.cs):
   - Reads assembly identity from embedded resource via PE headers
   - Extracts embedded DLL to `{moduleDir}/lib/{AssemblyName}.dll`

3. **Native Embedded Dependencies** (src/UnifierTSL/Module/Dependencies/NativeEmbeddedDependency.cs):
   - Probes RID fallback chain for matching embedded resource
   - Extracts to `{moduleDir}/runtimes/{rid}/native/{libraryName}.{ext}`

**Dependency Extraction Process** (src/UnifierTSL/Module/ModuleAssemblyLoader.cs:368-560):

```csharp
private bool UpdateDependencies(string dll, ModuleInfo info)
{
    // 1. Validate module structure (must be in named directory)
    // 2. Load previous dependencies.json
    DependenciesConfiguration prevConfig = DependenciesConfiguration.LoadDependenciesConfig(moduleDir);

    // 3. Extract new/updated dependencies
    foreach (ModuleDependency dependency in dependencies) {
        if (dependency.Version != prevConfig.Version) {
            ImmutableArray<LibraryEntry> items = dependency.LibraryExtractor.Extract(Logger);
            // ... track highest version per file path
        }
    }

    // 4. Copy files with lock handling
    foreach (var (dependency, item) in highestVersion) {
        try {
            using Stream source = item.Stream.Value;
            destination = Utilities.IO.SafeFileCreate(targetPath, out Exception? ex);

            if (destination != null) {
                source.CopyTo(destination);  // Success path
            }
            else if (ex is IOException && FileSystemHelper.FileIsInUse(ex)) {
                // File is locked by loaded assembly! Create versioned file instead
                string versionedPath = Path.ChangeExtension(item.FilePath,
                    $"{item.Version}.{Path.GetExtension(item.FilePath)}");
                destination = Utilities.IO.SafeFileCreate(versionedPath, ...);
                source.CopyTo(destination);

                // Track both old (obsolete) and new (active) files
                currentSetting.Dependencies[dependency.Name].Manifests.Add(
                    new DependencyItem(item.FilePath, item.Version) { Obsolete = true });
                currentSetting.Dependencies[dependency.Name].Manifests.Add(
                    new DependencyItem(versionedPath, item.Version));
            }
        }
        finally { destination?.Dispose(); }
    }

    // 5. Cleanup obsolete files and save dependencies.json
    currentConfig.SpecificDependencyClean(moduleDir, prevConfig.Setting);
    currentConfig.Save(moduleDir);
}
```

**What happens when a file is locked**:
- The loader detects locked files via `IOException` HResult code
- It creates a versioned copy instead: `Newtonsoft.Json.13.0.3.dll` alongside `Newtonsoft.Json.dll`
- The old file gets marked `Obsolete` in the manifest
- Cleanup happens on next restart, once the file is no longer locked

**RID Graph for Native Dependencies** (src/UnifierTSL/Module/Dependencies/RidGraph.cs):
- Loads embedded `RuntimeIdentifierGraph.json` (NuGet's official RID graph)
- BFS traversal for RID expansion: `win-x64` → [`win-x64`, `win`, `any`]
- Used by extraction-time native selection (`NugetPackageFetcher`, `NativeEmbeddedDependency`)
- `ModuleLoadContext.LoadUnmanagedDll()` is currently manifest-driven and does not perform RID fallback probing itself

#### Module Unload and Dependency Graph

**LoadedModule** (src/UnifierTSL/Module/LoadedModule.cs) keeps track of who depends on whom:

```csharp
public record LoadedModule(
    ModuleLoadContext Context,
    Assembly Assembly,
    ImmutableArray<ModuleDependency> Dependencies,
    FileSignature Signature,
    LoadedModule? CoreModule)  // Null for core/independent modules
{
    // Modules that depend on THIS module
    public ImmutableArray<LoadedModule> DependentModules => dependentModules;

    // Modules that THIS module depends on
    public ImmutableArray<LoadedModule> DependencyModules => dependencyModules;

    // Thread-safe reference tracking
    public static void Reference(LoadedModule dependency, LoadedModule dependent) {
        ImmutableInterlocked.Update(ref dependent.dependencyModules, x => x.Add(dependency));
        ImmutableInterlocked.Update(ref dependency.dependentModules, x => x.Add(dependent));
    }
}
```

**Unload Cascade** (src/UnifierTSL/Module/LoadedModule.cs:50-68):
```csharp
public void Unload()
{
    if (CoreModule is not null) return;  // Cannot unload satellites (share ALC)
    if (Unloaded) return;

    // Recursively unload all dependents
    foreach (LoadedModule dependent in DependentModules) {
        if (dependent.CoreModule == this) {
            dependent.Unreference();  // Just break link (will unload with core)
        } else {
            dependent.Unload();  // Recursively cascade
        }
    }

    Unreference();     // Clear all references
    unloaded = true;
    Context.Unload();  // Triggers OnUnloading event -> disposal actions
}
```

**Topological Sort for Ordered Unload** (src/UnifierTSL/Module/LoadedModule.cs:77-109):
```csharp
// Get dependents in execution order (preorder = leaf-to-root, postorder = root-to-leaf)
public ImmutableArray<LoadedModule> GetDependentOrder(bool includeSelf, bool preorder)
{
    HashSet<LoadedModule> visited = [];  // Cycle detection
    Queue<LoadedModule> result = [];

    void Visit(LoadedModule module) {
        if (!visited.Add(module)) return;  // Already visited (cycle detected)

        if (preorder) result.Enqueue(module);
        foreach (var dep in module.DependentModules) Visit(dep);
        if (!preorder) result.Enqueue(module);
    }

    Visit(this);
    return result.ToImmutableArray();
}
```

**ForceUnload** (src/UnifierTSL/Module/ModuleAssemblyLoader.cs:179-189):
```csharp
public void ForceUnload(LoadedModule module)
{
    // If satellite, unload core instead (satellites share ALC)
    if (module.CoreModule is not null) {
        ForceUnload(module.CoreModule);
        return;
    }

    // Unload in postorder (dependents before dependencies)
    foreach (LoadedModule m in module.GetDependentOrder(includeSelf: true, preorder: false)) {
        Logger.Debug($"Unloading module {m.Signature.FilePath}");
        m.Unload();
        moduleCache.Remove(m.Signature.FilePath, out _);
    }
}
```

#### Module vs. Plugin Relationship

**These are two different things**:

| Concept | Responsibility | Key Type |
|---------|---------------|----------|
| **Module** | Assembly loading, dependency management, ALC lifecycle | `LoadedModule` |
| **Plugin** | Business logic, event handlers, game integration | `IPlugin` / `PluginContainer` |

**Flow**:
1. `ModuleAssemblyLoader` stages and loads assemblies → `LoadedModule`
2. `PluginDiscoverer` scans loaded modules for `IPlugin` implementations → `IPluginInfo`
3. `PluginLoader` instantiates plugin classes → `IPlugin` instances
4. `PluginContainer` wraps `LoadedModule` + `IPlugin` + `PluginMetadata`
5. `PluginOrchestrator` manages plugin lifecycle (init, shutdown, unload)

**Plugin Discovery** (src/UnifierTSL/PluginHost/Hosts/Dotnet/PluginDiscoverer.cs):
```csharp
public IReadOnlyList<IPluginInfo> DiscoverPlugins(string pluginsDirectory)
{
    // 1. Use module loader to discover/stage modules
    ModuleAssemblyLoader moduleLoader = new(pluginsDirectory);
    List<ModulePreloadInfo> modules = moduleLoader.PreloadModules(ModuleSearchMode.Any).ToList();

    // 2. Extract plugin metadata (find IPlugin implementations)
    foreach (ModulePreloadInfo module in modules) {
        pluginInfos.AddRange(ExtractPluginInfos(module));
    }

    return pluginInfos;
}
```

**Plugin Loading** (src/UnifierTSL/PluginHost/Hosts/Dotnet/PluginLoader.cs):
```csharp
public IPluginContainer? LoadPlugin(IPluginInfo pluginInfo)
{
    // 1. Load module
    ModuleAssemblyLoader loader = new("plugins");
    if (!loader.TryLoadSpecific(info.Module, out LoadedModule? loaded, ...)) {
        return null;
    }

    // 2. Instantiate plugin
    Type? type = loaded.Assembly.GetType(info.EntryPoint.EntryPointString);
    IPlugin instance = (IPlugin)Activator.CreateInstance(type)!;

    // 3. Register disposal action in ALC
    loaded.Context.AddDisposeAction(async () => await instance.DisposeAsync());

    // 4. Wrap in container
    return new PluginContainer(info.Metadata, loaded, instance);
}
```

</details>

#### Best Practices

1. **Group related features into core modules** — satellites share dependencies and unload together, which keeps things clean
2. **Declare your dependencies** — use `ModuleDependenciesAttribute` instead of manually copying DLLs
3. **Test hot reload** — use `FileSignature.Hash` checks to make sure update detection works
4. **Release file handles quickly** — long-lived streams can prevent clean unloads
5. **Don't cache `LoadedModule` references across reloads** — they go stale
6. **Let the loader handle NuGet** — transitive resolution is automatic
7. **Embed for multiple RIDs** if you ship native libs, or lean on NuGet package RID probing

#### Performance Notes

**Loading**: Cost depends on how many assemblies you have, how deep the dependency graph goes, disk speed, and whether the NuGet cache is warm. Metadata scanning is lighter than a full load, but first-time NuGet resolution or native payload extraction can dominate.

**Memory**: Footprint scales with loaded assemblies and active `ModuleLoadContext` instances. Unloaded modules become reclaimable once references are released and GC runs.

### 2.3 Plugin Host Orchestration
- `PluginOrchestrator` (`src/UnifierTSL/PluginHost/PluginOrchestrator.cs`) registers the built-in `DotnetPluginHost` plus any extra `[PluginHost(...)]` hosts discovered from loaded modules. This extension point is what enables non-default plugin/script runtimes.
- Want a custom host? Implement `IPluginHost`, give it a parameterless constructor, and tag it with `[PluginHost(majorApiVersion, minorApiVersion)]`.
- Host admission is version-gated against `PluginOrchestrator.ApiVersion` (currently `1.0.0`): `major` must match exactly, and the host's `minor` must be ≤ the runtime's `minor`. If it doesn't match, the host is skipped with a warning.
- `.InitializeAllAsync` preloads modules, discovers plugin entry points via `PluginDiscoverer`, and loads them through `PluginLoader`.
- Plugin containers are sorted by `InitializationOrder`, and `IPlugin.InitializeAsync` is called with `PluginInitInfo` lists so you can await specific dependencies.
- `ShutdownAllAsync` and `UnloadAllAsync` exist on the orchestrator, but heads up — the built-in `DotnetPluginHost` still has TODO stubs for `ShutdownAsync`/`UnloadPluginsAsync`. For now, disposal is mainly driven by the module unload path.

### 2.4 Networking & Coordinator

The networking layer runs all your servers behind **a single port** — it routes packets to the right server, lets you intercept/modify them middleware-style, and uses pooled buffers to keep allocations low.

<details>
<summary><strong>Expand Networking & Coordinator implementation deep dive</strong></summary>

#### UnifiedServerCoordinator Architecture

**UnifiedServerCoordinator** (src/UnifierTSL/UnifiedServerCoordinator.cs) centralizes shared networking state across all servers:

**Global Shared State**:
```csharp
// Index = client slot (0-255)
Player[] players                              // Player entity state
RemoteClient[] globalClients                  // TCP socket wrappers
LocalClientSender[] clientSenders             // Per-client packet senders
MessageBuffer[] globalMsgBuffers              // Receive buffers (256 × servers count)
ServerContext?[] clientCurrentlyServers       // Client → server mapping (read/write via Volatile helpers)
```

**Connection Pipeline**:

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

**PendingConnection** (src/UnifierTSL/UnifiedServerCoordinator.cs:529-696):
- Handles pre-authentication packets before server assignment
- Validates client version against `Terraria{Main.curRelease}` (with override via `Coordinator.CheckVersion`)
- `Coordinator.CheckVersion` is currently invoked twice during `ClientHello` handling; handlers should be idempotent and avoid side effects that assume single invocation
- Password authentication (if `UnifierApi.ServerPassword` is set)
- Collects client metadata: `ClientUUID`, player name and appearance
- Kicks incompatible clients with `NetworkText` reasons

**Server Transfer Protocol** (src/UnifierTSL/UnifiedServerCoordinator.cs:290-353):
```csharp
public static void TransferPlayerToServer(byte plr, ServerContext to, bool ignoreChecks = false)
{
    ServerContext? from = GetClientCurrentlyServer(plr);
    if (from is null || from == to) return;
    if (!to.IsRunning && !ignoreChecks) return;

    UnifierApi.EventHub.Coordinator.PreServerTransfer.Invoke(new(from, to, plr), out bool handled);
    if (handled) return;

    // Sync leave → switch mapping → sync join
    from.SyncPlayerLeaveToOthers(plr);
    from.SyncServerOfflineToPlayer(plr);
    SetClientCurrentlyServer(plr, to);
    to.SyncServerOnlineToPlayer(plr);
    to.SyncPlayerJoinToOthers(plr);

    UnifierApi.EventHub.Coordinator.PostServerTransfer.Invoke(new(from, to, plr));
}
```

**Packet Routing Hook** (src/UnifierTSL/UnifiedServerCoordinator.cs:356-416):
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
        // Decode packet length, then route payload to NetPacketHandler.ProcessBytes(...)
        NetPacketHandler.ProcessBytes(server, buffer, contentStart, contentLength);
    }
}
```

#### Packet Interception - NetPacketHandler

**NetPacketHandler** (src/UnifierTSL/Events/Handlers/NetPacketHandler.cs) provides **middleware-style packet processing** with cancellation and rewriting capabilities.

**Handler Registration**:
```csharp
NetPacketHandler.Register<TrProtocol.NetPackets.TileChange>(
    (ref ReceivePacketEvent<TileChange> args) =>
    {
        if (IsProtectedRegion(args.Packet.X, args.Packet.Y)) {
            args.HandleMode = PacketHandleMode.Cancel;  // Block packet
        }
    },
    HandlerPriority.Highest);
```

**Handler Storage**:
- Static array: `Array?[] handlers = new Array[INetPacket.GlobalIDCount]`
- One slot per packet type (indexed by `TPacket.GlobalID`)
- Each slot stores `PriorityItem<TPacket>[]` (sorted by priority)

**Packet Processing Flow**:
```
ProcessBytes(server, messageBuffer, contentStart, contentLength)
    ↓
1. Parse MessageID from buffer
    ↓
2. Dispatch to type-specific handler via switch(messageID):
    - ProcessPacket_F<TPacket>()    // Fixed, non-side-specific
    - ProcessPacket_FS<TPacket>()   // Fixed, side-specific
    - ProcessPacket_D<TPacket>()    // Dynamic (managed)
    - ProcessPacket_DS<TPacket>()   // Dynamic, side-specific
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

**PacketHandleMode** (src/UnifierTSL/Events/Handlers/NetPacketHandler.cs):
```csharp
public enum PacketHandleMode : byte
{
    None = 0,       // Pass through to original Terraria logic
    Cancel = 1,     // Block packet (prevent processing)
    Overwrite = 2   // Use modified packet (re-inject via ClientPacketReceiver)
}
```

**Example - Packet Modification**:
```csharp
NetPacketHandler.Register<TrProtocol.NetPackets.TileChange>(
    (ref ReceivePacketEvent<TileChange> args) =>
    {
        // Deny tile placement in protected regions
        if (IsProtectedRegion(args.Packet.X, args.Packet.Y)) {
            args.HandleMode = PacketHandleMode.Cancel;
        }
    });
```

#### Packet Sending - PacketSender & LocalClientSender

**PacketSender** (src/UnifierTSL/Network/PacketSender.cs) - Abstract base class for generic-specialized packet transmission (lets struct packet types stay on no-boxing fast paths):

**API Methods**:
```csharp
// Fixed-size packets (unmanaged structs)
public void SendFixedPacket<TPacket>(scoped in TPacket packet)
    where TPacket : unmanaged, INonSideSpecific, INetPacket

// Dynamic-size packets (managed types)
public void SendDynamicPacket<TPacket>(scoped in TPacket packet)
    where TPacket : struct, IManagedPacket, INonSideSpecific, INetPacket

// Server-side variants (set IsServerSide flag)
public void SendFixedPacket_S<TPacket>(scoped in TPacket packet) where TPacket : unmanaged, ISideSpecific, INetPacket
public void SendDynamicPacket_S<TPacket>(scoped in TPacket packet) where TPacket : struct, IManagedPacket, ISideSpecific, INetPacket

// Runtime dispatch (uses a switch covering many packet types)
public void SendUnknownPacket<TPacket>(scoped in TPacket packet) where TPacket : struct, INetPacket
```

**Buffer Management** (src/UnifierTSL/Network/SocketSender.cs:79-117):
```csharp
private unsafe byte[] AllocateBuffer<TPacket>(in TPacket packet, out byte* ptr_start) where TPacket : INetPacket
{
    int capacity = packet is TrProtocol.NetPackets.TileSection ? 16384 : 1024;
    byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);

    fixed (byte* buf = buffer) {
        ptr_start = buf + 2;  // Reserve 2 bytes for length header
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

**LocalClientSender** (src/UnifierTSL/Network/LocalClientSender.cs) - Per-client packet sender:
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

**Packet Reception Simulation - ClientPacketReceiver** (src/UnifierTSL/Network/ClientPacketReceiver.cs):

Used when handlers set `HandleMode = Overwrite`:
```csharp
public void AsReceiveFromSender_FixedPkt<TPacket>(LocalClientSender sender, scoped in TPacket packet)
    where TPacket : unmanaged, INetPacket, INonSideSpecific
{
    byte[] buffer = ArrayPool<byte>.Shared.Rent(sizeof(TPacket) + 4);
    try {
        unsafe {
            fixed (byte* buf = buffer) {
                void* ptr = buf + 2;
                packet.WriteContent(ref ptr);  // Serialize modified packet
                short len = (short)((byte*)ptr - buf);
                *(short*)buf = len;
            }
        }

        // Inject as if received from sender
        Server.NetMessage.buffer[sender.ID].GetData(Server, 0, len, out _, buffer, ...);
    }
    finally {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
```

#### TrProtocol Integration

UnifierTSL consumes **TrProtocol** packet models that USP bundles into the runtime via IL merge:

**Packet Characteristics**:
- **Many packet types** defined as structs under `TrProtocol.NetPackets` and `TrProtocol.NetPackets.Modules`
- **Interfaces**: `INetPacket`, `IManagedPacket`, `ISideSpecific`, `INonSideSpecific`
- **Dispatch strategy**: prefer generic methods with interface constraints over runtime `is`-based interface dispatch that boxes packet structs; paired interfaces like `ISideSpecific` / `INonSideSpecific` encode mutually exclusive compile-time paths and reduce misrouting risk
- **Serialization contract**: pointer-based `ReadContent(ref void* ptr, void* ptrEnd)` and `WriteContent(ref void* ptr)`; the read end-pointer enables bounded reads and managed exceptions on overflow attempts

For concrete packet models and fields, inspect the actual TrProtocol packet structs shipped with the runtime.

#### Network Patching for USP

**UnifiedNetworkPatcher** (src/UnifierTSL/Network/UnifiedNetworkPatcher.cs:10-31) hooks USP initialization to redirect to shared arrays:

```csharp
On.Terraria.NetplaySystemContext.StartServer += (orig, self) =>
{
    ServerContext server = self.root.ToServer();
    self.Connection.ResetSpecialFlags();
    self.ResetNetDiag();

    // Replace per-server arrays with global shared arrays
    self.Clients = UnifiedServerCoordinator.globalClients;
    server.NetMessage.buffer = UnifiedServerCoordinator.globalMsgBuffers;

    // Disable per-server broadcasting (coordinator handles it)
    On.Terraria.NetplaySystemContext.StartBroadCasting = (_, _) => { };
On.Terraria.NetplaySystemContext.StopBroadCasting = (_, _) => { };
};
```

</details>

#### Best Practices

1. **Packet handlers**:
   - Register them early (in `InitializeAsync` or `BeforeGlobalInitialize`)
   - Choose priority by ordering requirements; reserve `Highest` for handlers that must run before everything else (for example hard security gates)
   - Always unregister in your dispose hook (`DisposeAsync(bool isDisposing)` if you're using `BasePlugin`)

2. **Packet modification**:
   - Pick `Cancel` vs `Overwrite` by intent: `Cancel` drops the packet, `Overwrite` re-injects your modified packet
   - If you set `HandleMode = Overwrite`, usually also set `StopPropagation = true` unless you explicitly want downstream handlers to process the rewritten packet
   - Keep packet data read-only unless you intentionally overwrite it

3. **Post-processing callbacks (`PacketProcessed`)**:
   - Use for after-processing work such as metrics, tracing, or business logic that depends on final outcome
   - Branch on the callback `PacketHandleMode` (`None`, `Cancel`, `Overwrite`) instead of assuming a single path

4. **Server transfers**:
   - Treat `PreServerTransfer` as the veto point before any state swap
   - Use `PostServerTransfer` for logic that must run after mapping switch + join sync
   - Query current mapping via coordinator helpers (`GetClientCurrentlyServer`) instead of caching long-lived snapshots

5. **Memory and sender lifecycle**:
   - `PacketSender` and `ClientPacketReceiver` rent temporary buffers from `ArrayPool<byte>.Shared`; never retain rented arrays past the callback/scope
   - `UnifiedServerCoordinator` pre-allocates one `LocalClientSender` per client slot in `clientSenders`

#### Performance Notes

**Packet processing**: Handler lookup is O(1) via packet GlobalID. Total cost scales with how many handlers you registered for that packet type and what they do. Serialization overhead depends on packet shape and size.

**Buffer pooling**: Uses `ArrayPool<byte>.Shared` with larger initial buffers for tile-heavy packets. How well this works depends on your traffic patterns and concurrency.

**Multi-server routing**: Client→server lookup is O(1) through volatile-backed mapping. Transfer cost is mostly about sync steps and your event handlers.

### 2.5 Logging Infrastructure

The logging system is built for performance — `LogEntry` lives on the stack, metadata is pooled, and everything routes through pluggable writers with server-scoped output. The `Logger` implementation also keeps a bounded in-memory history ring so sinks can replay recent entries before joining live writes, and the launcher can attach an async durable writer that drains to `txt` or `sqlite` sinks in the background.

<details>
<summary><strong>Expand Logging Infrastructure implementation deep dive</strong></summary>

#### Logger Architecture

**Logger** (src/UnifierTSL/Logging/Logger.cs) - Core logging engine:

```csharp
public class Logger
{
    public ILogFilter Filter { get; set; } = EmptyLogFilter.Instance;
    public ILogWriter Writer { get; set; } = ConsoleLogWriter.Instance;
    private ImmutableArray<ILogMetadataInjector> MetadataInjectors = [];

    public void Log(ref LogEntry entry)
    {
        // 1. Apply metadata injectors
        foreach (var injector in MetadataInjectors) {
            injector.InjectMetadata(ref entry);
        }

        // 2. Filter check
        if (!Filter.ShouldLog(in entry)) return;

        // 3. Write
        Writer.Write(in entry);
    }
}
```

**LogEntry** (src/UnifierTSL/Logging/LogEntry.cs) - Structured log event:
```csharp
public ref struct LogEntry
{
    public string Role { get; init; }           // Logger scope (e.g., "TShockAPI", "Log")
    public string? Category { get; init; }      // Sub-category (e.g., "ConnectionAccept")
    public string Message { get; init; }
    public LogLevel Level { get; init; }
    public DateTime Timestamp { get; init; }
    public Exception? Exception { get; init; }
    public LogEventId? EventId { get; init; }
    public ref readonly TraceContext TraceContext { get; }

    private MetadataCollection metadata;  // ArrayPool-backed sorted collection

    public void SetMetadata(string key, string value) {
        metadata.Set(key, value);  // Binary search insert
    }
}
```

#### RoleLogger - Scoped Logging

**RoleLogger** (src/UnifierTSL/Logging/RoleLogger.cs) wraps `Logger` with host context:

```csharp
public class RoleLogger
{
    private readonly Logger logger;
    private readonly ILoggerHost host;
    private ImmutableArray<ILogMetadataInjector> injectors = [];

    public void Log(LogLevel level, string message, ReadOnlySpan<KeyValueMetadata> metadata = default)
    {
        // 1. Create entry
        MetadataAllocHandle allocHandle = logger.CreateMetadataAllocHandle();
        LogEntry entry = new(host.Name, message, level, ref allocHandle);

        // 2. Apply manual metadata
        foreach (var kv in metadata) {
            entry.SetMetadata(kv.Key, kv.Value);
        }

        // 3. Apply RoleLogger injectors
        foreach (var injector in injectors) {
            injector.InjectMetadata(ref entry);
        }

        // 4. Delegate to Logger
        logger.Log(ref entry);

        // 5. Cleanup
        allocHandle.Free();
    }
}
```

**Extension Methods** (src/UnifierTSL/Logging/LoggerExt.cs):
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

#### Metadata Management

**MetadataCollection** (src/UnifierTSL/Logging/Metadata/MetadataCollection.cs) - Sorted key-value storage:

```csharp
public ref struct MetadataCollection
{
    private Span<KeyValueMetadata> _entries;  // ArrayPool buffer
    private int _count;

    public void Set(string key, string value)
    {
        // Binary search for insertion point
        int index = BinarySearch(key);
        if (index >= 0) {
            // Key exists - update value
            _entries[index] = new(key, value);
        } else {
            // Key doesn't exist - insert at ~index
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

        // Grow via ArrayPool
        int newCapacity = _entries.Length == 0 ? 4 : _entries.Length * 2;
        Span<KeyValueMetadata> newBuffer = ArrayPool<KeyValueMetadata>.Shared.Rent(newCapacity);
        _entries.CopyTo(newBuffer);
        ArrayPool<KeyValueMetadata>.Shared.Return(_entries.ToArray());
        _entries = newBuffer;
    }
}
```

**MetadataAllocHandle** (src/UnifierTSL/Logging/Metadata/MetadataAllocHandle.cs) - Allocation manager:
```csharp
public unsafe struct MetadataAllocHandle
{
    private delegate*<int, Span<KeyValueMetadata>> _allocate;
    private delegate*<Span<KeyValueMetadata>, void> _free;

    public Span<KeyValueMetadata> Allocate(int capacity) => _allocate(capacity);
    public void Free(Span<KeyValueMetadata> buffer) => _free(buffer);
}
```

#### ConsoleLogWriter - Color-Coded Output

**ConsoleLogWriter** (src/UnifierTSL/Logging/LogWriters/ConsoleLogWriter.cs) - Server-routed console output:

```csharp
public class ConsoleLogWriter : ILogWriter
{
    public void Write(in LogEntry raw)
    {
        // 1. Check for server-specific routing
        if (raw.TryGetMetadata("ServerContext", out string? serverName)) {
            ServerContext? server = UnifiedServerCoordinator.Servers
                .FirstOrDefault(s => s.Name == serverName);

            if (server is not null) {
                WriteToConsole(server.Console, raw);
                return;
            }
        }

        // 2. Write to global console
        WriteToConsole(Console, raw);
    }

    private static void WriteToConsole(IConsole console, in LogEntry raw)
    {
        lock (SynchronizedGuard.ConsoleLock) {
            // Format segments with color codes
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

**Color Mapping**:
| LogLevel | Level Text | Foreground Color |
|----------|-----------|------------------|
| Trace | `[Trace]` | Gray |
| Debug | `[Debug]` | Blue |
| Info | `[+Info]` | White |
| Success | `[Succe]` | Green |
| Warning | `[+Warn]` | Yellow |
| Error | `[Error]` | Red |
| Critical | `[Criti]` | DarkRed |
| (Unknown) | `[+-·-+]` | White |

**Output Format** (src/UnifierTSL/Logging/Formatters/ConsoleLog/DefConsoleFormatter.cs:13-71):

Single-line message:
```
[Level][Role|Category] Message
```

Multi-line message:
```
[Level][Role|Category] First line
 │   Second line
 └── Last line
```

With exception (Handled - Level ≤ Warning):
```
[Level][Role|Category] Message
 │ Handled Exception:
 │   Exception line 1
 │   Exception line 2
 └── Exception line N
```

With exception (Unexpected - Level > Warning):
```
[Level][Role|Category] Message
 │ Unexpected Exception:
 │   Exception line 1
 │   Exception line 2
 └── Exception line N
```

**Segment Structure**:
- **Segment 0**: Level text (colored by level)
- **Segment 1**: Role/Category text (cyan foreground, black background)
- **Segment 2**: Main message with box-drawing characters for multi-line (colored by level)
- **Segment 3** (optional): Exception details with box-drawing characters (red foreground, white background)

#### TraceContext - Request Correlation

**TraceContext** (src/UnifierTSL/Logging/LogTrace/TraceContext.cs) - Distributed tracing context:

```csharp
[StructLayout(LayoutKind.Explicit, Size = 40)]
public readonly struct TraceContext(Guid correlationId, TraceId traceId, SpanId spanId)
{
    [FieldOffset(00)] public readonly Guid CorrelationId = correlationId;  // 16 bytes
    [FieldOffset(16)] public readonly TraceId TraceId = traceId;           // 8 bytes (ulong)
    [FieldOffset(32)] public readonly SpanId SpanId = spanId;              // 8 bytes (ulong)
}
```

**Usage**:
```csharp
TraceContext trace = new(
    Guid.NewGuid(),                                 // Unique request ID
    new TraceId((ulong)DateTime.UtcNow.Ticks),     // Logical trace chain
    new SpanId((ulong)Thread.CurrentThread.ManagedThreadId)  // Operation span
);

logger.Log(LogLevel.Info, "Player authenticated", in trace,
    metadata: stackalloc[] { new("PlayerId", playerId.ToString()) });
```

#### ServerContext Integration

**ServerContext** (src/UnifierTSL/Servers/ServerContext.cs) implements both `ILoggerHost` and `ILogMetadataInjector`:

```csharp
public partial class ServerContext : RootContext, ILoggerHost, ILogMetadataInjector
{
    public readonly RoleLogger Log;
    public string? CurrentLogCategory { get; set; }
    string ILoggerHost.Name => "Log";

    public ServerContext(...) {
        Log = UnifierApi.CreateLogger(this, overrideLogCore);
        Log.AddMetadataInjector(injector: this);  // Register self
    }

    void ILogMetadataInjector.InjectMetadata(scoped ref LogEntry entry) {
        entry.SetMetadata("ServerContext", this.Name);  // Auto-inject server name
    }
}
```

**Per-Server Routing**:
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

#### Performance Notes

**Overhead**: Logging is allocation-light — `LogEntry` is stack-based, metadata is pool-backed. Actual overhead depends on how much metadata you attach, your formatter, and which sink you're writing to.

**Memory**: Metadata buffers grow on demand and get reused. Keep your metadata sets small to avoid resize churn.

**Throughput**: In practice, throughput is limited by your sink. Console output is much slower than buffered file writes. If you have production latency targets, benchmark with your actual sink and log volume.

#### Best Practices

1. **Use metadata, not string interpolation**:
   ```csharp
   // Bad: Creates string allocation
   logger.Info($"Player {playerId} joined");

   // Good: Uses structured metadata
   logger.Info("Player joined", metadata: stackalloc[] {
       new("PlayerId", playerId.ToString())
   });
   ```

2. **Scope your log categories**:
   ```csharp
   // Set category for block of related logs
   serverContext.CurrentLogCategory = "WorldGen";
   GenerateWorld();
   serverContext.CurrentLogCategory = null;
   ```

3. **Add custom metadata injectors** for correlation:
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

4. **Log exceptions properly**:
   ```csharp
   try {
       DangerousOperation();
   }
   catch (Exception ex) {
       logger.LogHandledException("Operation failed", ex, category: "DangerousOperation");
       // Exception details auto-formatted in console output
   }
   ```

For more logging examples, see `src/Plugins/TShockAPI/TShock.cs` and `src/UnifierTSL/Servers/ServerContext.cs`.

### 2.6 Configuration Service
- `ConfigRegistrar` implements `IPluginConfigRegistrar`. In the built-in .NET host, plugin config root is `Path.Combine("config", Path.GetFileNameWithoutExtension(container.Location.FilePath))` (for example `config/TShockAPI`).
- `CreateConfigRegistration<T>` gives you a `ConfigRegistrationBuilder` where you set defaults, serialization options, error policies (`DeserializationFailureHandling`), and external-change triggers.
- You get back a `ConfigHandle<T>` that lets you `RequestAsync`, `Overwrite`, `ModifyInMemory`, and subscribe via `OnChangedAsync` for hot reload. File access is guarded by `FileLockManager` to prevent corruption.
- Launcher root config is separate from plugin config: `LauncherConfigManager` owns `config/config.json`, creates it when missing, and intentionally ignores the legacy root-level `config.json`.
- Startup precedence for launcher settings is `config/config.json` -> CLI overrides -> interactive fallback for missing port/password, and the effective startup snapshot is persisted back to `config/config.json`. After startup, edits to `config/config.json` apply to the launcher settings that support reload.
- Root-config hot reload applies `launcher.serverPassword`, `launcher.joinServer`, additive `launcher.autoStartServers`, and `launcher.listenPort` (via listener rebind).

## 3. USP Integration Points

- `ServerContext` inherits USP's `RootContext`, plugging Unifier services into the context (custom console, packet receiver, logging metadata). Everything that touches Terraria world/game state goes through this context.
- The networking patcher (`UnifiedNetworkPatcher`) detours `NetplaySystemContext` functions to share buffers and coordinate send/receive paths across servers.
- MonoMod `On.` detours are a valid tool for niche/cold hooks. For common or contested hook points, prefer exposing/using `EventHub` providers so plugins can share handler-level ordering/filtering instead of stacking plugin-local detours.
- Unifier directly uses TrProtocol packet structs/interfaces from the USP runtime (`INetPacket`, `IManagedPacket`, `ISideSpecific`, etc.), and `PacketSender`/`NetPacketHandler` follow TrProtocol read/write contracts.

## 4. Public API Surface

### 4.1 Facade (`UnifierApi`)
- The runtime boots through launcher entrypoints (`Program.cs`) via `HandleCommandLinePreRun`, `PrepareRuntime`, `InitializeCore`, and `CompleteLauncherInitialization`, then turns on `StartRootConfigMonitoring` after the coordinator is live — these are internal startup APIs, not something your plugin calls.
- `EventHub` gives you access to all grouped event providers once init is done.
- `EventHub.Launcher.InitializedEvent` fires when launcher arguments are finalized (including interactive fallback), right before the coordinator starts; root config file watching begins only after `UnifiedServerCoordinator.Launch(...)` succeeds.
- `PluginHosts` lazily sets up the `PluginOrchestrator` for host-level interactions.
- `CreateLogger(ILoggerHost, Logger? overrideLogCore = null)` gives you a `RoleLogger` scoped to your plugin, reusing the shared `Logger`.
- `UpdateTitle(bool empty = false)` controls the window title based on coordinator state.
- `VersionHelper` and `LogCore`/`Logger` provide shared utilities (version info, logging core).

### 4.2 Event Payloads & Helpers
- Event payload structs live under `src/UnifierTSL/Events/*` and implement `IEventContent`. The specialised interfaces (`IPlayerEventContent`, `IServerEventContent`) add context like server or player info.
- `HandlerPriority` and `FilterEventOption` control invocation order and filtering.
- Registration/unregistration helpers are thread-safe and allocation-light.

### 4.3 Module & Plugin Types
- `ModulePreloadInfo`, `ModuleLoadResult`, `LoadedModule` describe module metadata and lifecycle.
- `IPlugin`, `BasePlugin`, `PluginContainer`, `PluginInitInfo`, and `IPluginHost` define plugin contracts and containers.
- Configuration surface includes `IPluginConfigRegistrar`, `ConfigHandle<T>`, and `ConfigFormat` enumeration.

### 4.4 Networking APIs
- `PacketSender` exposes fixed/dynamic packet send helpers plus server-side variants.
- `NetPacketHandler` offers `Register<TPacket>`, `UnRegister<TPacket>`, `ProcessBytes`, and packet callbacks over `ReceivePacketEvent<T>`.
- `LocalClientSender` wraps a `RemoteClient`, exposing `Kick`, `SendData`, and `Client` metadata. `ClientPacketReceiver` replays or rewrites inbound packets.
- Coordinator helpers provide `TransferPlayerToServer`, `SwitchJoinServerEvent`, and state queries (`GetClientCurrentlyServer`, `Servers` list).

### 4.5 Logging & Diagnostics
- `RoleLogger` extension methods (see `src/UnifierTSL/Logging/LoggerExt.cs`) give you severity helpers: `Debug`, `Info`, `Warning`, `Error`, `Success`, `LogHandledException`.
- `LogEventIds` lists standard event identifiers for categorising log output.
- `Logger.ReplayHistory(...)` and `LogCore.AttachHistoryWriter(...)` let new sinks catch up from the in-memory history ring before they start receiving live writes.
- Event providers expose `HandlerCount`, and you can enumerate `EventProvider.AllEvents` for diagnostics dashboards.

## 5. Runtime Lifecycle & Operations

### 5.1 Startup Sequence
1. `HandleCommandLinePreRun` applies pre-run language overrides and locks in the active Terraria culture.
2. `PrepareRuntime` parses launcher CLI overrides, loads `config/config.json`, merges startup settings, and configures the durable logging backend.
3. `ModuleAssemblyLoader.Load` scans `plugins/`, stages assemblies, and handles dependency extraction.
4. Plugin hosts find eligible entry points and instantiate `IPlugin` implementations.
5. `BeforeGlobalInitialize` runs synchronously on every plugin — use it for cross-plugin service wiring.
6. `InitializeAsync` runs for each plugin; you get prior plugin init tasks so you can await your dependencies.
7. `InitializeCore` wires `EventHub`, finishes plugin host initialization, and applies the resolved launcher defaults (join policy + queued auto-start worlds).
8. `CompleteLauncherInitialization` prompts for any still-missing port/password, syncs the effective runtime snapshot, and fires `EventHub.Launcher.InitializedEvent`.
9. `UnifiedServerCoordinator.Launch(...)` binds the shared listener, starts the configured worlds, and registers the live coordinator loops.
10. `StartRootConfigMonitoring()` begins watching `config/config.json`; then `Program.Run()` updates the title, logs startup success, and fires `EventHub.Coordinator.Started`.

**A few notes on launcher args**:
- Language precedence: `UTSL_LANGUAGE` env var is applied before CLI parsing and blocks later `-lang` / `-culture` / `-language` overrides.
- `-server` / `-addserver` / `-autostart` parse server descriptors during `PrepareRuntime`; merge behavior is controlled by `-servermerge` (`replace` default, `overwrite`, `append`) and the effective startup list is persisted.
- `-joinserver` sets the launcher's low-priority default join mode (`random|rnd|r` or `first|f`) inside a permanent resolver; later root-config reloads can replace that mode without re-registering handlers.
- `-logmode` / `--log-mode` selects the durable log backend (`txt`, `none`, or `sqlite`).
- `UnifierApi.CompleteLauncherInitialization()` prompts for any missing port/password, then fires `EventHub.Launcher.InitializedEvent`.
- `Program.Run()` launches the coordinator, enables root config monitoring, logs success, then fires `EventHub.Coordinator.Started`.

### 5.2 Runtime Operations
- Event handlers handle cross-cutting concerns — chat moderation, transfer control, packet filtering, etc.
- Config handles react to file changes, so you can tweak settings without restarting.
- The launcher root config watcher applies password changes, join-policy changes, additive auto-start worlds (hot-add only), and `launcher.listenPort` listener rebinding.
- The coordinator keeps window titles updated, maintains server lists, replays join/leave sequences, and can swap the active listener without tearing down the process.
- Logging metadata and the bounded history ring let you trace any log entry back to its server, plugin, or subsystem, and attach new sinks without losing recent context.
- Durable backends (`txt` / `sqlite`) now run on a background consumer queue; `none` bypasses durable history commits entirely to keep the hot path minimal.

### 5.3 Shutdown & Reload
- `PluginOrchestrator` exposes `ShutdownAllAsync` and `UnloadAllAsync`, though the built-in `DotnetPluginHost` still has TODO stubs for `ShutdownAsync`/`UnloadPluginsAsync`.
- Module reload and targeted unload work through loader APIs (`ModuleAssemblyLoader.TryLoadSpecific`, `ForceUnload`) and plugin-loader ops (`TryUnloadPlugin`/`ForceUnloadPlugin`).
- Plugin `DisposeAsync` hooks into `ModuleLoadContext` unload via registered dispose actions.

### 5.4 Diagnostics
- Event providers expose handler counts for observability — enumerate `EventProvider.AllEvents` to build dashboards.
- `PacketSender.SentPacket`, `NetPacketHandler.ProcessPacketEvent`, and per-packet `PacketProcessed` callbacks are great for traffic metrics and tracing.
- Logging metadata injectors give you per-server/per-plugin tags for filtering in external sinks.

## 6. Extensibility Guidelines & Best Practices

- **Use event providers first** — before adding MonoMod detours in your plugin, check if an `EventHub` provider already exists (or add one to the core so others can benefit too).
- **Stay within context boundaries** — always go through `ServerContext` and USP context APIs to avoid cross-server bugs.
- **Declare your dependencies** — if you're shipping modules with native/managed deps, use `ModuleDependenciesAttribute` so the loader can track and clean up properly.
- **No async in event handlers** — `ref struct` args can't be captured in closures or async methods. Grab what you need, then schedule async work separately.
- **Await your dependencies explicitly** — use `PluginInitInfo` to await prerequisite plugins instead of just hoping the order works out.
- **Use the built-in logging** — create loggers via `UnifierApi.CreateLogger` so you get metadata injection and console formatting for free. Add `ILogMetadataInjector` for correlation data.
- **Write tests around events and coordinator flows** — simulate packet sequences, player joins, etc. USP contexts can run in isolation, which makes this pretty straightforward.
- **Batch registrations at startup** — event registration is thread-safe but not free. Use pooled buffers from the packet sender and keep allocations out of hot paths.
- **Build monitoring plugins with hooks** — `PacketSender.SentPacket` and event filters let you observe traffic without touching the core runtime.
