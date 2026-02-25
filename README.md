# UnifierTSL

> Languages: [English](./README.md) | [简体中文](./docs/README.zh-cn.md)

<p align="center">
  <img src="./docs/assets/readme/hero.svg" alt="UnifierTSL" width="100%">
</p>

<p align="center">
  <a href="#-quick-start"><img alt="Quick Start" src="https://img.shields.io/badge/Quick_Start-blue?style=flat-square"></a>
  <a href="https://github.com/CedaryCat/UnifierTSL/releases"><img alt="Releases" src="https://img.shields.io/badge/Releases-green?style=flat-square&logo=github"></a>
  <a href="./docs/dev-plugin.md"><img alt="Plugin Guide" src="https://img.shields.io/badge/Plugin_Guide-orange?style=flat-square"></a>
  <a href="#-architecture"><img alt="Architecture" src="https://img.shields.io/badge/Architecture-purple?style=flat-square"></a>
</p>

<p align="center">
  <a href="https://github.com/CedaryCat/UnifierTSL/actions/workflows/build.yaml"><img alt="Build" src="https://img.shields.io/github/actions/workflow/status/CedaryCat/UnifierTSL/build.yaml?branch=main&label=build&style=flat-square"></a>
  <a href="https://github.com/CedaryCat/UnifierTSL/actions/workflows/docs-check.yaml"><img alt="Docs Check" src="https://img.shields.io/github/actions/workflow/status/CedaryCat/UnifierTSL/docs-check.yaml?label=docs&style=flat-square"></a>
  <a href="./src/UnifierTSL.slnx"><img alt=".NET 9.0" src="https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet&logoColor=white"></a>
  <a href="./LICENSE"><img alt="License: GPL-3.0" src="https://img.shields.io/badge/License-GPL--3.0-green?style=flat-square"></a>
</p>

<p align="center">
  <em>Experiment-friendly Terraria server launcher based on OTAPI Unified Server Process,<br>with per-instance consoles, context-aware plugin hosting, and publisher tooling.</em>
</p>

---

<p align="center">
  <img src="./docs/assets/readme/quick-glance.svg" alt="Quick Overview" width="100%">
</p>

## 📑 Table of Contents

