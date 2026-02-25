# UnifierTSL

An experiment-friendly Terraria server launcher built on OTAPI USP, bundling per-instance consoles, early publishing helpers, and plugin scaffolding.

## What is UnifierTSL?

UnifierTSL is a plugin framework for Terraria servers that enables:
- Multi-world hosting in a single process
- Hot-reloadable plugin system with dependency management
- Shared event hub for cross-server coordination
- Dedicated console isolation per server instance

## Installation

```bash
dotnet add package UnifierTSL
```

## Quick Start for Plugin Developers

### 1. Create Your Plugin

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

namespace MyPlugin
{
    [PluginMetadata("MyPlugin", "1.0.0", "Author", "My awesome plugin")]
    public sealed class Plugin : BasePlugin, ILoggerHost
    {
        readonly RoleLogger logger;

        public Plugin()
        {
            logger = UnifierApi.CreateLogger(this);
        }

        public string Name => "MyPlugin";
        public string? CurrentLogCategory => null;

        public override Task InitializeAsync(
            IPluginConfigRegistrar registrar,
            ImmutableArray<PluginInitInfo> prior,
            CancellationToken cancellationToken = default)
        {
            // Hook into UnifierApi.EventHub
            UnifierApi.EventHub.Chat.MessageEvent.Register(OnChatMessage, HandlerPriority.Normal);
            logger.Info("MyPlugin initialized!");
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
                return;

            if (args.Content.Text.Trim().Equals("!hello", StringComparison.OrdinalIgnoreCase))
            {
                args.Content.Sender.Chat("Hello from MyPlugin!", Color.LightGreen);
                args.Handled = true;
            }
        }
    }
}
```

### 2. Key Features for Plugin Authors

**Module System**
- `[CoreModule]`: Mark your main plugin assembly
- `[RequiresCoreModule("Name")]`: Create dependent modules
- `[ModuleDependencies]`: Declare NuGet or embedded dependencies

**Configuration Management**
```csharp
var configHandle = registrar
    .CreateConfigRegistration<MyConfig>("config.json")
    .WithDefault(() => new MyConfig { Enabled = true })
    .TriggerReloadOnExternalChange(true)
    .Complete();

MyConfig config = await configHandle.RequestAsync(cancellationToken);
```

**Logging**
```csharp
// Plugin should implement ILoggerHost
var logger = UnifierApi.CreateLogger(this);
logger.Info("Plugin initialized");
logger.Warning("This is a warning");
logger.Error("An error occurred", exception: ex);
```

**Event Hub**
Access event providers through `UnifierApi.EventHub` domains:
- `Game`: `PreUpdate`, `PostUpdate`, `GameHardmodeTileUpdate`
- `Chat`: `ChatEvent`, `MessageEvent`
- `Netplay`: `ConnectEvent`, `ReceiveFullClientInfoEvent`, `LeaveEvent`
- `Coordinator`: `SwitchJoinServerEvent`, `PreServerTransfer`, `PostServerTransfer`
- `Server`: `CreateConsoleService`, `AddServer`, `RemoveServer`

### 3. Build and Deploy

Build your plugin as a class library targeting `net9.0`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="UnifierTSL" Version="*" />
  </ItemGroup>
</Project>
```

Drop the compiled DLL into the server's `plugins/` directory.

## Package Contents

This NuGet package includes:
- **Core Runtime**: `UnifierTSL.dll` - Launcher, orchestration services, module loader, event hub, and plugin host
- **TrProtocol**: Network packet definitions (IL-merged from USP) for type-safe packet handling
- **Event System**: Zero-allocation, priority-ordered event providers with handler management
- **Module System**: Collectible assembly loading with hot-reload support and automatic dependency extraction
- **Logging Infrastructure**: Structured logging with metadata injection and per-server console routing
- **Configuration Service**: Hot-reloadable config management with file watching and error policies

## Documentation

- [Plugin Development Guide](https://github.com/CedaryCat/UnifierTSL/blob/main/docs/dev-plugin.md)
- [Developer Overview](https://github.com/CedaryCat/UnifierTSL/blob/main/docs/dev-overview.md)
- [Full README](https://github.com/CedaryCat/UnifierTSL/blob/main/README.md)

## Requirements

- .NET 9.0 or later
- OTAPI USP 1.1.0+
- Supported platforms: Windows, Linux (x64/ARM), macOS

## License

GPL-3.0 - See [LICENSE](https://github.com/CedaryCat/UnifierTSL/blob/main/LICENSE) for details

## Links

- [GitHub Repository](https://github.com/CedaryCat/UnifierTSL)
- [Issue Tracker](https://github.com/CedaryCat/UnifierTSL/issues)
- [OTAPI USP](https://github.com/CedaryCat/OTAPI.UnifiedServerProcess)
