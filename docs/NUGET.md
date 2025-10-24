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
using UnifierTSL.PluginHost.Attributes;
using UnifierTSL;

[CoreModule]
[ModuleDependencies(
    nugetPackages: ["Newtonsoft.Json:13.0.3"]
)]
public class MyPlugin
{
    public void Initialize()
    {
        // Hook into UnifierApi.EventHub
        UnifierApi.EventHub.ServerStarted += OnServerStarted;
    }

    private void OnServerStarted(object? sender, ServerEventArgs args)
    {
        Console.WriteLine($"Server {args.ServerName} started!");
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
var config = UnifierApi.GetPluginConfig<MyConfig>("MyPlugin");
config.TriggerReloadOnExternalChange(true); // Enable hot reload
```

**Logging**
```csharp
var logger = UnifierApi.CreateLogger("MyPlugin");
logger.Info("Plugin initialized");
```

**Event Hub**
Access global events through `UnifierApi.EventHub`:
- `ServerStarted`, `ServerStopped`
- `PlayerJoining`, `PlayerLeft`
- Custom event registration

### 3. Build and Deploy

Build your plugin as a class library targeting `net9.0`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="UnifierTSL" Version="*" />
  </ItemGroup>
</Project>
```

Drop the compiled DLL into the server's `plugins/` directory.

## Package Contents

This NuGet package includes:
- `UnifierTSL.dll` - Core launcher and plugin framework
- `UnifierTSL.ConsoleClient.dll` - Console isolation client

## Documentation

- [Plugin Development Guide](https://github.com/CedaryCat/UnifierTSL/blob/main/docs/dev-plugin.md)
- [Developer Overview](https://github.com/CedaryCat/UnifierTSL/blob/main/docs/dev-overview.md)
- [Full README](https://github.com/CedaryCat/UnifierTSL/blob/main/README.md)

## Requirements

- .NET 9.0 or later
- OTAPI USP 1.0.13+
- Supported platforms: Windows, Linux (x64/ARM), macOS

## License

GPL-3.0 - See [LICENSE](https://github.com/CedaryCat/UnifierTSL/blob/main/LICENSE) for details

## Links

- [GitHub Repository](https://github.com/CedaryCat/UnifierTSL)
- [Issue Tracker](https://github.com/CedaryCat/UnifierTSL/issues)
- [OTAPI USP](https://github.com/CedaryCat/OTAPI.UnifiedServerProcess)