- [Overview](#-overview)
- [Core Capabilities](#-core-capabilities)
- [Version Matrix](#-version-matrix)
- [Architecture](#-architecture)
- [Quick Start](#-quick-start)
- [Launcher Reference](#-launcher-reference)
- [Publisher Reference](#-publisher-reference)
- [Project Layout](#-project-layout)
- [Plugin System](#-plugin-system)
- [Developer Guide](#-developer-guide)
- [Resources](#-resources)

---

## 📖 Overview

UnifierTSL wraps [OTAPI Unified Server Process](https://github.com/CedaryCat/OTAPI.UnifiedServerProcess) into a friendlier operational workflow for hosting **multiple Terraria worlds on a single host process**.

The launcher coordinates world lifecycle, routes joining players, and spawns dedicated console clients per world context — so each world's I/O stays completely isolated.

### What's in the repo

The workspace bundles everything you need in one solution:

| Component | Purpose |
|:--|:--|
| **Launcher** (`UnifierTSL`) | Runtime entry point — boots worlds, routes players, manages contexts |
| **Console Client** (`UnifierTSL.ConsoleClient`) | Per-world console process, connected via named pipes |
| **Publisher** (`UnifierTSL.Publisher`) | Produces RID-targeted output trees for deployment |
| **Plugins** (`src/Plugins/`) | Maintained examples and bundled modules (TShockAPI, CommandTeleport, etc.) |

Shared services are centered around `UnifiedServerCoordinator`, event traffic runs through `UnifierApi.EventHub`, and plugin loading is orchestrated by `PluginHost.PluginOrchestrator`.

---

## ✨ Core Capabilities

| Feature | Description |
|:--|:--|
| 🖥 **Multi-world coordination** | Launch and isolate multiple worlds in one runtime process |
| 🔀 **Live routing control** | Register default join strategies and re-route players through coordinator events |
| 🔌 **Plugin hosting** | Load .NET modules from `plugins/` with config registration and dependency extraction |
| 📦 **Collectible module contexts** | `ModuleLoadContext` for unloadable plugin domains and staged dependency handling |
| 📝 **Shared logging pipeline** | `UnifierApi.LogCore` supports custom filters, writers, and metadata injectors |
| 🛡 **Bundled TShock port** | Includes USP-adapted TShock baseline for drop-in use |
| 💻 **Per-context console isolation** | Console client processes spawned via named pipe protocol |
| 🚀 **RID-targeted publishing** | Publisher outputs reproducible, runtime-specific directory trees |

---

## 📊 Version Matrix

Current versions sourced from project files in this repository:

| Component | Version | Source |
|:--|:--|:--|
| Target framework | `.NET 9.0` | `src/UnifierTSL/*.csproj` |
| OTAPI USP | `1.1.0-pre-release-upstream.24` | `UnifierTSL.csproj` |
| ModFramework | `1.1.15` | `UnifierTSL.csproj` |
| MonoMod.RuntimeDetour | `25.2.3` | `UnifierTSL.csproj` |
| Tomlyn | `0.19.0` | `UnifierTSL.csproj` |
| linq2db | `5.4.1` | `UnifierTSL.csproj` |
| Microsoft.Data.Sqlite | `9.0.0` | `UnifierTSL.csproj` |

<details>
<summary><strong>TShock sync metadata</strong></summary>

| Item | Value |
|:--|:--|
| Bundled TShock version | `5.9.9` |
| Sync branch | `general-devel` |
| Sync commit | `cd68321fcc7b7b2a02d8ed6449910c4763b45350` |
| Source | `src/Plugins/TShockAPI/TShockAPI.csproj` |

</details>

---

## 🏗 Architecture

<p align="center">
  <img src="./docs/assets/readme/arch-flow.svg" alt="Architecture flow" width="100%">
</p>

The runtime coordination flow follows a linear pipeline:

```mermaid
graph LR
    A["Program.cs"] -->|init| B["UnifierApi"]
    B -->|coordinate| C["UnifiedServerCoordinator"]
    C -->|spawn| D["ServerContext"]
    D -->|load| E["PluginHost"]

    style A fill:#EFF6FF,stroke:#3B82F6,color:#1E293B
    style B fill:#F0FDF4,stroke:#16A34A,color:#1E293B
    style C fill:#FFF7ED,stroke:#EA580C,color:#1E293B
    style D fill:#FAF5FF,stroke:#9333EA,color:#1E293B
    style E fill:#EFF6FF,stroke:#3B82F6,color:#1E293B
```

<details>
<summary><strong>Key runtime responsibilities</strong></summary>

| Component | Responsibilities |
|:--|:--|
| `Program.cs` | Starts the launcher and runtime bootstrap |
| `UnifierApi` | Initializes event hub, plugin orchestration, and launcher argument handling |
| `UnifiedServerCoordinator` | Owns listening socket, client coordination, and world routing |
| `ServerContext` | Keeps each hosted world state isolated |
| `PluginHost` + module loader | Plugin discovery, loading, and dependency staging |

</details>

### Pick Your Path

| Role | Start Here | Why |
|:--|:--|:--|
| 🖥 Server operator | [Quick Start ↓](#-quick-start) | Get a running multi-world host with minimum setup |
| 🔌 Plugin developer | [Plugin Development Guide](./docs/dev-plugin.md) | Implement and migrate modules with config/events/deps workflow |

---

## 🚀 Quick Start

### Prerequisites

Choose the requirement set matching your workflow:

| Workflow | Requirements |
|:--|:--|
| **Release bundles only** | [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) on the target host |
| **From source / Publisher** | [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) + `msgfmt` in `PATH` (for `.mo` files) |

### Option A: Use a Release Bundle

**1.** Download the asset matching your platform from [GitHub Releases](https://github.com/CedaryCat/UnifierTSL/releases):

| Platform | File pattern |
|:--|:--|
| Windows | `utsl-<rid>-v<semver>.zip` |
| Linux / macOS | `utsl-<rid>-v<semver>.tar.gz` |

**2.** Extract and launch:

<details>
<summary><strong>Windows (PowerShell)</strong></summary>

```powershell
.\UnifierTSL.exe -lang 7 -port 7777 -password changeme `
  -server "name:S1 worldname:S1 gamemode:3 size:1 evil:0 seed:\"for the worthy\"" `
  -server "name:S2 worldname:S2 gamemode:2 size:2" `
  -joinserver first
```

</details>

<details>
<summary><strong>Linux / macOS</strong></summary>

```bash
chmod +x UnifierTSL
./UnifierTSL -lang 7 -port 7777 -password changeme \
  -server "name:S1 worldname:S1 gamemode:3 size:1 evil:0 seed:\"for the worthy\"" \
  -joinserver first
```

</details>

### Option B: Run from Source

Use this path for local debugging, CI integration, or custom bundle output.

**1.** Clone and restore:

```bash
git clone https://github.com/CedaryCat/UnifierTSL.git
cd UnifierTSL
dotnet restore src/UnifierTSL.slnx
```

**2.** Build:

```bash
dotnet build src/UnifierTSL.slnx -c Debug
```

**3.** *(Optional)* Produce local Publisher output:

```bash
dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- \
  --rid win-x64 \
  --excluded-plugins ExamplePlugin,ExamplePlugin.Features
```

**4.** Smoke test the launcher:

```bash
dotnet run --project src/UnifierTSL/UnifierTSL.csproj -- \
  -port 7777 -password changeme \
  -server "name:Dev worldname:Dev" \
  -joinserver first
```

> **Note**: Default Publisher output path is `src/UnifierTSL.Publisher/bin/<Configuration>/net9.0/utsl-<rid>/`.
> `UnifierTSL.ConsoleClient` is not intended to be launched manually — the launcher injects pipe arguments automatically.

---

## 🎮 Launcher Reference

### Command-Line Flags

| Flag(s) | Description | Accepted Values | Default |
|:--|:--|:--|:--|
| `-listen`, `-port` | Coordinator TCP port | Integer | Prompts on STDIN |
| `-password` | Shared client password | Any string | Prompts on STDIN |
| `-autostart`, `-addserver`, `-server` | Add server definitions | Repeatable `key:value` pairs | — |
| `-joinserver` | Default join strategy | `first` / `f` / `random` / `rnd` / `r` | — |
| `-culture`, `-lang`, `-language` | Override Terraria language | Legacy culture ID or name | Host culture |

> **Tip**: Use `-joinserver first` or `random` unless a plugin sets join behavior through `EventHub.Coordinator.SwitchJoinServer`.

### Server Definition Keys

Each `-server` value is a whitespace-separated set of `key:value` pairs parsed by `UnifierApi.AutoStartServer`:

| Key | Purpose | Accepted Values | Default |
|:--|:--|:--|:--|
| `name` | Friendly server identifier | Unique string | *Required* |
| `worldname` | World name to load/generate | Unique string | *Required* |
| `seed` | Generation seed | Any string | — |
| `gamemode` / `difficulty` | World difficulty | `0`–`3`, `normal`, `expert`, `master`, `creative` | `2` |
| `size` | World size | `1`–`3`, `small`, `medium`, `large` | `3` |
| `evil` | World evil type | `0`–`2`, `random`, `corruption`, `crimson` | `0` |

---

## 📦 Publisher Reference

### CLI Flags

| Flag | Description | Values | Default |
|:--|:--|:--|:--|
| `--rid` | Target runtime identifier | e.g. `win-x64`, `linux-x64`, `osx-x64` | *Required* |
| `--excluded-plugins` | Plugin projects to skip | Comma-separated or repeated | — |
| `--output-path` | Base output directory | Absolute or relative path | `src/.../bin/<Config>/net9.0` |
| `--use-rid-folder` | Append `utsl-<rid>` folder | `true` / `false` | `true` |
| `--clean-output-dir` | Clear existing output first | `true` / `false` | `true` |

Publisher builds framework-dependent outputs (`SelfContained=false`).

### Output Lifecycle

<details>
<summary><strong>Initial Publisher output (local)</strong></summary>

Publisher creates a directory tree (not an archive):

```
utsl-<rid>/
├── UnifierTSL(.exe)
├── UnifierTSL.pdb
├── app/
│   ├── UnifierTSL.ConsoleClient(.exe)
│   └── UnifierTSL.ConsoleClient.pdb
├── i18n/
├── lib/
├── plugins/
│   ├── TShockAPI.dll
│   ├── TShockAPI.pdb
│   ├── CommandTeleport.dll
│   └── CommandTeleport.pdb
└── runtimes/
```

</details>

<details>
<summary><strong>Runtime-reorganized plugin layout (after first boot)</strong></summary>

On startup, the module loader may rearrange plugin files into module folders based on attributes (`[CoreModule]`, `[RequiresCoreModule]`, dependency declarations):

```
plugins/
├── TShockAPI/
│   ├── TShockAPI.dll
│   ├── dependencies.json
│   └── lib/
└── CommandTeleport.dll

config/
├── TShockAPI/
└── CommandTeleport/
```

`dependencies.json` is generated/updated by the dependency staging logic during module loading.

</details>

<details>
<summary><strong>CI artifact and release naming</strong></summary>

GitHub Actions uses two naming layers:

| Layer | Pattern |
|:--|:--|
| Workflow artifacts | `utsl-<rid>-<semver>` |
| Release archives (Windows) | `utsl-<rid>-v<semver>.zip` |
| Release archives (Linux/macOS) | `utsl-<rid>-v<semver>.tar.gz` |

</details>

---

## 🗂 Project Layout

```mermaid
graph TD
    SLN["📄 UnifierTSL.slnx"] --> MAIN["📁 UnifierTSL/"]
    SLN --> CC["📁 ConsoleClient/"]
    SLN --> PUB["📁 Publisher/"]
    SLN --> PLUG["📁 Plugins/"]

    MAIN --> MOD["Module/"]
    MAIN --> PH["PluginHost/"]
    MAIN --> SRV["Servers/"]
    MAIN --> NET["Network/"]
    MAIN --> LOG["Logging/"]

    PLUG --> EX1["ExamplePlugin"]
    PLUG --> EX2["ExamplePlugin.Features"]
    PLUG --> CMD["CommandTeleport"]
    PLUG --> TS["TShockAPI"]

    style SLN fill:#EFF6FF,stroke:#3B82F6,color:#1E293B
    style MAIN fill:#F0FDF4,stroke:#16A34A,color:#1E293B
    style CC fill:#FFF7ED,stroke:#EA580C,color:#1E293B
    style PUB fill:#FAF5FF,stroke:#9333EA,color:#1E293B
    style PLUG fill:#FEF3C7,stroke:#D97706,color:#1E293B
```

| Directory | Description |
|:--|:--|
| `src/UnifierTSL/` | Runtime entry points and systems (`Module/`, `PluginHost/`, `Servers/`, `Network/`, `Logging/`) |
| `src/UnifierTSL.ConsoleClient/` | Per-context console client and named-pipe protocol |
| `src/UnifierTSL.Publisher/` | Local packaging logic |
| `src/Plugins/` | Maintained examples and bundled modules |
| `docs/` | Project documentation |

---

## 🔌 Plugin System

### Plugin Loading Flow

```mermaid
graph TD
    DROP["Drop .dll into plugins/"] --> DISC["Module loader discovers assemblies"]
    DISC --> CORE{"Has [CoreModule]?"}
    CORE -->|Yes| FOLDER["Create dedicated plugin folder"]
    CORE -->|No| REQ{"Has [RequiresCoreModule]?"}
    REQ -->|Yes| UNDER["Load under target core module context"]
    REQ -->|No| FLAT["Load as standalone module"]
    FOLDER --> DEP["Stage dependencies into lib/"]
    DEP --> CONF["Register configs under config/PluginName/"]

    style DROP fill:#EFF6FF,stroke:#3B82F6,color:#1E293B
    style DISC fill:#F1F5F9,stroke:#94A3B8,color:#1E293B
    style CORE fill:#FEF3C7,stroke:#D97706,color:#1E293B
    style REQ fill:#FEF3C7,stroke:#D97706,color:#1E293B
    style FOLDER fill:#F0FDF4,stroke:#16A34A,color:#1E293B
    style UNDER fill:#FAF5FF,stroke:#9333EA,color:#1E293B
    style FLAT fill:#F1F5F9,stroke:#94A3B8,color:#1E293B
    style DEP fill:#FFF7ED,stroke:#EA580C,color:#1E293B
    style CONF fill:#ECFDF5,stroke:#059669,color:#1E293B
```

### Key Concepts

| Concept | Description |
|:--|:--|
| **`[CoreModule]`** | Marks a module for its own dedicated folder and load context |
| **`[RequiresCoreModule("...")]`** | Loads this module under the specified core module's context |
| **Dependency staging** | Dependencies are extracted into `lib/` and tracked via `dependencies.json` |
| **Config registration** | Configs stored in `config/<PluginName>/`, supports auto-reload (`TriggerReloadOnExternalChange(true)`) |
| **Collectible contexts** | `ModuleLoadContext` enables unloadable plugin domains |

→ Full guide: [Plugin Development Guide](./docs/dev-plugin.md)

---

## 🛠 Developer Guide

### Common Commands

```bash
# Restore dependencies
dotnet restore src/UnifierTSL.slnx

# Build (Debug)
dotnet build src/UnifierTSL.slnx -c Debug

# Run launcher with test world
dotnet run --project src/UnifierTSL/UnifierTSL.csproj -- \
  -port 7777 -password changeme -joinserver first

# Produce publisher output for Windows x64
dotnet run --project src/UnifierTSL.Publisher/UnifierTSL.Publisher.csproj -- \
  --rid win-x64

# Run tests (when available)
dotnet test src/UnifierTSL.slnx
```

> **Note**: Automated tests are not yet included in the repository.

### Supported Platforms

| RID | Status |
|:--|:--|
| `win-x64` | ✅ Supported |
| `linux-x64` | ✅ Supported |
| `linux-arm64` | ✅ Supported |
| `linux-arm` | ✅ Supported |
| `osx-x64` | ✅ Supported |

---

## 📚 Resources

| Resource | Link |
|:--|:--|
| Developer Overview | [docs/dev-overview.md](./docs/dev-overview.md) |
| Plugin Development Guide | [docs/dev-plugin.md](./docs/dev-plugin.md) |
| OTAPI Unified Server Process | [GitHub](https://github.com/CedaryCat/OTAPI.UnifiedServerProcess) |
| Upstream TShock | [GitHub](https://github.com/Pryaxis/TShock) |
| DeepWiki AI Analysis | [deepwiki.com](https://deepwiki.com/CedaryCat/UnifierTSL) *(reference only)* |

---

<p align="center">
  <sub>Made with ❤️ by the UnifierTSL contributors · Licensed under GPL-3.0</sub>
</p>
